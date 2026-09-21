"""
Contrast audit for the Beacon palette.

Reads the colours straight out of Theme.cs and checks every foreground/background pairing the UI
actually uses. Exists because "dark text on a dark background" is not a bug you can reliably catch by
looking -- it depends on which style stack happens to be pushed at the call site, and the failures
show up in whichever combination nobody screenshotted.

Run:  python tools/contrast-audit.py
Exit code is non-zero when any pairing fails, so it can gate a build if that is ever wanted.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

THEME = Path(__file__).resolve().parent.parent / "src" / "Beacon.Plugin" / "UI" / "Theme.cs"

# WCAG AA: 4.5:1 for body text, 3:1 for large/bold text and meaningful non-text marks.
BODY = 4.5
LARGE = 3.0


def load_palette() -> dict[str, tuple[int, int, int]]:
    src = THEME.read_text(encoding="utf-8")
    pattern = re.compile(
        r"public static readonly Vector4 (\w+)\s*=\s*Rgb\(0x([0-9A-Fa-f]{2}),\s*0x([0-9A-Fa-f]{2}),\s*0x([0-9A-Fa-f]{2})"
    )
    return {m.group(1): tuple(int(m.group(i), 16) for i in (2, 3, 4)) for m in pattern.finditer(src)}


def luminance(rgb: tuple[int, int, int]) -> float:
    def channel(v: int) -> float:
        c = v / 255
        return c / 12.92 if c <= 0.03928 else ((c + 0.055) / 1.055) ** 2.4

    r, g, b = (channel(v) for v in rgb)
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def contrast(fg: tuple[int, int, int], bg: tuple[int, int, int]) -> float:
    a, b = luminance(fg), luminance(bg)
    hi, lo = max(a, b), min(a, b)
    return (hi + 0.05) / (lo + 0.05)


# Every pairing the UI actually draws, grouped by the surface it sits on.
# (foreground, background, threshold, where it appears)
PAIRINGS: list[tuple[str, str, float, str]] = [
    # --- Dark chrome: the window frame, list and status bar ---
    # PanelRaised is the lightest of the dark surfaces (a selected row), so anything that can appear
    # on a selected row is checked against it as the worst case, not just against the darkest panel.
    ("Cream", "Leather", BODY, "window text on the frame"),
    ("CreamDim", "Leather", BODY, "default text"),
    ("Muted", "Leather", BODY, "row subtitles"),
    ("MutedDeep", "Leather", BODY, "faint text"),
    ("Cream", "Panel", BODY, "list row name, lit"),
    ("CreamDim", "Panel", BODY, "list row name, dark"),
    ("Muted", "PanelRaised", BODY, "list row zone and world, selected"),
    ("MutedDeep", "PanelRaised", BODY, "list row 'kept by', selected"),
    ("BrassText", "PanelRaised", BODY, "list row 'lit by', selected"),
    ("BrassBright", "PanelRaised", BODY, "list row note, selected"),
    ("BrassBright", "LeatherLight", BODY, "section headings"),
    ("Verdigris", "Leather", BODY, "'Connected' in the status bar"),
    ("WaxText", "Leather", BODY, "errors in the status bar"),
    ("Ember", "Panel", LARGE, "the flame glyph"),
    ("Gold", "Panel", LARGE, "selection outline"),
    # --- Parchment: the beacon page ---
    ("Ink", "Parchment", BODY, "beacon name and body"),
    ("InkSoft", "Parchment", BODY, "description"),
    ("InkFaint", "Parchment", BODY, "field labels"),
    ("Wax", "Parchment", BODY, "flame status and destructive actions"),
    ("VerdigrisInk", "Parchment", BODY, "'Copied' confirmation"),
    ("Ink", "ParchmentShade", BODY, "page button label"),
    ("InkSoft", "ParchmentShade", BODY, "journey box detail"),
    ("InkFaint", "ParchmentShade", BODY, "journey box label"),
    ("Ink", "ParchmentRule", BODY, "page button label, hovered"),
    # --- Accent buttons: dark label engraved into light brass ---
    ("Leather", "BrassBright", BODY, "accent button, enabled"),
    ("Leather", "Gold", BODY, "accent button, hovered"),
    ("Leather", "GoldBright", BODY, "accent button, pressed"),
    ("Muted", "Panel", BODY, "accent button, disabled"),
    # --- Ordinary and destructive buttons in the chrome ---
    ("CreamDim", "PanelRaised", BODY, "ordinary button in the chrome"),
    ("Cream", "Wax", BODY, "destructive button"),
    # --- The Chronicle ---
    ("Verdigris", "Panel", BODY, "'open to walk-ups' in a card row"),
    ("Verdigris", "PanelRaised", BODY, "'open to walk-ups', selected row"),
    ("MutedDeep", "Panel", BODY, "'last seen' in a card row"),
    ("VerdigrisInk", "ParchmentShade", BODY, "'open to walk-ups' on the card"),
    ("InkFaint", "ParchmentShade", BODY, "'last seen' on the card"),
    ("BrassText", "Panel", BODY, "portrait placeholder initial"),
    # Ornament.Tag draws an outlined chip, not a filled one: the border is Brass but the label sits
    # on the page behind it. Checking the label against Brass would be measuring a pairing that is
    # never actually drawn.
    ("Ink", "Parchment", BODY, "personality tag label on the card"),
    ("Brass", "Parchment", LARGE, "personality tag border on the card"),
    # --- The wax seal ---
    ("WaxLight", "Wax", LARGE, "the flame device pressed into the seal"),
    ("Ink", "Parchment", BODY, "share code beside the seal"),
]


def main() -> int:
    palette = load_palette()
    missing = {name for pair in PAIRINGS for name in pair[:2] if name not in palette}
    if missing:
        print(f"Colours referenced but not found in Theme.cs: {', '.join(sorted(missing))}")
        return 2

    failures = []
    print(f"{'foreground':<14} {'background':<16} {'ratio':>7}  {'need':>5}  result  where")
    print("-" * 96)

    for fg, bg, threshold, where in PAIRINGS:
        ratio = contrast(palette[fg], palette[bg])
        ok = ratio >= threshold
        if not ok:
            failures.append((fg, bg, ratio, threshold, where))
        print(f"{fg:<14} {bg:<16} {ratio:>6.2f}:1  {threshold:>4.1f}   {'ok  ' if ok else 'FAIL'}   {where}")

    print()
    if failures:
        print(f"{len(failures)} failing pairing(s):")
        for fg, bg, ratio, threshold, where in failures:
            print(f"  {fg} on {bg} is {ratio:.2f}:1, needs {threshold:.1f}:1 - {where}")
        return 1

    print("All pairings pass.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
