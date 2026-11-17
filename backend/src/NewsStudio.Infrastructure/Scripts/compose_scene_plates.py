"""
Scene plates for the 9:16 video: one 1080x1920 picture per scene, made once with Pillow instead of
blurring every video frame in FFmpeg.

  - the scene's photo fitted into the picture area (1080x1050) over a blurred, darkened copy of itself;
  - a small credit line ("Ảnh: VnExpress", "Ảnh: Tác giả / Wikimedia Commons · CC BY-SA 4.0",
    "Ảnh minh hoạ", "Đồ hoạ: số liệu VnExpress") kept inside the phone safe zone;
  - for a photo whose subject is a verified person: a lower-third name tag in the topic's accent colour.

Usage: python compose_scene_plates.py spec.json
spec = {"theme": "...", "plates": [{"image", "output", "credit", "name"}]}
"""
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageOps

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from generate_news_overlay import BOLD_FONTS, REGULAR_FONTS, SAFE_LEFT, SAFE_RIGHT, _font, _palette  # noqa: E402

W, H = 1080, 1920
AREA_H = 1050          # picture area above the news card
CREDIT_Y = 968         # just above the brand pill on the card edge


def _cover(im, w, h):
    return ImageOps.fit(im, (w, h), Image.LANCZOS, centering=(0.5, 0.4))


def plate(item, pal):
    with Image.open(item["image"]) as src:
        photo = ImageOps.exif_transpose(src).convert("RGB")
    base = _cover(photo, W, H).filter(ImageFilter.GaussianBlur(28))
    base = Image.blend(base, Image.new("RGB", (W, H), (0, 0, 0)), 0.35)
    fg = ImageOps.contain(photo, (W, AREA_H), Image.LANCZOS)
    base.paste(fg, ((W - fg.width) // 2, (AREA_H - fg.height) // 2))
    d = ImageDraw.Draw(base, "RGBA")

    name = (item.get("name") or "").strip()
    if name:
        f = _font(BOLD_FONTS, 38)
        tw = d.textlength(name, font=f)
        x0, y0 = SAFE_LEFT, CREDIT_Y - 92
        d.rectangle([x0, y0, x0 + 10, y0 + 58], fill=pal["accent"])
        d.rectangle([x0 + 10, y0, x0 + 10 + tw + 36, y0 + 58], fill=(10, 12, 18, 200))
        d.text((x0 + 28, y0 + 29), name, font=f, fill=(255, 255, 255, 255), anchor="lm")

    credit = (item.get("credit") or "").strip()
    if credit:
        f = _font(REGULAR_FONTS, 22)
        while d.textlength(credit, font=f) > SAFE_RIGHT - SAFE_LEFT - 24 and len(credit) > 12:
            credit = credit[:-2].rstrip() + "…"
        tw = d.textlength(credit, font=f)
        x1 = SAFE_RIGHT
        d.rounded_rectangle([x1 - tw - 20, CREDIT_Y - 17, x1, CREDIT_Y + 17], radius=8, fill=(0, 0, 0, 130))
        d.text((x1 - 10, CREDIT_Y), credit, font=f, fill=(255, 255, 255, 230), anchor="rm")

    base.save(item["output"], "JPEG", quality=90)


def main(path):
    with open(path, encoding="utf-8") as f:
        spec = json.load(f)
    pal = _palette(spec.get("theme"))
    for item in spec["plates"]:
        plate(item, pal)
    print(len(spec["plates"]))


if __name__ == "__main__":
    main(sys.argv[1])
