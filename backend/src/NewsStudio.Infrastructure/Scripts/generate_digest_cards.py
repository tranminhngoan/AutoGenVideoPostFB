"""
Pictures of the daily "Điểm tin nóng" digest of one Facebook Page.

For every story: its photo with a short headline + 1-2 sentence summary written on it, in the Page's
topic colours and with the Page name, in two shapes:
  post_NN.jpg  1080x1350 (4:5)  -> multi-photo post on the Page's wall
  story_NN.jpg 1080x1920 (9:16) -> frames of the Page Story (a short slideshow video)
NN = 00 is the cover: "ĐIỂM TIN NÓNG", the date and the list of headlines.

Usage: python generate_digest_cards.py spec.json
spec = {"out_dir", "brand", "theme", "topic_label", "date", "title",
        "items": [{"photo", "headline", "summary", "source"}]}
Prints {"post": [paths], "story": [paths]} as JSON.
"""
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from generate_news_overlay import BOLD_FONTS, REGULAR_FONTS, _fit_brand, _font, _palette, _wrap, paste_logo  # noqa: E402

LOGO = {"path": ""}  # page logo (brand kit), set from the spec

POST = {"w": 1080, "h": 1350, "photo_h": 720, "margin": 64, "top_safe": 48, "bottom_safe": 1350 - 40}
# Stories: Facebook draws the progress bar + page name at the top and the reply box at the bottom
STORY = {"w": 1080, "h": 1920, "photo_h": 1010, "margin": 80, "top_safe": 250, "bottom_safe": 1920 - 250}

def _cover_crop(photo, w, h):
    scale = max(w / photo.width, h / photo.height)
    resized = photo.resize((max(w, int(photo.width * scale + 0.5)), max(h, int(photo.height * scale + 0.5))), Image.LANCZOS)
    left, top = (resized.width - w) // 2, (resized.height - h) // 2
    return resized.crop((left, top, left + w, top + h))


