using TrayGit.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TrayGit.Services;

/// <summary>
/// Registers one system-wide hotkey. WM_HOTKEY goes to a hidden message-only window, so nothing is
/// polled and the idle cost is a single registered key.
/// </summary>
internal sealed unsafe class GlobalHotKeyService : IDisposable
{
    private const uint WM_HOTKEY = 0x0312;
    private const int HotKeyId = 1;
    private const string WindowClassName = "TrayGit_HotKeyWindow";
    private static readonly HWND MessageOnlyParent = new(-3); // HWND_MESSAGE

    private readonly Action _pressed;
    private readonly WNDPROC _wndProc; // kept in a field so the native window can't outlive the delegate
    private HWND _hwnd;
    private bool _isRegistered;

    public GlobalHotKeyService(Action pressed)
    {
        _pressed = pressed;
        _wndProc = WndProc;
    }

    /// <summary>False when the hotkey is off or another app already owns the combination.</summary>
    public bool IsRegistered => _isRegistered;

    /// <summary>Registers <paramref name="hotKey"/>, replacing any earlier one. Returns false if Windows refuses it.</summary>
    public bool Register(HotKey hotKey)
    {
        Unregister();
        if (!hotKey.IsValid || !EnsureWindow())
        {
            return false;
        }

        // MOD_NOREPEAT: holding the keys down shouldn't toggle the flyout repeatedly.
        const uint ModNoRepeat = 0x4000;
        _isRegistered = PInvoke.RegisterHotKey(_hwnd, HotKeyId, (HOT_KEY_MODIFIERS)(hotKey.Modifiers | ModNoRepeat), hotKey.VirtualKey);
        return _isRegistered;
    }

    public void Unregister()
    {
        if (_isRegistered)
        {
            _ = PInvoke.UnregisterHotKey(_hwnd, HotKeyId);
            _isRegistered = false;
        }
    }

    public void Dispose()
    {
        Unregister();
        if (_hwnd != HWND.Null)
        {
            _ = PInvoke.DestroyWindow(_hwnd);
            _hwnd = HWND.Null;
        }
    }

    private bool EnsureWindow()
    {
        if (_hwnd != HWND.Null)
        {
            return true;
        }

        fixed (char* className = WindowClassName)
        {
            WNDCLASSW windowClass = new()
            {
                lpfnWndProc = _wndProc,
                lpszClassName = className,
            };
            PInvoke.RegisterClass(windowClass); // fails harmlessly if the class already exists

            _hwnd = PInvoke.CreateWindowEx(0, WindowClassName, null, 0, 0, 0, 0, 0, MessageOnlyParent, null, null, null);
        }

        return _hwnd != HWND.Null;
    }

    private LRESULT WndProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message == WM_HOTKEY && (int)wParam.Value == HotKeyId)
        {
            _pressed();
            return new LRESULT(0);
        }

        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }
}
