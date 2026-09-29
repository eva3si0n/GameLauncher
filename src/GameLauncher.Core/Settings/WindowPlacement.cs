namespace GameLauncher.Core.Settings;

/// <summary>Прямоугольник в физических пикселях экрана.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>Положение и размер окна (обычные, не развёрнутые) и признак «развёрнуто».</summary>
public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool IsMaximized)
{
    public const int MinWidth = 640;
    public const int MinHeight = 480;

    public PixelRect Bounds => new(X, Y, Width, Height);

    /// <summary>
    /// Вписывает окно в рабочую область монитора: размер — не больше области и не меньше минимального,
    /// положение — так, чтобы окно целиком было видно. Нужно, если с прошлого запуска сменилось разрешение
    /// или монитор.
    /// </summary>
    public PixelRect FitInto(PixelRect workArea)
    {
        var width = Math.Min(Math.Max(Width, MinWidth), workArea.Width);
        var height = Math.Min(Math.Max(Height, MinHeight), workArea.Height);
        var x = Math.Clamp(X, workArea.X, workArea.X + workArea.Width - width);
        var y = Math.Clamp(Y, workArea.Y, workArea.Y + workArea.Height - height);
        return new PixelRect(x, y, width, height);
    }
}
