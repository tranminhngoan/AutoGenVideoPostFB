"""
News chart for a video scene, drawn ONLY from numbers found in the article (checked by the backend).

1080x1050 (the picture area of the 9:16 video). Horizontal bars, one series in the topic's accent colour,
value at the end of each bar, title on top, "Nguồn: …" at the bottom. Long labels wrap; up to 8 rows.

Usage: python generate_chart.py spec.json
spec = {"output", "title", "unit", "source", "theme", "points": [{"label", "value"}]}
"""
import json
import os
import sys

from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from generate_news_overlay import BOLD_FONTS, REGULAR_FONTS, _font, _palette, _wrap  # noqa: E402

W, H = 1080, 1050
M = 90  # side margin (inside the phone safe zone)


def _fmt(v):
    if abs(v - round(v)) < 1e-9:
        s = f"{int(round(v)):,}".replace(",", ".")
    else:
        s = f"{v:,.2f}".rstrip("0").rstrip(".").replace(",", "X").replace(".", ",").replace("X", ".")
    return s


def render(spec):
    pal = _palette(spec.get("theme"))
    img = Image.new("RGB", (W, H), pal["bottom"][:3])
    d = ImageDraw.Draw(img, "RGBA")
    for y in range(H):  # gentle vertical gradient
        r = y / (H - 1)
        d.line([(0, y), (W, y)], fill=tuple(int(pal["top"][i] + (pal["bottom"][i] - pal["top"][i]) * r) for i in range(3)) + (255,))

    title_font = _font(BOLD_FONTS, 50)
    y = 70
    for line in _wrap(d, spec.get("title", "").strip(), title_font, W - 2 * M)[:2]:
        d.text((M, y), line, font=title_font, fill=(255, 255, 255, 255))
        y += 62
    unit = (spec.get("unit") or "").strip()
    if unit:
        d.text((M, y + 4), f"Đơn vị: {unit}", font=_font(REGULAR_FONTS, 28), fill=pal["date_text"])
        y += 44
    y += 26

    points = spec["points"][:8]
    vmax = max(abs(p["value"]) for p in points) or 1
    footer = 150  # the video's brand pill sits at y≈1014: nothing important below ~950
    values = [p["value"] for p in points]
    # Prices / rates that move a little: bars from zero would all look the same, so show the trend instead
    if min(values) > 0 and (max(values) - min(values)) / max(values) < 0.15:
        _line(d, pal, points, y, H - footer)
        _footer(d, pal, spec)
        img.save(spec["output"], "PNG")
        return
    row_h = (H - footer - y) / len(points)
    label_font = _font(REGULAR_FONTS, 30 if len(points) <= 5 else 26)
    value_font = _font(BOLD_FONTS, 32 if len(points) <= 5 else 28)
    bar_h = max(14, min(44, row_h * 0.38))
    track_w = W - 2 * M - 150  # room for the value text

    for i, p in enumerate(points):
        top = y + i * row_h
        lines = _wrap(d, str(p["label"]), label_font, W - 2 * M)[:2]
        ly = top
        for line in lines:
            d.text((M, ly), line, font=label_font, fill=pal["summary_text"])
            ly += label_font.size + 4
        by = ly + 6
        bw = max(6, track_w * abs(p["value"]) / vmax)
        d.rounded_rectangle([M, by, M + track_w, by + bar_h], radius=bar_h / 2, fill=(255, 255, 255, 26))  # track
        d.rounded_rectangle([M, by, M + bw, by + bar_h], radius=bar_h / 2, fill=pal["accent"])
        d.text((M + bw + 16, by + bar_h / 2), _fmt(p["value"]), font=value_font, fill=(255, 255, 255, 255), anchor="lm")

    _footer(d, pal, spec)
    img.save(spec["output"], "PNG")


def _footer(d, pal, spec):
    source = (spec.get("source") or "").strip()
    d.text((M, H - 125), f"Nguồn số liệu: {source}" if source else "Số liệu trong bài gốc", font=_font(REGULAR_FONTS, 26), fill=pal["date_text"])


def _line(d, pal, points, top, bottom):
    """Trend of close values: the axis spans the data (labelled), every point carries its value."""
    values = [p["value"] for p in points]
    lo, hi = min(values), max(values)
    pad = (hi - lo) * 0.25 or abs(hi) * 0.02 or 1
    lo, hi = lo - pad, hi + pad
    label_font = _font(REGULAR_FONTS, 26)
    value_font = _font(BOLD_FONTS, 30)
    plot_top, plot_bottom = top + 60, bottom - 110
    left, right = M + 20, W - M - 20
    n = len(points)
    xs = [left + (right - left) * (i / (n - 1) if n > 1 else 0.5) for i in range(n)]
    ys = [plot_bottom - (plot_bottom - plot_top) * (v - lo) / (hi - lo) for v in values]
    # recessive guides: min and max of the axis
    for gy, gv in ((plot_top, hi), (plot_bottom, lo)):
        d.line([(M, gy), (W - M, gy)], fill=(255, 255, 255, 40), width=2)
    d.line(list(zip(xs, ys)), fill=pal["accent"], width=7, joint="curve")
    for x, y_, p in zip(xs, ys, points):
        d.ellipse([x - 11, y_ - 11, x + 11, y_ + 11], fill=pal["accent"], outline=pal["bottom"][:3] + (255,), width=4)
        d.text((x, y_ - 26), _fmt(p["value"]), font=value_font, fill=(255, 255, 255, 255), anchor="ms")
        for k, line in enumerate(_wrap(d, str(p["label"]), label_font, (right - left) / max(1, n - 1) + 40)[:2]):
            d.text((x, plot_bottom + 40 + k * 32), line, font=label_font, fill=pal["summary_text"], anchor="ms")


if __name__ == "__main__":
    with open(sys.argv[1], encoding="utf-8") as f:
        render(json.load(f))
    print("ok")
