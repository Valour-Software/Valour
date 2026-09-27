"""Writes the app, website and native icon files from the masters in media/socials.

Run render.mjs first. Needs Pillow.
"""

import json
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
MASTERS = ROOT / "media" / "socials"
CLIENT = ROOT / "Valour" / "Client" / "wwwroot"
WEB = ROOT / "Valour" / "Web" / "wwwroot"
MAUI = ROOT / "Valour" / "Client.Maui"

INK = "#12161e"
FUR = "#f3f5fa"
SHADE = "#cdd4e2"

# Victor is placed by the center of mass of his silhouette, in logo units, so he
# looks centered next to text. See Docs/DesignLanguage.md.
CENTER_OF_MASS = (281, 289)


def master(name):
    return Image.open(MASTERS / name).convert("RGBA")


def save(image, size, path):
    resized = image.resize((size, size), Image.LANCZOS)
    path = Path(path)
    if path.suffix == ".webp":
        resized.save(path, "WEBP", quality=90, method=6)
    else:
        resized.save(path, optimize=True)


def android_foreground(scale=0.85):
    parts = json.loads((Path(__file__).parent / "victor-parts.json").read_text())
    face, far, eye_left, eye_right, nose, body, bow_left, bow_knot, bow_right = parts
    x = 512 - CENTER_OF_MASS[0] * scale
    y = 1024 * 0.485 - CENTER_OF_MASS[1] * scale
    layers = [(far, SHADE), (face, FUR), (body, FUR), (eye_left, INK), (eye_right, INK), (nose, INK),
              (bow_left, INK), (bow_right, INK), (bow_knot, INK)]
    paths = "".join(f'<path d="{d}" fill="{fill}"/>' for d, fill in layers)
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024">'
            f'<g transform="translate({x:.1f},{y:.1f}) scale({scale})">{paths}</g></svg>\n')


def main():
    outline = master("icon-outline.png")
    bleed = master("icon-full-bleed.png").convert("RGB")
    small = master("icon-small.png")

    logo = CLIENT / "media" / "logo"
    for size in (64, 128, 256, 512):
        save(outline, size, logo / f"logo-{size}.png")
        save(outline, size, logo / f"logo-{size}.webp")
        save(outline, size, WEB / "media" / "logo" / f"logo-{size}.png")
    for size in (64, 128, 192, 256, 512):
        save(bleed, size, logo / f"logo-square-{size}.png")
    for size in (64, 128, 256, 512):
        save(bleed, size, logo / f"logo-square-{size}.webp")
    save(bleed, 1024, logo / "logo-square-1k.png")
    save(bleed, 1024, logo / "logo-square-1k.webp")

    favicon = CLIENT / "media" / "favicon"
    save(small, 16, favicon / "favicon-16x16.png")
    save(small, 32, favicon / "favicon-32x32.png")
    save(outline, 192, favicon / "android-chrome-192x192.png")
    save(outline, 512, favicon / "android-chrome-512x512.png")
    save(bleed, 180, favicon / "apple-touch-icon.png")
    for path in (CLIENT / "favicon.ico", WEB / "favicon.ico"):
        small.save(path, sizes=[(16, 16), (32, 32), (48, 48)])
    small.save(MAUI / "Platforms" / "Windows" / "trayicon.ico",
               sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])

    bleed.save(MAUI / "Resources" / "AppIcon" / "appicon.png", optimize=True)
    master("icon-android-background.png").convert("RGB").save(
        MAUI / "Resources" / "AppIcon" / "Android" / "appicon.png", optimize=True)
    (MAUI / "Resources" / "AppIcon" / "Android" / "appiconfg.svg").write_text(android_foreground())
    master("splash-victor.png").save(MAUI / "Resources" / "Splash" / "splash.png", optimize=True)

    master("social-card.png").convert("RGB").save(WEB / "media" / "twitter-card.png", optimize=True)
    for theme in ("dark", "light"):
        master(f"wordmark-{theme}.png").save(logo / "wide" / f"valour-wordmark-{theme}.png", optimize=True)

    save(master("social-avatar.png").convert("RGB"), 400, MASTERS / "social-avatar-400.png")
    print("exported")


if __name__ == "__main__":
    main()
