using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace GameLauncher.App.Services;

/// <summary>Загрузка картинок для карточек. Вызывать из UI-потока.</summary>
public static class ImageLoader
{
    /// <summary>Картинка из файла. Читается через поток, чтобы не держать файл и не получать устаревшую копию из кэша.</summary>
    public static async Task<BitmapImage?> FromFileAsync(string path, int decodeWidth)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            using var stream = new MemoryStream(bytes).AsRandomAccessStream();
            var image = new BitmapImage { DecodePixelWidth = decodeWidth };
            await image.SetSourceAsync(stream);
            return image;
        }
        catch (Exception)
        {
            // Файл удалён, недоступен или формат не поддерживается — покажем иконку exe.
            return null;
        }
    }

    /// <summary>Иконка exe (как в Проводнике), до 256 px.</summary>
    public static async Task<BitmapImage?> ExeIconAsync(string exePath)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(exePath);
            using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 256, ThumbnailOptions.ResizeThumbnail);
            if (thumbnail is null)
            {
                return null;
            }

            var image = new BitmapImage();
            await image.SetSourceAsync(thumbnail);
            return image;
        }
        catch (Exception)
        {
            // Нет файла, нет доступа или у exe нет иконки — покажем заглушку.
            return null;
        }
    }
}
