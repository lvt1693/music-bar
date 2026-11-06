# musicbar.truclv.com: design contract

The site sells an app, so it wears the app. Every token below comes from MusicBar's own XAML
(`Themes/Cards.xaml`, `Views/ControlCenterWindow.xaml`, `Views/SettingsWindow.xaml`). When the site
needs something the app doesn't have, add it here first, then use it.

## Direction

The page is a walk through MusicBar's Settings window, with a live Windows 11 taskbar at the bottom of
the screen that MusicBar is playing on. Whatever the visitor points at (a style, a gallery look) is
tried on that taskbar. The product is the demo; nothing on the page is a mock of something else.

## Color

| Token | Value | From | Role |
| --- | --- | --- | --- |
| `--ground` | `#1C1C20` | Windows 11 dark window (SettingsWindow, `ThemeMode="Dark"`) | page background, solid |
| `--card` | `rgba(255,255,255,.05)` + border `rgba(255,255,255,.07)` | SettingCard `#0DFFFFFF` / `#12FFFFFF` | rows, setting cards |
| `--panel` | `rgba(20,20,24,.96)` + edge `rgba(255,255,255,.14)` | `CardBackground` `#F5141418`, `CardEdge` `#24FFFFFF` | floating panels (download, notices) |
| `--wash` | `rgba(255,107,107,.13)` to transparent, top down | `NotificationWash` `#22FF6B6B` | the top of a panel that announces something |
| `--accent` | `#FF6B6B` | `Accent` | the one action per view, the selected item, counts |
| `--on-accent` | `#101014` | `CardAccentButton` foreground | text on accent |
| `--soft` | `rgba(255,255,255,.10)`, hover `.16` | `CardSoftButton` `#1AFFFFFF` / `#29FFFFFF` | secondary buttons, tiles |
| `--text` | `#FFFFFF` | | headings, body |
| `--text-2` | `rgba(255,255,255,.72)` | `CardLinkButton` `#B3FFFFFF` | descriptions |
| `--text-3` | `rgba(255,255,255,.56)` | `VersionText` opacity .55 | captions (still AA on `--ground`) |

Dark is not a default here: the app is dark (`ThemeMode="Dark"`), and so is the taskbar it draws on.

## Type

- Headings: **Segoe UI Variable Display**, semibold. Body: **Segoe UI Variable Text**. That is the
  app's own face (`FontFamily` on every window); on non-Windows devices the system face stands in.
  This is the brief, not a default (`avoid-ai-design-ignore: T1`).
- Scale: 56 / 36 / 26 / 20 / 15 / 13 px. Sentence case everywhere. No all-caps labels, no mono labels;
  mono only for hashes and code.

## Shape

- Radius 7: buttons, setting cards. Radius 8: tiles. Radius 14: panels. Radius 18: round glyph
  buttons. The taskbar and its flyouts keep Windows' own radii (4 and 8).
- No drop shadows on the page; flyouts and toasts over the taskbar keep Windows' shadow.
- Blur (`backdrop-filter`) only where something really floats over content: the taskbar and its flyouts.

## Components

- **Accent button**: coral fill, `--on-accent` text, radius 7, white 14% overlay on hover.
- **Soft button**: `--soft` fill, 1px white 10% border, radius 7.
- **Setting card**: icon left, title and description, control or media right; radius 7, `--card`.
- **Nav item**: radius 5, selected = white 6% fill plus a 3x16 coral pill on the left (SettingsWindow `NavItem`).
- **Panel**: `--panel`, edge, radius 14, optional `--wash` on top.
- **Icons**: Phosphor, regular weight, one family.

## Motion

- Looping motion (the bar under the taskbar icons, the face, the karaoke line, clips) is off until the
  visitor turns it on (▶ on the taskbar, quick settings, Start), so the site scrolls light. The home
  page's reel is the exception: it plays unless the visitor turned motion off.
- The home page tells its story with the scroll, one scene a feature, and every scene shows what
  MusicBar does: the taskbar changing outfits (the real taskbar too), the scroll singing a lyric, the
  taskbar melting into the wallpaper, the face peeking up and singing, the gallery sliding sideways.
  Wide screens pin a scene while it plays; narrow ones play it as it passes. Titles come up word by
  word out of their own slot. GSAP + ScrollTrigger, served from /static/vendor.
- No count-ups, no generic fade-up on everything. `prefers-reduced-motion` shows every scene still.

## Copy

Sentence case. No arrow glyphs, no `A · B · C` meta strings, no em or en dashes, no tricolons, no
one-word colored emphasis in headlines. Numbers shown are true (from the server) or not shown.

## Films

Hero and gallery films use this palette and type: the taskbar on a Windows 11 dark desktop, MusicBar's
real renders (`promo/assets/product`, `web/seed/previews`), the face, coral as the only accent.
