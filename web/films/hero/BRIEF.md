---
workflow: general-video
flow: automation
storyboard: yes
---

# Brief: MusicBar website hero loop (1.8, the online gallery)

One short looping film for the home page hero of musicbar.truclv.com. MusicBar is a Windows 11 app that
draws a progress bar, a visualizer or an RGB LED strip under the taskbar icons, sings karaoke lyrics on
the taskbar, and has a little face (two round eyes, a smile, sometimes a mic) by the player. Version 1.8
adds an online gallery: click a look on the website or in the app and the taskbar puts it on.

## Format
- 1600 x 900, 30 fps, muted (no audio track), seamless loop.
- 118 BPM (beat 0.5085 s), 24 beats, 366 frames = 12.2 s. Changes land on beats.
- Frame 0 is the poster and has to work alone.

## Look (fixed: `web/DESIGN.md`)
- Windows 11 dark desktop `#1C1C20`. Panels `#141418` at 96 %, 14 % white edge, radius 14, coral wash
  on top when the panel announces something, Windows' own flyout shadow.
- One accent, coral `#FF6B6B`, meaning "on / in use".
- Segoe UI Variable (Display for the panel title, Text for the rest), sentence case.
- No all-caps or mono labels, no arrow glyphs, no em or en dashes, no glow, no purple, no particles.
- The taskbar matches the site's Windows 11 taskbar (`web/server/static/css/site.css`, `_taskbar.html`).
- The earlier paper / ink / orange print style with Anybody and Geist Mono was rejected: none of it here.

## Material (real product only)
- Gallery looks: `web/seed/previews/*.mp4` (760 x 84, 6 s).
- Karaoke: `promo/assets/product/lyrics-*.webm` (MusicBar's own lyrics renderer, alpha).
- Face markup and CSS: copied from `promo/index.html`. Cover and icon: `promo/assets/product/`.

## Deliverables
- `web/server/static/media/hero.mp4` (H.264, yuv420p, +faststart, no audio, <= 4 MB)
- `web/server/static/media/hero.webm` (VP9, no audio, <= 4 MB)
- `web/server/static/media/hero-poster.jpg` (frame 0, <= 200 KB)

## Rules
- Headless only: no Studio, no preview window (the user may be gaming on this machine).
- No git commits; touch only `web/films/hero/` and `web/server/static/media/hero*`.
