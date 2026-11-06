"""The dashboard's charts, drawn as SVG on the server (the site's pages run no chart library, and the
CSP allows none from outside). One axis per chart; bars rounded at their data end only; a 2px surface
gap between stacked segments; a quiet grid. Hovering a day shows its numbers (an SVG <title>), and each
chart has a table view in the page. Colors passed the dataviz validator on the dark card surface."""

from __future__ import annotations

import math
from html import escape

from markupsafe import Markup

BLUE = "#3987e5"   # series 1
CORAL = "#e66767"  # series 2
GRID = "rgba(255,255,255,.08)"
INK = "rgba(255,255,255,.56)"
W, H = 760, 230
LEFT, RIGHT, TOP, BOTTOM = 40, 8, 12, 24


def _nice(top: float) -> tuple[float, float]:
    """A round axis maximum and its step (4 gridlines)."""
    if top <= 0:
        return 4, 1
    raw = top / 4
    mag = 10 ** math.floor(math.log10(raw))
    step = next(m * mag for m in (1, 2, 2.5, 5, 10) if m * mag >= raw)
    step = max(1, step)
    return step * 4, step


def _label(day: str) -> str:
    return f"{day[8:10]}/{day[5:7]}"


def _frame(maximum: float, step: float, labels: list[str]) -> list[str]:
    out = []
    plot_h = H - TOP - BOTTOM
    for i in range(5):
        y = TOP + plot_h - plot_h * i / 4
        value = step * i
        out.append(f'<line x1="{LEFT}" x2="{W - RIGHT}" y1="{y:.1f}" y2="{y:.1f}" stroke="{GRID}" stroke-width="1"/>')
        out.append(f'<text x="{LEFT - 6}" y="{y + 4:.1f}" text-anchor="end" font-size="11" fill="{INK}">{int(value):,}</text>'.replace(",", "."))
    every = max(1, math.ceil(len(labels) / 8))
    slot = (W - LEFT - RIGHT) / len(labels)
    last = len(labels) - 1
    for i, day in enumerate(labels):
        # Every few days, and always the last one; a regular label too close to the last one gives way.
        if (i % every == 0 and last - i >= every) or i == last:
            x = LEFT + slot * (i + .5)
            out.append(f'<text x="{x:.1f}" y="{H - 6}" text-anchor="middle" font-size="11" fill="{INK}">{_label(day)}</text>')
    return out


def _bar(x: float, y: float, w: float, h: float, color: str, rounded: bool) -> str:
    """A bar anchored to its baseline, rounded at the data end only."""
    if h <= 0:
        return ""
    r = min(4, w / 2, h) if rounded else 0
    bottom = y + h
    if not r:
        return f'<rect x="{x:.1f}" y="{y:.1f}" width="{w:.1f}" height="{h:.1f}" fill="{color}"/>'
    return (f'<path d="M{x:.1f},{bottom:.1f} V{y + r:.1f} Q{x:.1f},{y:.1f} {x + r:.1f},{y:.1f} H{x + w - r:.1f} '
            f'Q{x + w:.1f},{y:.1f} {x + w:.1f},{y + r:.1f} V{bottom:.1f} Z" fill="{color}"/>')


def _svg(parts: list[str], label: str) -> Markup:
    return Markup(f'<svg class="chart" viewBox="0 0 {W} {H}" role="img" aria-label="{escape(label)}" '
                  f'font-family="Segoe UI Variable Text, Segoe UI, system-ui, sans-serif">{"".join(parts)}</svg>')


def bars_and_line(labels: list[str], bars: list[int], line: list[int], bar_name: str, line_name: str) -> Markup:
    """Bars for one count, a line for a smaller count of the same unit (views and the people behind them)."""
    maximum, step = _nice(max(bars + line + [0]))
    plot_h = H - TOP - BOTTOM
    slot = (W - LEFT - RIGHT) / len(labels)
    width = max(2, slot * .62)
    parts = _frame(maximum, step, labels)
    if not any(bars) and not any(line):
        parts.append(f'<text x="{W / 2}" y="{H / 2}" text-anchor="middle" font-size="13" fill="{INK}">Chưa có dữ liệu</text>')
    y_of = lambda v: TOP + plot_h - plot_h * v / maximum
    for i, v in enumerate(bars):
        x = LEFT + slot * i + (slot - width) / 2
        parts.append(_bar(x, y_of(v), width, plot_h * v / maximum, BLUE, True))
    points = " ".join(f"{LEFT + slot * (i + .5):.1f},{y_of(v):.1f}" for i, v in enumerate(line))
    parts.append(f'<polyline points="{points}" fill="none" stroke="{CORAL}" stroke-width="2" stroke-linejoin="round"/>')
    for i, day in enumerate(labels):
        x = LEFT + slot * i
        parts.append(f'<rect x="{x:.1f}" y="{TOP}" width="{slot:.1f}" height="{plot_h}" fill="transparent">'
                     f'<title>{_label(day)}: {bars[i]} {escape(bar_name)}, {line[i]} {escape(line_name)}</title></rect>')
    return _svg(parts, f"{bar_name} và {line_name} theo ngày")


def stacked(labels: list[str], series: list[tuple[str, list[int], str]], name: str) -> Markup:
    """Stacked daily bars, a 2px gap between segments, the top segment rounded."""
    totals = [sum(s[1][i] for s in series) for i in range(len(labels))]
    maximum, step = _nice(max(totals + [0]))
    plot_h = H - TOP - BOTTOM
    slot = (W - LEFT - RIGHT) / len(labels)
    width = max(2, slot * .62)
    parts = _frame(maximum, step, labels)
    if not any(totals):
        parts.append(f'<text x="{W / 2}" y="{H / 2}" text-anchor="middle" font-size="13" fill="{INK}">Chưa có dữ liệu</text>')
    for i, day in enumerate(labels):
        x = LEFT + slot * i + (slot - width) / 2
        base = TOP + plot_h
        shown = [(label, values[i], color) for label, values, color in series if values[i] > 0]
        for k, (label, value, color) in enumerate(shown):
            h = plot_h * value / maximum
            gap = 2 if k else 0
            parts.append(_bar(x, base - h + gap, width, h - gap, color, k == len(shown) - 1))
            base -= h
        detail = ", ".join(f"{values[i]} {label}" for label, values, _ in series)
        parts.append(f'<rect x="{LEFT + slot * i:.1f}" y="{TOP}" width="{slot:.1f}" height="{plot_h}" fill="transparent">'
                     f'<title>{_label(day)}: {escape(detail)}</title></rect>')
    return _svg(parts, name)


def label(day: str) -> str:
    return _label(day)
