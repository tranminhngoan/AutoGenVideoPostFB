import os
import argparse
from PIL import Image, ImageDraw, ImageFont

# ─────────────────────────────────────────────────────────────────────────────
# Canvas and "safe zone" for Facebook Reels / phones.
#
# Tall phones show a 9:16 video filled to the screen height, which crops ~90 px off
# each side of a 1080-px frame. Facebook also draws its action column (like / comment /
# share) on the right and the page name + caption across the bottom. Everything that
# must stay readable is kept inside the box below.
# ─────────────────────────────────────────────────────────────────────────────
WIDTH, HEIGHT = 1080, 1920
SAFE_LEFT = 120          # cropped edge on tall phones + breathing room
SAFE_RIGHT = 850         # clear of Facebook's right-hand action buttons
CARD_TOP = 1050          # green news card starts here (image area is above)
TEXT_BOTTOM = 1530       # Facebook's page name / caption overlay starts below this

GREEN = (16, 149, 90, 255)


def _font(paths, size):
    for p in paths:
        if os.path.exists(p):
            return ImageFont.truetype(p, size)
    return ImageFont.load_default()


BOLD_FONTS = [
    r"C:\Windows\Fonts\segoeuib.ttf",
    r"C:\Windows\Fonts\arialbd.ttf",
    "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
    "/usr/share/fonts/TTF/DejaVuSans-Bold.ttf",
]
REGULAR_FONTS = [
    r"C:\Windows\Fonts\segoeui.ttf",
    r"C:\Windows\Fonts\arial.ttf",
    "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
    "/usr/share/fonts/TTF/DejaVuSans.ttf",
]


def _wrap(draw, text, font, max_width):
    """Greedy word wrap using real glyph widths."""
    lines, current = [], []
    for word in text.split():
        candidate = " ".join(current + [word])
        if draw.textlength(candidate, font=font) <= max_width or not current:
            current.append(word)
        else:
            lines.append(" ".join(current))
            current = [word]
    if current:
        lines.append(" ".join(current))
    return lines


