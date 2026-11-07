using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TrayGit.Core;
using TrayGit.Services;
using TrayGit.ViewModels;
using Windows.System;
using Windows.UI.Core;

namespace TrayGit.Views;

/// <summary>Settings view. Input capture and focus coordination stay here.</summary>
public sealed partial class SettingsPage : Page
{
    public SettingsPage(Action goBack)
    {
        ViewModel = new SettingsViewModel(App.Current, goBack);
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; }

    public void OnShown()
    {
        _ = ViewModel.OnShownAsync();
        BackButton.Focus(FocusState.Programmatic);
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.BrowseCommand.ExecuteAsync(null);
        BrowseButton.Focus(FocusState.Programmatic);
    }

    private void ActionsList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) =>
        ViewModel.SaveActionOrder();

    private void HotKeyToggle_Toggled(object sender, RoutedEventArgs e) =>
        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            ViewModel.RefreshHotKeyState);

    private void HotKeyButton_LostFocus(object sender, RoutedEventArgs e) =>
        ViewModel.CancelHotKeyCapture();

    private void HotKeyButton_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!ViewModel.IsCapturingHotKey)
        {
            return;
        }

        e.Handled = true;
        uint modifiers = 0;
        if (IsDown(VirtualKey.Control)) modifiers |= HotKey.Control;
        if (IsDown(VirtualKey.Menu)) modifiers |= HotKey.Alt;
        if (IsDown(VirtualKey.Shift)) modifiers |= HotKey.Shift;
        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows)) modifiers |= HotKey.Win;

        if (ViewModel.CaptureHotKey(
                e.Key == VirtualKey.Escape,
                (uint)e.Key,
                modifiers))
        {
            DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                ViewModel.RefreshHotKeyState);
        }
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
