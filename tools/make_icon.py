"""Генерирует иконку приложения: src/GameLauncher.App/Assets/GameLauncher.ico и TitleBarIcon.png.

Запуск из корня репозитория: pip install pillow && python3 tools/make_icon.py
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

OUT = Path(__file__).resolve().parent.parent / "src" / "GameLauncher.App" / "Assets"
S = 1024

def lerp(a, b, t): return tuple(round(a[i] + (b[i]-a[i])*t) for i in range(3))

def render(pad_margin=0.0, simple=False):
    # Фон: диагональный градиент синий → фиолетовый.
    top, bottom = (56, 132, 255), (124, 58, 237)
    grad = Image.new("RGB", (S, S))
    px = grad.load()
    for y in range(S):
        for x in range(S):
            px[x, y] = lerp(top, bottom, (x + y) / (2 * S - 2))
    m = round(S * pad_margin)
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([m, m, S - 1 - m, S - 1 - m], radius=round((S - 2*m) * 0.22), fill=255)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    img.paste(grad, (0, 0), mask)

    # Геймпад: корпус + две рукояти, белый.
    pad = Image.new("L", (S, S), 0)
    d = ImageDraw.Draw(pad)
    cx, cy = S // 2, S // 2 + 10
    w, h = 0.70 * S, 0.34 * S
    d.rounded_rectangle([cx - w/2, cy - h/2, cx + w/2, cy + h/2], radius=h * 0.48, fill=255)
    gr = 0.135 * S  # рукояти
    for sx in (-1, 1):
        gx = cx + sx * w * 0.30
        gy = cy + h * 0.30
        d.ellipse([gx - gr, gy - gr, gx + gr, gy + gr], fill=255)
        # мост от корпуса к рукояти
        d.polygon([(gx - sx * gr * 1.6, cy), (gx + sx * gr * 0.95, cy - h * 0.1), (gx + sx * gr * 0.9, gy), (gx - sx * gr * 0.9, gy)], fill=255)

    # Вырезы: крестовина слева, две кнопки справа.
    cut = ImageDraw.Draw(pad)
    dx, dy = cx - w * 0.25, cy - h * 0.04
    arm, thick = 0.075 * S, 0.034 * S if not simple else 0.045 * S
    cut.rounded_rectangle([dx - arm, dy - thick, dx + arm, dy + thick], radius=thick * 0.4, fill=0)
    cut.rounded_rectangle([dx - thick, dy - arm, dx + thick, dy + arm], radius=thick * 0.4, fill=0)
    br = 0.038 * S if not simple else 0.05 * S
    bx, by = cx + w * 0.25, cy - h * 0.04
    if simple:
        cut.ellipse([bx - br, by - br, bx + br, by + br], fill=0)
    else:
        for ox, oy in ((-0.06, 0.035), (0.06, -0.035)):
            x, y = bx + ox * S, by + oy * S
            cut.ellipse([x - br, y - br, x + br, y + br], fill=0)

    # Лёгкая тень под геймпадом.
    shadow = pad.filter(ImageFilter.GaussianBlur(S * 0.02)).point(lambda v: v * 0.28)
    sh = Image.new("RGBA", (S, S), (20, 10, 60, 0)); sh.putalpha(shadow)
    sh = sh.transform((S, S), Image.AFFINE, (1, 0, 0, 0, 1, -S * 0.018))
    img = Image.alpha_composite(img, Image.composite(sh, Image.new("RGBA", (S, S)), mask))
    white = Image.new("RGBA", (S, S), (255, 255, 255, 255)); white.putalpha(pad)
    return Image.alpha_composite(img, white)

big = render()
small = render(simple=True)  # для 16–32 px: крупнее вырезы, иначе каша
sizes = [256, 64, 48, 40, 32, 24, 20, 16]
frames = [(big if s >= 40 else small).resize((s, s), Image.LANCZOS) for s in sizes]
frames[0].save(OUT / "GameLauncher.ico", sizes=[(s, s) for s in sizes], append_images=frames[1:])
# Строка заголовка: 16 px при масштабе до 400 %.
big.resize((64, 64), Image.LANCZOS).save(OUT / "TitleBarIcon.png")
