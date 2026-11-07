using Windows.ApplicationModel.DataTransfer;

namespace TrayGit.Services;

public sealed class ClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        DataPackage data = new();
        data.SetText(text);
        Clipboard.SetContent(data);
        Clipboard.Flush();
    }
}
