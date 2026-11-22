"""
Watermark of an AI-studio video: a semi-transparent pill with the Facebook Page's round logo (brand kit, optional)
and the Page name, drawn on a transparent PNG. FFmpeg overlays it and moves it around the frame so a re-uploader
cannot crop or blur one fixed corner.

Usage: python generate_studio_watermark.py --output wm.png --name "Miền Tây Quê Tôi" [--logo-image logo.png] [--height 76]
"""
import argparse
import os

from PIL import Image, ImageDraw, ImageFont, ImageOps

BOLD_FONTS = [
    "C:/Windows/Fonts/arialbd.ttf",
    "C:/Windows/Fonts/segoeuib.ttf",
    "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
    "/usr/share/fonts/TTF/DejaVuSans-Bold.ttf",
]


def _font(size):
    for p in BOLD_FONTS:
        if os.path.exists(p):
            return ImageFont.truetype(p, size)
    raise SystemExit("No bold font with Vietnamese glyphs found (install fonts-dejavu-core).")


def generate(output, name, logo_image="", height=76, max_width=680):
    name = (name or "").strip()
    if not name:
        raise SystemExit("--name is empty")
    pad = height // 6
    icon = height - 2 * pad
    font_size = int(height * 0.44)
    probe = ImageDraw.Draw(Image.new("RGBA", (1, 1)))

    logo = None
    if logo_image and os.path.exists(logo_image):
        try:
            with Image.open(logo_image) as src:
                logo = ImageOps.fit(src.convert("RGBA"), (icon, icon), Image.LANCZOS)
        except Exception:
            logo = None

    text_left = pad + (icon + pad if logo else pad)
    room = max_width - text_left - 2 * pad
    font = _font(font_size)
    while probe.textlength(name, font=font) > room and font_size > 18:
        font_size -= 1
        font = _font(font_size)
    text = name
    while probe.textlength(text, font=font) > room and len(text) > 4:
        text = text[:-2].rstrip() + "…"

    bbox = probe.textbbox((0, 0), text, font=font)
    width = int(text_left + (bbox[2] - bbox[0]) + 2 * pad + pad)
    img = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    draw.rounded_rectangle([0, 0, width - 1, height - 1], radius=height // 2, fill=(0, 0, 0, 120), outline=(255, 255, 255, 170), width=2)

    if logo:
        mask = Image.new("L", (icon, icon), 0)
        ImageDraw.Draw(mask).ellipse([0, 0, icon - 1, icon - 1], fill=255)
        img.paste(logo, (pad, pad), Image.composite(logo.getchannel("A"), mask, mask))

    ty = (height - (bbox[3] - bbox[1])) // 2 - bbox[1]
    draw.text((text_left + 2, ty + 2), text, font=font, fill=(0, 0, 0, 150))  # soft shadow
    draw.text((text_left, ty), text, font=font, fill=(255, 255, 255, 240))

    os.makedirs(os.path.dirname(os.path.abspath(output)), exist_ok=True)
    img.save(output, "PNG")
    print(f"Watermark created: {output} ({width}x{height})")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True)
    parser.add_argument("--name", required=True)
    parser.add_argument("--logo-image", default="")
    parser.add_argument("--height", type=int, default=76)
    args = parser.parse_args()
    generate(args.output, args.name, args.logo_image, args.height)
