"""Генерирует иконку приложения: src/GameLauncher.App/Assets/GameLauncher.ico и TitleBarIcon.png.

Геймпад — xbox_controller из Fluent UI System Icons (Microsoft, MIT, см. LICENSE-fluentui-system-icons):
https://github.com/microsoft/fluentui-system-icons. Мелкие SVG (16–32) нарисованы под пиксельную сетку,
поэтому на мелких кадрах берётся SVG родного размера без масштабирования.

Запуск из корня репозитория: pip install pillow cairosvg && python3 tools/icon/make_icon.py
"""
import io
from pathlib import Path

import cairosvg
from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent
OUT = HERE.parent.parent / "src" / "GameLauncher.App" / "Assets"
NATIVE = (16, 20, 24, 32)  # размеры, для которых есть свой SVG; остальное — из 48
S = 1024  # фон рисуется крупно и уменьшается — ровные края


def lerp(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def glyph(svg_size, px):
    """Белый геймпад px×px из SVG xbox_controller_{svg_size}_filled."""
    svg = (HERE / f"xbox_controller_{svg_size}_filled.svg").read_text().replace("#212121", "#FFFFFF")
    png = cairosvg.svg2png(bytestring=svg.encode(), output_width=px, output_height=px)
    return Image.open(io.BytesIO(png)).convert("RGBA")


def background(size):
    """Скруглённый квадрат, диагональный градиент синий → фиолетовый."""
    top, bottom = (56, 132, 255), (124, 58, 237)
    grad = Image.new("RGB", (S, S))
    px = grad.load()
    for y in range(S):
        for x in range(S):
            px[x, y] = lerp(top, bottom, (x + y) / (2 * S - 2))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=round(S * 0.22), fill=255)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    img.paste(grad, (0, 0), mask)
    return img.resize((size, size), Image.LANCZOS)


def tile(size):
    img = background(size)
    if size in NATIVE:
        # Поля уже заложены в SVG (контур ~1/16 от края).
        pad = glyph(size, size)
    else:
        pad = glyph(48, round(size * 0.9))
    g = pad.width
    off = ((size - g) // 2, (size - g) // 2)
    if size >= 48:
        # Лёгкая тень под геймпадом.
        a = Image.new("L", (size, size), 0)
        a.paste(pad.getchannel("A"), (off[0], off[1] + max(1, size // 64)))
        a = a.filter(ImageFilter.GaussianBlur(size * 0.02)).point(lambda v: v * 0.3)
        shadow = Image.new("RGBA", (size, size), (20, 10, 60, 0))
        shadow.putalpha(a)
        img = Image.alpha_composite(img, shadow)
    img.alpha_composite(pad, off)
    return img


sizes = [256, 64, 48, 40, 32, 24, 20, 16]
frames = [tile(s) for s in sizes]
frames[0].save(OUT / "GameLauncher.ico", sizes=[(s, s) for s in sizes], append_images=frames[1:])
# Строка заголовка: 16 px при масштабе до 400 %.
tile(64).save(OUT / "TitleBarIcon.png")
