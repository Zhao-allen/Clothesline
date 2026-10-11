"""Generate Clothesline app icon (.ico) with multiple sizes.

Design: a curved clothesline with a clothespin and a photo card,
matching the tray icon drawn in TrayIcon.cs.
Run: python make_icon.py
"""
from PIL import Image, ImageDraw

SIZES = [16, 24, 32, 48, 64, 128, 256]


def draw_icon(size: int) -> Image.Image:
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    s = size

    # Scale factors relative to the 64px reference.
    def v(x):
        return int(round(x * s / 64.0))

    rope = (140, 140, 140, 255)
    shadow = (0, 0, 0, 56)
    pin = (178, 178, 178, 255)
    card_fill = (180, 180, 180, 255)
    card_border = (255, 255, 255, 220)
    photo_inner = (255, 255, 255, 200)
    mountain = (255, 255, 255, 255)

    # Rope shadow (offset down)
    d.arc([v(-16), v(12), v(80), v(40)], 0, 180, fill=shadow, width=max(1, v(2)))
    # Rope core
    d.arc([v(-16), v(10), v(80), v(38)], 0, 180, fill=rope, width=max(1, v(1.5)))
    # Rope highlight
    d.arc([v(-16), v(9), v(80), v(37)], 0, 180, fill=(255, 255, 255, 115), width=1)

    # Clothespin (rounded rect)
    pin_x0, pin_y0 = v(29), v(14)
    pin_x1, pin_y1 = v(35), v(26)
    d.rounded_rectangle([pin_x0, pin_y0, pin_x1, pin_y1], radius=v(2), fill=pin)

    # Photo card (rounded rect)
    card_x0, card_y0 = v(20), v(26)
    card_x1, card_y1 = v(44), v(54)
    d.rounded_rectangle([card_x0, card_y0, card_x1, card_y1], radius=v(3),
                        fill=card_fill, outline=card_border, width=1)
    # Photo inner
    d.rounded_rectangle([v(24), v(30), v(40), v(44)], radius=v(1), fill=photo_inner)
    # Mountain in photo
    tri = [(v(26), v(40)), (v(31), v(34)), (v(36), v(40))]
    d.polygon(tri, fill=mountain)

    return img


def main():
    # Use the largest icon as the base; PIL downscales to the requested sizes.
    base = draw_icon(256)
    out = "Clothesline.ico"
    base.save(out, format="ICO", sizes=[(sz, sz) for sz in SIZES])
    print(f"Wrote {out} with sizes {SIZES}")


if __name__ == "__main__":
    main()
