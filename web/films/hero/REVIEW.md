# REVIEW: hero loop

Read `NOTE.md` for the beats, the gate numbers and the frame descriptions. Short form:

- Creative calls: one continuous shot, no cuts; the gallery tile's live preview drops into the taskbar
  on the beat (three times); the camera leans toward each pick and follows the drop; one 2x push that centres the band vertically
  for the karaoke line; the panel closes and reopens with the Windows flyout motion, caused by a click
  on the player's cover. The previous print-style attempt (paper, ink, orange, Anybody, Geist Mono) is
  fully removed.
- Storylines not built: a taskbar-only "outfit every bar" reel (hides the gallery, which is the 1.8
  feature) and a fitting-room joke with the face (needs more time than a 12 s loop has).
- Measured: `hyperframes check` (0 errors), `look.py` G0/G2/G3, `lint.mjs` G5, seam difference,
  file sizes and streams. Looked at only: G1, G4, the stills and the two contact sheets. Nothing was
  heard (the film is silent by brief).
- Weakest shot: the karaoke push (frames 162-254): band, face and line now sit in the middle, with
  dark ground above and below the band.
- Warnings kept on purpose: `font_face_local_only` (Segoe UI Variable is the brief's face and is
  installed here); `line_unread` on "Đang dùng" / "Áp dụng" near the end (the state carries through the
  seam into frame 0, so it is on screen far longer than 1.1 s); `composition_file_too_large` (one
  continuous shot, one camera); `text_occluded` at 11.5 s (the falling preview passes over the tile).

## Rebuild
- `npx hyperframes render -o renders/master.mp4 --crf 8` (headless), then `bash encode.sh 19 31`
  writes `web/server/static/media/hero.mp4`, `hero.webm`, `hero-poster.jpg`.
