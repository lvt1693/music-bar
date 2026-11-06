# Storyboard: the preview is the real thing

## PAGE
- Problem: people don't believe a taskbar can wear someone else's look with one click.
- Audience: Windows 11 users landing on musicbar.truclv.com, muted, at about 900 px wide.
- Goal: in one loop they see a gallery tile turn into their taskbar, three times, and the face enjoying it.
- Emotion: playful, a bit proud; calm enough to sit behind a headline.

One true thing: 1.8's online gallery. A tile's live preview is the same render the taskbar gets
("Bản xem trước chạy y như thật", the app's own line), and one click puts it on.

## Inventory
| | |
|---|---|
| Logo shape | a dark rounded square with two bars and a coral slider: a progress bar |
| Core control | the gallery tile (live preview, name, Tải về / Dùng / Đang dùng), the coral edge on the tile in use |
| Material | the real renders: Northern Curtains, Gamer RGB, Pumpkin Ember, Lime Summer; the karaoke renderer |
| Key verb | put on (an outfit for the taskbar) |
| Success moment | the taskbar wears the look on the beat, the face lights up |
| Voice | casual Vietnamese, a face that sings "Hôm nay em được hát rồi" |
| Colour logic | coral means "on": the tile in use, the focused app on the taskbar, the mic band |
| Type | Segoe UI Variable, the app's own face |
| Would never | purple glow, particles, caps labels, a fake stat |

## Ideas
1. UI documentary of one task (chosen): the gallery panel above the taskbar, a cursor picks a look, the
   tile's preview lifts off and drops into the taskbar, which wears it on the beat. Three picks, a song
   in between, back to the first look so it loops.
2. A held shot on the taskbar alone: the band changes outfit every bar while the face reacts; the gallery
   is never seen. Rejected: it hides 1.8's actual feature (the gallery) and reads as "visualizer reel".
3. A fitting-room joke: the face tries looks on like clothes, shrugs at one, keeps another. Rejected for
   now: it needs a character arc that a 12 s muted loop behind a headline can't carry.

Cover-the-logo test: no other product has a live-preview tile that becomes the taskbar.

## World
- Ground: the Windows 11 dark desktop `#1C1C20`, solid, as the design contract fixes.
- Depth: the MusicBar panel floats above with Windows' flyout shadow; the taskbar is a slightly lighter
  band with its 8 % top edge. The camera is the only lens: wide on the desktop, then a 2x push onto the
  taskbar for the song.
- Light: only what the product emits: the looks themselves. No glow is added.
- Scale: the world is a Windows desktop magnified 2.5x (taskbar 48 px becomes 120 px), so 12 px UI text
  reads at 30 px; the push takes it to 2x more.
- Clock: smooth 30 fps, crisp (no blur grammar except the stretch of the falling preview).

## Beats (frames at 30 fps; beat k = round(k x 15.254))
| # | frames | role | buys | what moves | handoff |
|---|---|---|---|---|---|
| 0 | 0-40 | hook (poster) | the viewer sees a gallery of live looks over a taskbar wearing one of them | previews play, face happy, settles to a smile, cursor leaves tile A | cursor travels to tile B |
| 1 | 40-61 | proof | one click puts a look on the taskbar | press, coral edge jumps to Gamer RGB, its preview lifts and drops, stretching into the band; lands on beat 4 | the falling strip becomes the band |
| 2 | 61-122 | proof | it works for any look, again on the beat | face cheers; cursor to Pumpkin Ember, same gesture, lands on beat 8 | cursor goes to the player |
| 3 | 122-162 | turn | done choosing, now listen | click the cover, the panel closes (Windows flyout close), camera pushes 2x onto face and band | the push lands on the face |
| 4 | 162-252 | the point | the taskbar sings, word by word, and the face sings with it | mic pops (the one spring), lyric band opens, "Hôm nay em được hát rồi" fills word by word, face mouths and nods | line finishes, mic drops |
| 5 | 252-331 | turn | back to choosing | camera pulls out, cursor clicks the cover, panel opens, cursor to Northern Curtains | press |
| 6 | 331-366 | close | it loops: the first look again, on beat 23 | preview drops into the band, face lights up, holds into frame 0 | frame 366 = frame 0 |

At most 25 % "the point": beat 4 only (plus the two landings as supporting proof).

## Text (everything readable on screen)
"Kho trực tuyến"; tile names Northern Curtains, Gamer RGB, Pumpkin Ember, Lime Summer; buttons Đang dùng,
Dùng, Tải về; player title "Đêm thành phố"; the karaoke line from the real renderer.

## Gates planned
- G1: UI text 30-40 px at the wide framing (>= 0.033 H), lyric caps ~60 px in the push; the band and
  panel are cropped by the frame edges (a magnified fragment).
- G3: no end card; the last frame is mid-reaction and flows into frame 0.
- G4: wide = panel (hero while picking) above a full-bleed band; at each landing the eye goes to the band
  because it is the only thing that changes colour; the push isolates face and lyric.