def _gradient(w, h, pal):
    card = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(card)
    for y in range(h):
        r = y / max(1, h - 1)
        d.line([(0, y), (w, y)], fill=tuple(int(pal["top"][i] + (pal["bottom"][i] - pal["top"][i]) * r) for i in range(3)) + (255,))
    for radius in [h // 2, int(h * 0.8), int(h * 1.1)]:
        d.ellipse([(w // 2 - radius, h // 2 - radius + 40), (w // 2 + radius, h // 2 + radius + 40)], outline=pal["ring"], width=2)
    return card


def _brand_pill(draw, pal, brand, x0, y, max_w):
    logo_h, icon_d = 64, 44
    font, text = _fit_brand(draw, (brand or "Trạm Tin 24/7").strip(), max_w - (10 + icon_d + 14 + 26))
    bbox = draw.textbbox((0, 0), text, font=font)
    w = 10 + icon_d + 14 + (bbox[2] - bbox[0]) + 26
    draw.rounded_rectangle([x0 - 2, y - 2, x0 + w + 2, y + logo_h + 4], radius=32, fill=(0, 0, 0, 90))
    draw.rounded_rectangle([x0, y, x0 + w, y + logo_h], radius=32, fill=pal["pill"], outline=(255, 255, 255, 220), width=2)
    cx, cy = x0 + 10, y + 10
    draw.ellipse([cx, cy, cx + icon_d, cy + icon_d], fill=(255, 255, 255, 255))
    if not (LOGO["path"] and paste_logo(draw._image, LOGO["path"], cx, cy, icon_d)):
        for i, lw in enumerate((22, 22, 14)):  # newspaper mark
            ly = cy + 13 + i * 8
            draw.rounded_rectangle([cx + 11, ly, cx + 11 + lw, ly + 4], radius=2, fill=pal["pill"])
    draw.text((cx + icon_d + 14, y + (logo_h - (bbox[3] - bbox[1])) // 2 - bbox[1]), text, font=font, fill=(255, 255, 255, 255))
    return logo_h


def _accent_text_color(pal):
    r, g, b = pal["accent"][:3]
    return (24, 20, 10, 255) if 0.299 * r + 0.587 * g + 0.114 * b > 165 else (255, 255, 255, 255)


def _tag(draw, pal, text, x, y, font, filled):
    bbox = draw.textbbox((0, 0), text, font=font)
    ref = draw.textbbox((0, 0), "0123", font=font)  # same baseline for every tag
    w, h = bbox[2] - bbox[0] + 28, ref[3] - ref[1] + 16
    draw.rounded_rectangle([x, y, x + w, y + h], radius=8, fill=pal["accent"] if filled else (255, 255, 255, 34))
    draw.text((x + 14, y + 8 - ref[1]), text, font=font, fill=_accent_text_color(pal) if filled else pal["date_text"])
    return w, h


def _fit_story_text(draw, headline, summary, box_w, box_h, max_size):
    """Largest headline size whose headline (bold, upper case) + summary fit the box; trims the summary if needed."""
    head = headline.strip().upper()
    for size in range(max_size, 31, -2):
        h_font = _font(BOLD_FONTS, size)
        s_size = max(28, int(size * 0.66))
        s_font = _font(REGULAR_FONTS, s_size)
        h_lines = _wrap(draw, head, h_font, box_w)
        s_lines = _wrap(draw, summary.strip(), s_font, box_w) if summary.strip() else []
        h_lh, s_lh = int(size * 1.16), int(s_size * 1.4)
        gap = int(size * 0.5) if s_lines else 0
        if len(h_lines) * h_lh + gap + len(s_lines) * s_lh <= box_h:
            return h_font, h_lines, h_lh, s_font, s_lines, s_lh, gap
    size, s_size = 32, 28
    h_font, s_font = _font(BOLD_FONTS, size), _font(REGULAR_FONTS, s_size)
    h_lh, s_lh, gap = int(size * 1.16), int(s_size * 1.4), int(size * 0.5)
    h_lines = _wrap(draw, head, h_font, box_w)
    s_lines = _wrap(draw, summary.strip(), s_font, box_w)
    keep = max(0, (box_h - len(h_lines) * h_lh - gap) // s_lh)
    if len(s_lines) > keep:
        s_lines = s_lines[:keep]
        if s_lines:
            s_lines[-1] = s_lines[-1].rstrip(" ,.;:") + "…"
    return h_font, h_lines, h_lh, s_font, s_lines, s_lh, gap if s_lines else 0


def _shadow_text(draw, xy, text, font, fill, shadow=200):
    draw.text((xy[0] + 2, xy[1] + 2), text, font=font, fill=(0, 0, 0, shadow))
    draw.text(xy, text, font=font, fill=fill)


def story_card(spec, item, index, total, L, out_path):
    pal = _palette(spec.get("theme"))
    W, H, PH, M = L["w"], L["h"], L["photo_h"], L["margin"]
    img = Image.new("RGB", (W, H), pal["bottom"][:3])
    with Image.open(item["photo"]) as photo:
        img.paste(_cover_crop(photo.convert("RGB"), W, PH), (0, 0))
    card = _gradient(W, H - PH, pal)
    img.paste(card, (0, PH), card)
    draw = ImageDraw.Draw(img, "RGBA")
    draw.line([(0, PH), (W, PH)], fill=pal["line"], width=4)

    # "1/6" counter on the picture (below the Story header on 9:16)
    meta = _font(BOLD_FONTS, 30)
    if total > 1:  # a single card (one news photo post) has no counter
        _tag(draw, pal, f"{index}/{total}", M, L["top_safe"] if L is STORY else 40, meta, True)

    # Page name on the edge between picture and card
    logo_h = _brand_pill(draw, pal, spec.get("brand"), M, PH - 32, W - 2 * M)

    # Headline + summary
    src_font = _font(REGULAR_FONTS, 26)
    src_h = 44 if item.get("source") else 0
    box_top = PH + logo_h - 32 + 34
    box_bottom = min(L["bottom_safe"], H - 40) - src_h
    h_font, h_lines, h_lh, s_font, s_lines, s_lh, gap = _fit_story_text(
        draw, item["headline"], item.get("summary", ""), W - 2 * M, box_bottom - box_top, 68 if L is STORY else 60)
    y = box_top
    if L is STORY:
        # Tall card: centre the text block instead of leaving the lower half empty
        used = len(h_lines) * h_lh + gap + len(s_lines) * s_lh
        y += max(0, (box_bottom - box_top - used) // 2)
    for line in h_lines:
        _shadow_text(draw, (M, y), line, h_font, (255, 255, 255, 255))
        y += h_lh
    y += gap
    for line in s_lines:
        _shadow_text(draw, (M, y), line, s_font, pal["summary_text"], 150)
        y += s_lh
    if item.get("source"):
        draw.text((M, min(y + 14, box_bottom + 10)), f"Nguồn: {item['source']}", font=src_font, fill=pal["date_text"])

    img.save(out_path, "JPEG", quality=88, optimize=True)


def cover_card(spec, L, out_path):
    pal = _palette(spec.get("theme"))
    W, H, M = L["w"], L["h"], L["margin"]
    items = spec["items"]

    # Background: the first story's photo, blurred and tinted with the topic colours
    with Image.open(items[0]["photo"]) as photo:
        bg = _cover_crop(photo.convert("RGB"), W, H).filter(ImageFilter.GaussianBlur(22))
    img = bg.convert("RGBA")
    tint = Image.new("RGBA", (W, H), pal["bottom"][:3] + (215,))
    img = Image.alpha_composite(img, tint)
    img = Image.alpha_composite(img, _gradient_rings(W, H, pal)).convert("RGB")  # RGB so tags blend, not punch holes
    draw = ImageDraw.Draw(img, "RGBA")

    y = L["top_safe"] + (10 if L is STORY else 20)
    y += _brand_pill(draw, pal, spec.get("brand"), M, y, W - 2 * M) + 44

    title_font = _font(BOLD_FONTS, 92 if L is STORY else 80)
    for line in _wrap(draw, (spec.get("title") or "ĐIỂM TIN NÓNG").upper(), title_font, W - 2 * M):
        _shadow_text(draw, (M, y), line, title_font, pal["accent"])
        y += int(title_font.size * 1.1)
    y += 18
    meta = _font(BOLD_FONTS, 30)
    w, h = _tag(draw, pal, spec.get("date", ""), M, y, meta, False)
    if spec.get("topic_label"):
        _tag(draw, pal, spec["topic_label"].upper(), M + w + 14, y, meta, True)
    y += h + 44
    draw.line([(M, y), (W - M, y)], fill=pal["line"], width=3)
    y += 36

    # Numbered list of headlines, sized to fit the remaining space
    bottom = L["bottom_safe"] - 20
    num_d = 52
    text_w = W - 2 * M - num_d - 22
    for size in range(56 if L is STORY else 50, 25, -2):
        font = _font(BOLD_FONTS, size)
        lh, gap = int(size * 1.2), int(size * 0.6)
        wrapped = [_wrap(draw, it["headline"].strip(), font, text_w)[:3] for it in items]
        total_h = sum(max(len(lines) * lh, num_d) for lines in wrapped) + gap * (len(items) - 1)
        if total_h <= bottom - y:
            break
    num_font = _font(BOLD_FONTS, 28)
    for i, lines in enumerate(wrapped, start=1):
        draw.ellipse([M, y + 2, M + num_d, y + 2 + num_d], fill=pal["accent"])
        nb = draw.textbbox((0, 0), str(i), font=num_font)
        draw.text((M + (num_d - (nb[2] - nb[0])) // 2 - nb[0], y + 2 + (num_d - (nb[3] - nb[1])) // 2 - nb[1]),
                  str(i), font=num_font, fill=_accent_text_color(pal))
        ty = y + max(0, (num_d - lh) // 2) if len(lines) == 1 else y
        for line in lines:
            _shadow_text(draw, (M + num_d + 22, ty), line, font, (255, 255, 255, 255), 170)
            ty += lh
        y += max(len(lines) * lh, num_d) + gap

    img.save(out_path, "JPEG", quality=88, optimize=True)


def _gradient_rings(w, h, pal):
    layer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for radius in [int(w * 0.45), int(w * 0.75), int(w * 1.05)]:
        d.ellipse([(w // 2 - radius, h - radius // 2 - radius), (w // 2 + radius, h - radius // 2 + radius)], outline=pal["ring"], width=2)
    return layer


def main(spec_path):
    with open(spec_path, encoding="utf-8") as f:
        spec = json.load(f)
    out_dir = spec["out_dir"]
    LOGO["path"] = spec.get("logo") or ""
    os.makedirs(out_dir, exist_ok=True)
    total = len(spec["items"])
    result = {"post": [], "story": []}
    for kind, L in (("post", POST), ("story", STORY)):
        path = os.path.join(out_dir, f"{kind}_00.jpg")
        cover_card(spec, L, path)
        result[kind].append(path)
        for i, item in enumerate(spec["items"], start=1):
            path = os.path.join(out_dir, f"{kind}_{i:02d}.jpg")
            story_card(spec, item, i, total, L, path)
            result[kind].append(path)
    print(json.dumps(result))


if __name__ == "__main__":
    main(sys.argv[1])