def _fit_text_block(draw, title, summary, box_w, box_h):
    """
    Pick the largest font size at which the headline (bold, upper case) plus the
    summary (regular) fill the green card without overflowing it. Short stories get
    big text; long ones shrink down to a readable minimum and are then trimmed.
    """
    title_text = title.strip().upper()
    summary_text = (summary or "").strip()

    for size in range(76, 33, -2):
        t_font = _font(BOLD_FONTS, size)
        s_size = max(26, int(size * 0.62))
        s_font = _font(REGULAR_FONTS, s_size)
        t_lines = _wrap(draw, title_text, t_font, box_w)
        s_lines = _wrap(draw, summary_text, s_font, box_w) if summary_text else []
        t_lh, s_lh = int(size * 1.2), int(s_size * 1.38)
        gap = int(size * 0.45) if s_lines else 0
        total = len(t_lines) * t_lh + gap + len(s_lines) * s_lh
        if total <= box_h:
            return t_font, t_lines, t_lh, s_font, s_lines, s_lh, gap

    # Even the smallest size overflows: keep the headline, trim the summary with an ellipsis.
    size = 34
    t_font, s_size = _font(BOLD_FONTS, size), 26
    s_font = _font(REGULAR_FONTS, s_size)
    t_lines = _wrap(draw, title_text, t_font, box_w)
    t_lh, s_lh, gap = int(size * 1.2), int(s_size * 1.38), int(size * 0.45)
    room = box_h - len(t_lines) * t_lh - gap
    s_lines = _wrap(draw, summary_text, s_font, box_w) if summary_text else []
    keep = max(0, room // s_lh)
    if len(s_lines) > keep:
        s_lines = s_lines[:keep]
        if s_lines:
            s_lines[-1] = s_lines[-1].rstrip(" ,.;:") + "…"
    return t_font, t_lines, t_lh, s_font, s_lines, s_lh, gap if s_lines else 0


def generate_news_overlay(output_path, title, summary="", publisher="TIN TỨC 24H", date_str="", logo_text="Trạm Tin 24/7"):
    img = Image.new("RGBA", (WIDTH, HEIGHT), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    font_logo = _font(BOLD_FONTS, 28)
    font_meta = _font(BOLD_FONTS, 22)

    # 1. (No source credit on the video — owner's decision. `publisher` is still accepted for compatibility.)

    # 2. Green news card (full width background)
    card_h = HEIGHT - CARD_TOP
    card_img = Image.new("RGBA", (WIDTH, card_h), (0, 0, 0, 0))
    c_draw = ImageDraw.Draw(card_img)
    top_color, bot_color = (13, 85, 52, 255), (6, 38, 24, 255)
    for y in range(card_h):
        ratio = y / card_h
        c_draw.line([(0, y), (WIDTH, y)], fill=tuple(
            int(top_color[i] + (bot_color[i] - top_color[i]) * ratio) for i in range(3)) + (255,))
    for radius in [280, 480, 680]:
        c_draw.ellipse(
            [(WIDTH // 2 - radius, card_h // 2 - radius + 50), (WIDTH // 2 + radius, card_h // 2 + radius + 50)],
            outline=(20, 115, 72, 45), width=2)
    img.paste(card_img, (0, CARD_TOP))
    draw.line([(0, CARD_TOP), (WIDTH, CARD_TOP)], fill=(34, 197, 94, 200), width=3)

    # 3. Brand pill on the card edge
    brand = (logo_text or "Trạm Tin 24/7").strip()
    logo_h, icon_d = 64, 44
    logo_y = CARD_TOP - 36
    t_bbox = draw.textbbox((0, 0), brand, font=font_logo)
    logo_w = 10 + icon_d + 14 + (t_bbox[2] - t_bbox[0]) + 26
    x0 = SAFE_LEFT
    draw.rounded_rectangle([x0 - 2, logo_y - 2, x0 + logo_w + 2, logo_y + logo_h + 4], radius=32, fill=(0, 0, 0, 90))
    draw.rounded_rectangle([x0, logo_y, x0 + logo_w, logo_y + logo_h], radius=32, fill=GREEN, outline=(255, 255, 255, 220), width=2)
    cx0, cy0 = x0 + 10, logo_y + 10
    draw.ellipse([cx0, cy0, cx0 + icon_d, cy0 + icon_d], fill=(255, 255, 255, 255))
    draw.polygon([(cx0 + 17, cy0 + 12), (cx0 + 17, cy0 + 32), (cx0 + 33, cy0 + 22)], fill=GREEN)
    text_h = t_bbox[3] - t_bbox[1]
    draw.text((cx0 + icon_d + 14, logo_y + (logo_h - text_h) // 2 - t_bbox[1]), brand, font=font_logo, fill=(255, 255, 255, 255))

    # 4. Date badge
    if not date_str:
        import datetime
        date_str = datetime.date.today().strftime("%d/%m/%Y")
    date_text = f"NGÀY ĐĂNG: {date_str}"
    d_bbox = draw.textbbox((0, 0), date_text, font=font_meta)
    dw, dh = d_bbox[2] - d_bbox[0] + 20, d_bbox[3] - d_bbox[1] + 12
    date_y = CARD_TOP + 52
    draw.rounded_rectangle([SAFE_LEFT, date_y, SAFE_LEFT + dw, date_y + dh], radius=6, fill=(255, 255, 255, 30))
    draw.text((SAFE_LEFT + 10, date_y + 6 - d_bbox[1]), date_text, font=font_meta, fill=(187, 247, 208, 255))

    # 5. Headline + summary, auto-sized to fill the card inside the safe zone
    box_top = date_y + dh + 30
    box_w = SAFE_RIGHT - SAFE_LEFT
    box_h = TEXT_BOTTOM - box_top
    t_font, t_lines, t_lh, s_font, s_lines, s_lh, gap = _fit_text_block(draw, title, summary, box_w, box_h)

    y = box_top
    for line in t_lines:
        draw.text((SAFE_LEFT + 2, y + 2), line, font=t_font, fill=(0, 0, 0, 200))
        draw.text((SAFE_LEFT, y), line, font=t_font, fill=(255, 255, 255, 255))
        y += t_lh
    y += gap
    for line in s_lines:
        draw.text((SAFE_LEFT + 1, y + 1), line, font=s_font, fill=(0, 0, 0, 160))
        draw.text((SAFE_LEFT, y), line, font=s_font, fill=(214, 245, 226, 255))
        y += s_lh

    # (No fake progress bar / timer: the overlay is static — players show real progress.)
    os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)
    img.save(output_path, "PNG")
    print(f"Overlay created: {output_path}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True)
    parser.add_argument("--title", required=True)
    parser.add_argument("--summary", default="")
    parser.add_argument("--publisher", default="TIN TỨC 24H")
    parser.add_argument("--date", default="")
    parser.add_argument("--logo", default="Trạm Tin 24/7")
    args = parser.parse_args()

    generate_news_overlay(
        output_path=args.output,
        title=args.title,
        summary=args.summary,
        publisher=args.publisher,
        date_str=args.date,
        logo_text=args.logo,
    )
