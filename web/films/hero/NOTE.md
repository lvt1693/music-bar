# NOTE: hero loop, "the preview is the real thing"

## The idea
1.8's gallery tile shows a live preview that is the same render the taskbar gets. So the film picks a
tile, lifts that live preview off the tile and drops it into the taskbar, which wears it on the beat.
Three picks (Gamer RGB, Pumpkin Ember, back to Northern Curtains), a song in between, and the loop
closes on the first look. The face watches every pick and sings the line. No competitor has a tile
that becomes the taskbar, so the film stops working with the logo swapped.

## Beats (30 fps, 118 BPM, beat k = frame round(k x 15.254), 366 frames = 24 beats)
| frames | buys |
|---|---|
| 0-40 | poster: a gallery of live looks above a taskbar already wearing one; the face is still grinning |
| 40-61 | one click puts a look on: press, coral edge moves, Áp dụng becomes Đang dùng, the Gamer RGB preview drops into the band on beat 4 |
| 61-122 | it works for any look: Pumpkin Ember, same gesture, lands on beat 8 |
| 122-162 | done choosing: click the cover, the panel closes like a Windows flyout, the camera pushes 2x and centres the band |
| 162-254 | the point: the mic pops (the one spring), "Hôm nay em được hát rồi" fills word by word, the face mouths each word |
| 254-331 | back out, click the cover, the panel opens, pointer to Northern Curtains |
| 331-366 | it drops on beat 23, the face cheers, and frame 366 is frame 0 |

## Real and illustrative
- Real renders: `web/seed/previews/` northern-curtains, gamer-rgb, pumpkin-ember, lime-summer (tiles,
  falling previews and the taskbar band); `promo/assets/product/lyrics-default.webm` (the karaoke line,
  media 3.12-5.74 s at 1.076x); `cover-dem-thanh-pho.png`, `musicbar-icon.png`.
- Strings: "Kho trực tuyến" and "Đang dùng" (Strings.Settings.cs) and the new single button "Áp dụng" for every tile not on the taskbar (per the coordinator, app and site now share it); item names from
  `web/seed/manifest.json`. Tile layout follows `SettingsWindow.Gallery.cs` GalleryCard (34 px preview,
  radius 8, accent edge on the tile in use).
- Rebuilt, not captured: the panel shell (DESIGN.md panel tokens), the Windows 11 taskbar (after
  `site.css` / `_taskbar.html`), the face and mic (markup and CSS from `promo/index.html`, scaled 5/3).
- Illustrative: the song title "Đêm thành phố" (named after the promo's cover file); the falling preview
  is a film device, the app applies a look without that animation.
- Timing liberties: tile previews play at 0.77x and the Pumpkin Ember band at 0.76x so every visible
  clip is continuous across the loop seam (the previews are not seamless loops: wrap jumps 8-50/255).

## Gates (numbers from the scripts)
- G0 `look.py` on `hero.mp4`: PASS, 12.20 s, 366 frames, matches --expect 12.2; no audio, not required.
- G2 `look.py`: PASS, no run of more than 3 flat frames.
- G3 `look.py`: PASS, end hold 0.03 s (ends mid-motion, intended: it is a loop); no end card.
- G5 `lint.mjs`: first run FAIL on C1 (the player title "Đêm thành phố" at the bottom edge) and C6 (the
  taskbar read as a footer bar plus its hairline). The taskbar is the subject the brief asks for, so the
  taskbar and the panel are marked `data-ui="captured"` (rebuilds of the site taskbar and the app's
  gallery cards), as the lint documents; second run: GATE G5 PASS, all 15 rules.
- G1 by eye (no script): panel title 40 px (0.044 H); tile names 31 px and buttons 29 px (0.034 H and
  0.032 H, under the 0.04 H line; at the site's ~900 px they show at 16-17 px); lyric caps about 60 px
  in the 2x push (0.067 H), the render upscaled only 1.2x. Panel 0.875 W; the band is full width and cropped by both edges.
- G4 by eye (no script): wide frames hold the panel (0.56 of the frame) over the band (0.13), area
  ratio 4.3x, and the camera leans from the picked tile to the band on every drop; the push isolates
  face and lyric.
- MOTION note: mean luma change 1.5/255, below the human band (4.4-9.8). What is alive between beats:
  five real renders playing, the face nodding on every beat, the pointer, a 4 % camera lean per pick.
  Most of the frame is the dark desktop and panel chrome; for a hero that sits under page copy this
  is a deliberate calm, but it is the calmest number the script can print.
- Loop seam (own script, `hero.mp4`): last-to-first change 1.14/255 against a median frame step of
  1.70/255; webm 1.12 against 1.69.

## The 16 sheet frames (`renders/look-final/sheet.jpg`), one sentence each
1. Gallery panel over the taskbar wearing Northern Curtains, its tile edged coral, pointer on it, face grinning.
2. The camera leans right as the pointer travels to Gamer RGB; the face looks after it.
3. Gamer RGB edged coral; its rainbow preview has lifted and slides down across the lower tiles.
4. The taskbar is now the RGB strip; the face is wide-eyed.
5. Gamer RGB in use; the camera leans toward Pumpkin Ember as the pointer goes there.
6. Pumpkin Ember in use, the band has turned warm, the pointer heads for the cover, the face grins.
7. The panel is gone and the camera is pushing onto the warm band and the face.
8. Close on the face with the mic springing up beside it.
9-11. The face sings while "Hôm nay em được hát rồi" fills from left to right on the band.
12. The line is gone, the face smiles, the camera starts to pull out.
13. Wide again with the panel back (Pumpkin Ember in use), pointer on the cover.
14-15. The pointer crosses the panel to Northern Curtains and parks.
16. Northern Curtains in use, the band teal again, the face grinning: the state of frame 0.

Would it pass as a slide? The wide frames are a magnified Windows desktop with real renders moving in
five places; the push frames are a close-up with no layout at all.

## What I would still improve
- The push now centres the band, face, mic and line vertically (2x, world y 605 to 1055), so dark ground shows above and below the band (a solid ground is fixed by
  DESIGN.md; the karaoke render stays at 1.2x so it is sharp).
- Tile names sit at 0.034 H, under the G1 line; the panel is already 0.875 W, so only a tighter crop
  would fix it.
- Gamer RGB's rainbow has a magenta and violet segment; it is the product's real render, kept as is.
- The face is copied from the promo, which has its own mic placement; the mic stands beside the face
  rather than at the mouth.
- The font comes from `local("Segoe UI Variable")`: a render on a machine without Windows 11 falls back.
