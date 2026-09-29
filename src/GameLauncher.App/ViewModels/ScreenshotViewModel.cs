using GameLauncher.Core.GameInfo;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace GameLauncher.App.ViewModels;

/// <summary>Миниатюра скриншота на странице игры. Картинка грузится из сети Steam при показе.</summary>
public sealed class ScreenshotViewModel(Screenshot screenshot, int index)
{
    private ImageSource? _thumbnail;

    public Screenshot Screenshot { get; } = screenshot;

    public int Index { get; } = index;

    public ImageSource Thumbnail => _thumbnail ??= new BitmapImage(Screenshot.Thumbnail) { DecodePixelWidth = 320 };
}
