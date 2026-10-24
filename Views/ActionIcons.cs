using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace TrayGit.Views;

/// <summary>Loads an action's SVG once and hands the same source to every row that shows it.</summary>
public static class ActionIcons
{
    private static readonly Dictionary<string, SvgImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? For(string path)
    {
        if (path.Length == 0)
        {
            return null;
        }

        if (!Cache.TryGetValue(path, out SvgImageSource? source))
        {
            // Rasterize at twice the displayed size so it stays sharp on high-DPI screens.
            source = new SvgImageSource(new Uri($"ms-appx:///{path}")) { RasterizePixelWidth = 32, RasterizePixelHeight = 32 };
            Cache[path] = source;
        }

        return source;
    }
}
