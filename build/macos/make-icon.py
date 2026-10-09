"""LocalVault uygulama simgesini macOS ölçülerinde (1024x1024, kenar boşluklu) çizer.

Kullanım:  python build/macos/make-icon.py [çıktı.png]
Pillow gerekir. Çıktı package-mac.sh tarafından .icns'e dönüştürülür.
"""
import sys
from PIL import Image, ImageDraw

SIZE = 1024
SCALE = 4                      # kenar yumuşatma için büyük çizip küçült
S = SIZE * SCALE
TOP, BOTTOM = (59, 130, 246), (29, 78, 216)   # #3B82F6 → #1D4ED8 (uygulamadaki simgeyle aynı)


def lerp(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def main(out):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    # Arka plan: macOS ızgarasındaki gibi 100 px boşluklu, köşeleri yuvarlatılmış kare + dikey degrade
    inset, radius = 100 * SCALE, 185 * SCALE
    box = (inset, inset, S - inset, S - inset)
    gradient = Image.new("RGBA", (S, S))
    gdraw = ImageDraw.Draw(gradient)
    for y in range(box[1], box[3]):
        gdraw.line([(box[0], y), (box[2], y)], fill=lerp(TOP, BOTTOM, (y - box[1]) / (box[3] - box[1])) + (255,))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle(box, radius=radius, fill=255)
    img.paste(gradient, (0, 0), mask)

    draw = ImageDraw.Draw(img)
    white = (255, 255, 255, 255)
    cx = S // 2

    # Kilit halkası
    ring_w, ring_t = 300 * SCALE, 62 * SCALE
    ring_top = 270 * SCALE
    draw.rounded_rectangle((cx - ring_w // 2, ring_top, cx + ring_w // 2, ring_top + 420 * SCALE),
                           radius=ring_w // 2, outline=white, width=ring_t)

    # Gövde
    body = (cx - 255 * SCALE, 470 * SCALE, cx + 255 * SCALE, 800 * SCALE)
    draw.rounded_rectangle(body, radius=56 * SCALE, fill=white)

    # Anahtar deliği
    hole = lerp(TOP, BOTTOM, 0.55) + (255,)
    r = 46 * SCALE
    cy = 610 * SCALE
    draw.ellipse((cx - r, cy - r, cx + r, cy + r), fill=hole)
    draw.rounded_rectangle((cx - 20 * SCALE, cy, cx + 20 * SCALE, cy + 120 * SCALE), radius=20 * SCALE, fill=hole)

    img.resize((SIZE, SIZE), Image.LANCZOS).save(out)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "icon-1024.png")
