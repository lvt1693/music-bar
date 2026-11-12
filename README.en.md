# MusicBar

**English** · [Tiếng Việt](README.md)

The taskbar has sat still for thirty years. Let it sing.

**Download:** [installer](https://github.com/lvt1693/music-bar/releases/latest/download/MusicBar-1.8.5-Setup.exe)
or [portable zip](https://github.com/lvt1693/music-bar/releases/latest/download/MusicBar-1.8.5-portable.zip)
(Windows 10 2004 or later, or Windows 11, 64-bit; no .NET to install).

**Website:** [musicbar.truclv.com](https://musicbar.truclv.com), with the community **gallery**
(skins, visualizers, LED strips) to download from or share yours.

**What's new:** 1.8.5 lets you **share looks with your Google account** (see what you sent and why
something was turned down on the website), and the gallery takes **skins up to 4 MB**. 1.8.4 turns **NetEase lyrics** on for new installs, works with the **Tab key and screen
readers**, fits its windows on small screens, and **stops sending meeting names and web tabs** to the
lyrics sites. 1.8.3 runs **big skins** on Direct3D 11, adds a **search box** for the looks, makes the
lyrics in the biggest gap clickable and fixes the cover showing Spotify's logo. 1.8.2 makes **every screen's taskbar transparent**, also while nothing plays, and fixes a
**stutter every few seconds** and a **slow settings window**. 1.8.1 adds a **log to send** with song titles
masked, a **safe mode** for Explorer and a switch to turn hardware acceleration off. 1.8 brings an **online gallery** right inside the app, a **welcome tour** (every choice shows on your taskbar as you pick it),
fixes the progress bar vanishing with a transparent taskbar, **uses far less RAM** (no more
creeping up over long runs), works properly with an **auto-hiding
taskbar**, reads **VLC**, and adds a button to
restore the default settings and a red-dot option for notifications. Every version is listed in the [CHANGELOG](CHANGELOG.md) (in Vietnamese; each
[release](https://github.com/lvt1693/music-bar/releases) on GitHub ends with a paragraph in English).

![A sleepy taskbar, a record scratch, the MusicBar icon drops in and the stage lights come on](docs/wake-up.gif)

Music plays all day and the taskbar only shows a clock. MusicBar turns the whole width of the
taskbar into a progress bar that takes its colors from the cover art and moves with the music
actually playing, puts the cover and ⏮ ⏯ ⏭ next to the system tray, and runs karaoke lyrics in
the taskbar's free space. The taskbar still clicks, drags and pins apps as before: MusicBar only
sits underneath it.

It follows any app that plays through Windows Media Session: Spotify, YouTube in a browser, Media
Player, Apple Music… VLC doesn't tell Windows what it plays, so turn on **Read VLC** in Settings →
General and MusicBar reads VLC directly. With no music it gets out of the way and the taskbar is
back to normal.

The app's interface is in English by default, with Vietnamese one click away. The
[wiki](https://github.com/lvt1693/music-bar/wiki) (with a picture of every settings page) is in
Vietnamese.

## What it does

- **A progress bar across the whole taskbar**, under the icons. Fourteen styles (table below),
  each colored from the cover art, the playing app's color, Windows' accent or a color you pick.
  It runs in 4 directions: left to right, right to left, out from the middle, or in from both
  ends. Don't want it running behind the icons? Turn on **Only in the empty space**: the bar
  keeps clear of Start, the app icons and the tray, runs on from one empty stretch to the next,
  and fades out softly beside the icons.
- **Or a visualizer instead of the progress bar:** six that only dance to the music, at 60 frames
  a second, and ten **RGB LED strip** effects like a gaming LED strip under the screen. Every style
  keeps its own palette, and you can make your own skins (more below). In the settings they sit
  in four tabs: Progress, Visualizers, RGB LED strip, Skins; hover a tile and the line below
  describes it.
- **Taskbar on any edge:** bottom, top, or standing on the left or right. On a vertical taskbar
  the progress runs bottom to top, the cover and ⏮ ⏯ ⏭ stack, the lyrics show one word per line,
  and the song card and control center open to the side.
- **Several screens:** show the progress bar on the main screen, on all of them or on one, and
  pick a screen of its own for the player and the lyrics (**Settings → General → Screens**).
- **Minimal if you like:** hide the player and keep the lyrics (they move into the largest gap),
  or hide both and keep just the bar (**Settings → Player → On the taskbar**).
- **A face that lives on the taskbar** (more below): sings into a mic with the music, dozes off at
  night, giggles when poked, sulks when poked too much, wears a Santa hat at Christmas, and keeps
  a Pomodoro timer for you.
- **Your year in music** (more below): top songs, favorite artist, golden hour, what kind of
  listener you are, all in one picture to show off.
- **A welcome tour the first time:** the bar's look, the taskbar's background, the player, the
  lyrics, the face and a few habits, each showing on your taskbar as you pick it (with nothing
  playing, a demo song plays meanwhile). Any step can be skipped, and each says where it lives in
  the settings. Take it again from Settings → About.
- **Tells you about updates:** at start (and daily) it checks MusicBar's website (GitHub when the site can't be reached) and, if there's a new
  version, asks once in the corner of the screen.
- **See-through, light or dark:** by default the taskbar goes see-through with the bar right under
  the icons. For a background, pick dark, light, *Match Windows* (it changes when Windows does) or
  *Match Windows, tinted with the song's color*.
  On a light background the lyrics, buttons and face turn dark so Windows' black icons stay
  readable. With a transparent taskbar (or a very faint background) MusicBar looks at the
  wallpaper right behind the taskbar: dark text on a bright wallpaper, white on a dark one, live
  wallpapers from Wallpaper Engine included.
- **Moves with the real music.** The animated styles listen to what your speakers play (WASAPI
  loopback), processed on your computer and sent nowhere. Turn it off and they move to a made-up
  beat.
- **Synced lyrics** from [LRCLIB](https://lrclib.net), karaoke-colored word by word, with 8 line
  effects (slide, fade, swipe, zoom, word by word, typewriter, ticker…). The lyrics find the
  largest free stretch of the taskbar, or slide out beside the cover. Downloaded lyrics are kept
  as `.lrc` files, so next time no network is needed. Out of sync? Scroll over the lyrics to move
  them 0.25 s earlier or later, for each song. Click the lyrics for the whole song (more below).
- **Your own type:** size, color, weight, outline (thin is a crisp edge, wide is a soft halo),
  and 10 free fonts with full Vietnamese diacritics: Be Vietnam Pro, Lexend, Montserrat,
  Quicksand, Nunito, Comfortaa, Playfair Display, Lora, Dancing Script, Pacifico.
- **A quiet zone around the lyrics:** right under the line on screen the progress bar dims to
  about 30 %, as long as the line itself, so the words always read clearly.
- **A transparent Windows 11 taskbar**, done by MusicBar itself, nothing else to install (more
  below).
- **Its own control center:** the network / volume button in the tray opens a panel with
  notifications, quick settings, volume and output device. Lists of speakers, Wi-Fi networks and
  Bluetooth devices slide out beside it; pick one and it switches straight away.
- **Windows' taskbar settings:** small / medium size (reads Windows 11's new *Taskbar size*
  setting where it exists), Start and icons in the center. Works with the Windows 10 style taskbar
  of [ExplorerPatcher](https://github.com/valinet/ExplorerPatcher) too.
- **Song info card:** rest the pointer on the cover for a big cover, artist, album, year and
  genre, with a seek bar.
- **English and Vietnamese:** English by default; switch to Vietnamese in the language box at the
  top left of the settings window. It changes at once, no restart.
- **Seven button styles for ⏮ ⏯ ⏭** (**Settings → Player → Button style**, or right-click the
  player): Glow, Minimal, Capsule, Progress ring (a ring round the play button follows the song),
  Vinyl (spins while playing, the cover is the label), Cassette keys (keys that press in, with a
  lamp) and Bubbles (they wobble like jelly when pressed).

## Fourteen outfits

Pick one in **Settings → Progress bar** (the previews run live with the song that's playing) or
right-click → Progress bar style.

![The taskbar wearing Aurora on a stage](docs/outfits.jpg)

| Style | What it looks like |
| --- | --- |
| Aurora (default) | Three ribbons of light in the song's palette, swinging wider with the music |
| Color mesh | Big patches of the cover's colors drift and blend, swelling with each frequency band |
| Silk | 14 fine threads twist around each other and twine into one at the playhead |
| Holographic | Holographic foil shifting around the song's color, a streak of light gliding across |
| Bokeh | Out-of-focus lights near and far, each pulsing with a frequency band |
| Ink bloom | Watercolor spreading behind the playhead, the drops still blooming after it passes |
| Cover stripes | Each column of the cover becomes a stripe, revealed as the song plays |
| Cinema | The cover, deeply blurred, drifting slowly like a film scene |
| Neon tube | A neon tube along the edge, flickering and casting light on the taskbar |
| Liquid | A rippling liquid surface at the playhead, with tiny bubbles |
| Spectrum | A real audio spectrum, the played part colored in |
| Waveform | A SoundCloud-style waveform, drawn from the real sound as it plays |
| LED grid | A grid of sparkling squares, the playhead's column doubling as a level meter |
| Classic | A color fill with a glow at the playhead |

The animated styles draw 20 to 30 frames a second while playing. Spectrum, the visualizers and
skins follow the display (60 frames a second) on mains power, and drop to about 30 on battery.
Paused, they drift slowly in place, the music-driven parts settling into a gentle breath; to have
them stand still, turn off **Keep moving when paused** in Settings → Progress bar.

### Visualizers

Don't care where the song is, just want the taskbar to dance? Pick a visualizer on the same page:

| Style | What it looks like |
| --- | --- |
| Equalizer | Bars across the taskbar, bass in the middle, treble at the ends, with slowly falling peaks |
| Horizon | The spectrum as a ridge mirrored on a line of light |
| Oscilloscope | The sound wave itself, a glowing trace that holds still instead of jittering |
| Aura | Soft lights swelling with their part of the music, flaring on the drums |
| Fireflies | Sparks rising from the screen edge, more as it gets louder, a burst on every beat |
| LED matrix | A dot-matrix spectrum, cool to hot, with held peaks |

### RGB LED strip

A strip of light along the screen edge, washing softly up over the taskbar like an LED strip
behind a diffuser, with effects like a gaming LED controller's, only driven by the music:

| Effect | What it looks like |
| --- | --- |
| Rainbow wave | A rainbow running along the strip, faster and brighter as the music swells |
| Rainbow spectrum | Each part of the strip is a frequency: red bass in the middle out to violet treble |
| Breathing | The whole strip breathes with the music, drifting through your palette's three colors |
| Ripple | Every beat drops a ripple that spreads along the strip |
| Scanner | A bright eye sweeping end to end with a fading trail, quicker when it's loud |
| Starlight | Lights twinkling at random, thicker in loud passages, a shower on the beat |
| Fire | Flames flickering along the strip, the bass throwing sparks |
| Meteors | A meteor shoots across on every beat, its tail sparkling out |
| Marquee | Bands of light in three colors chasing each other, surging on the beat |
| VU meter | Green, then yellow, then red out from the middle as it gets louder, peak held |

Rainbow wave, rainbow spectrum, fire and the VU meter have colors of their own; the other effects
follow the palette you pick.

### Palettes

Every style (visualizers and skins too) keeps its own colors: **The song's** (default), one of 11
presets (Sunset, Ocean, Aurora, Neon, Candy, Sakura, Forest, Ember, Gold, Glacier, Silver), or
**Your own** three colors. Pick them right under the style gallery.

### Your own skins

A skin is a small `.hlsl` file: a pixel shader that paints the bar on the graphics card,
Shadertoy style. MusicBar hands it the time, the progress, the loudness, the beat, the spectrum,
the waveform and the palette's three colors. A skin can only draw: it reads no files and reaches
no network. **Add the sample skins** gives you 4 (Liquid Chrome, Comet, Plasma, Synthwave);
**Import a skin…** adds your own. Can't write shaders? Paste the [skin guide](docs/skins.md) into
ChatGPT / Claude / Gemini with a description of what you want: it has a ready-made prompt.

**Skin renderer** (Settings → General): **Automatic** by default. A skin that fits pixel shader 3.0 is drawn
as a WPF effect as before (the lightest way); one too big for it moves to **Direct3D 11** on its own (shader
model 5, next to no size limit, about 35 MB more RAM only while that skin runs). Always Direct3D 11 and
Classic only can be picked too.

## Install

- **Installer** (`MusicBar-1.8.0-Setup.exe`): installs for your account only, in
  `%LOCALAPPDATA%\Programs\MusicBar`, no admin prompt, with a Start menu entry, and uninstalls
  from Settings → Apps. Before installing over it or uninstalling, MusicBar is asked to quit, so
  the taskbar and tray go back exactly as Windows arranged them.
- **Uninstalling puts things back:** at its very first start, before touching anything, MusicBar
  records how Windows was set up (the taskbar's light or dark mode, taskbar size, centered or
  left icons, ExplorerPatcher's taskbar style) in `%LOCALAPPDATA%\MusicBar\windows-before.json`.
  Uninstalling puts back each of these that MusicBar changed, unless you changed it again yourself
  (then your choice stays). A normal exit only hands back what MusicBar holds while it runs (the
  transparent taskbar, the folded tray, the light or dark mode it switched); a taskbar size or
  alignment you chose in MusicBar stays, since that was your choice.
- **Portable** (`MusicBar-1.8.0-portable.zip`): unzip anywhere and run `MusicBar.exe`.

**Updates:** from 1.4 on, MusicBar checks for a new version every time it starts (from 1.8.2 it asks
[musicbar.truclv.com](https://musicbar.truclv.com) first and downloads the installer from there, GitHub when the site can't be reached) (and once a day while
running). When there's a new version a card shows above the tray: *Update now* downloads it,
checks its SHA-256, installs quietly and reopens; *Later* asks again next time; tick *Skip this
version* to stay quiet until the one after. In the portable copy the button opens the download
page instead. Turn it off in **Settings → About**. On 1.3 or older, download 1.4 by hand once; it
tells you from then on.

Coming from 1.1 or older, restart Explorer (or sign out and back in) once, so the new taskbar
transparency module replaces the old one. Until then MusicBar still runs, but on several screens
it tints the taskbar instead of making it transparent, and the settings remind you.

Both downloads are the same app with .NET 10 bundled, so there's nothing else to install, and
while music plays it uses about 50 MB of RAM (Task Manager's Memory column, measured on the test
PC), which stays put over long runs. The welcome
tour opens by itself the first time; after that, click the tray icon for the settings. To start it with Windows,
turn on **Start with Windows** in the settings. Settings, lyrics and the log live in
`%LOCALAPPDATA%\MusicBar` for both downloads.

The app isn't code-signed yet, so SmartScreen will stop it the first time: click **More info →
Run anyway**.

Command line, if you like:

```powershell
MusicBar.exe --demo       # a sample song with lyrics, no player needed
MusicBar.exe --settings   # open the settings (even while it's running)
MusicBar.exe --welcome    # open the welcome tour
MusicBar.exe --recap      # show the year-in-music banner now, without waiting for Christmas Eve
MusicBar.exe --exit       # quit and give the taskbar back
```

## Transparent taskbar

Windows doesn't let anything draw "inside" the taskbar, so the progress bar is a window of its own
kept just below the taskbar, and it only shows through when the taskbar is transparent. Turn it on
in **Settings → Taskbar → Make the taskbar transparent**. Once on, every screen's taskbar is
transparent, including screens without the bar and while nothing plays. If
[TranslucentTB](https://github.com/TranslucentTB/TranslucentTB) is running, MusicBar leaves the
job to it.

With an **auto-hiding taskbar**, MusicBar keeps Windows' own background and tints the bar over it:
when the taskbar slides up there are app windows right behind it, not the wallpaper, and a clear
taskbar would show their text through the icons and lyrics. The bar, player and face slide up and
down with the taskbar.

- **Windows 10 style taskbars** (Windows 10, ExplorerPatcher): MusicBar sets a DWM attribute on the
  taskbar window, and sets it again whenever Explorer changes the background (opening Start,
  changing the theme).
- **Windows 11's own taskbar:** the background is a XAML element inside Explorer. MusicBar loads a
  small module (`MusicBar.TaskbarTap.dll`, embedded in the app) into Explorer the way TranslucentTB
  does, written independently. It uses the public XAML diagnostics API to take the background's
  opacity to 0 (the color itself is left alone, so Windows still changes it with light and dark
  mode). It only keeps the background clear while MusicBar is alive: when MusicBar exits or is
  ended abruptly (Task Manager included), the taskbar gets its background back. A newer module asks
  the older one in Explorer to step aside, so updating doesn't need an Explorer restart.
- **Windows' icons follow the background:** a light MusicBar background while Windows is in dark
  mode would leave the tray, clock, Wi-Fi and volume icons white on light. MusicBar switches
  Windows' taskbar mode to match, and switches it back when it exits (the module in Explorer does
  it if MusicBar is ended abruptly). Turn it off in **Settings → Taskbar → Windows' taskbar icons
  follow the background**.

| Layer mode | When |
| --- | --- |
| Automatic (default) | Under the icons when the taskbar can be transparent, tinted over it otherwise |
| Under the taskbar icons | The taskbar is already transparent: it looks like the taskbar's own background |
| Tinted over the taskbar | The taskbar isn't transparent: drawn on top, its strength capped so the icons stay clear |

## Control center

![MusicBar's control panel: notifications, quick settings, volume, and the speaker list sliding out to the left](docs/control-center.jpg)

While MusicBar runs, the network / volume button (Windows 11) or the Control Center button
(ExplorerPatcher) opens MusicBar's panel. From top to bottom:

- **Notifications** from Windows: click one to open the app that sent it, × to dismiss it,
  *Clear all* to tidy up.
- **Quick settings:** Wi-Fi, Bluetooth, Night light, Live captions turn on and off for real.
  *Project* (like Win+P) and *Cast* (Miracast) open a list that slides out to the side.
- **Wi-Fi:** the arrow on the Wi-Fi tile opens the list of networks. Click one to connect (with a
  password field, a show-password button and *Connect automatically*), right-click to connect,
  disconnect or forget. From Windows 11 24H2, Windows only lets apps list Wi-Fi networks with
  location access; with location off, MusicBar opens Windows' own Wi-Fi list instead.
- **Bluetooth:** paired devices and devices nearby. Pair (the PIN shows right in the panel),
  connect or disconnect headphones and speakers, remove a device, with a right-click menu too.
- **Screen brightness** (laptops, or monitors that accept DDC/CI over the cable): drag or scroll,
  like the slider in Windows' Quick Settings. Where brightness can't be changed, the row hides.
- **Volume** and **output device**: the speaker list slides out, pick one and it switches. Scroll
  over the volume row, or right on the network / volume button in the taskbar, for 2 % a notch,
  as in Windows.

**Win + A** opens MusicBar's panel (press again to close); turn that off in **Settings → Taskbar**.
Win + N is still Windows' notification center. Turn the feature off, or quit MusicBar, and the tray
is exactly as Windows arranged it. New notifications show as a number on the tray button; for
something quieter, turn on **A red dot for new notifications** in **Settings → Taskbar**.

## The face

People loved the face in the promo film, so now it lives on the taskbar, next to the player (move
it in **Settings → Face**).

- **8 looks:** Classic (the face from the film), Classic girl, Anime girl, Anime boy, Alien, Robot
  (LED eyes, a mouth that's a little equalizer), Cat, Derp.
- **Your own:** a picture, GIF or WebP (animated is fine) for each mood: Idle, Music playing,
  Paused, Asleep, Bored, Happy, Dizzy, Worried, Focused. Only *Idle* is needed; a mood without a
  picture borrows a close one. Pictures are kept to 256 px so it stays light, and they still bob,
  tilt and bounce with the music like the drawn faces. Pick them in **Settings → Face → Your own**.
- **Follows what's going on:** bobs along and sings with the music, star eyes on a new song, looks
  around while paused, gets misty when the music stops after a long session.
- **Follows the clock:** yawns and stretches in the morning, gets hungry before noon, nods off
  after lunch, jumps on the hour, droops late at night, falls asleep (with a Zzz) when you leave
  the computer, and wakes when you're back.
- **Never quite still:** blinks, glances around, hums, winks, sticks its tongue out, and follows
  the pointer when it comes close.
- **Plays along:** click it and it laughs; keep clicking and it gets annoyed, then cross, then
  turns away sulking; drag across it (or move the pointer back and forth over it) and it laughs
  till it cries, then sends a heart; double-click for star eyes; scroll and it gets dizzy; poke it
  while it sleeps and it jumps.
- **The mic is back:** with music on, the mic from the film pops out and it sings along; it puts
  it away when you pause.
- **Hats, glasses, headphones:** a cap (in the song's color), a beanie, a crown, round glasses,
  sunglasses, pixel "deal with it" shades, headphones. In season it changes hats by itself: apricot
  blossom for Tết (the Lunar New Year), rabbit ears for the Mid-Autumn Festival (by the lunar
  calendar), a witch's hat for Halloween, a Santa hat for Christmas.
- **Focus timer (Pomodoro):** right-click the face → *Start focusing*. It frowns and works with you,
  with a small clock beside it; when time's up a speech bubble reminds you to stand up, stretch and
  drink some water. Every 4th round is a long break. Change the minutes in **Settings → Face**.
- **Keeps an eye on the computer:** sweats and steams when the CPU is flat out, panics when the
  network drops, cheers when it's back, beams when you plug in the charger, flags on low battery.
- **Goes for a stroll:** idle with no music, it now and then walks along the empty taskbar, looks
  around and comes back. Never over the player or the lyrics.
- Right-click it to change its look, put a hat on, start the timer or hide it. With no music and
  MusicBar hidden, the face can stay and doze (**Stay when MusicBar hides**, can be turned off).

## Lyrics

![Karaoke lyrics on the taskbar in Playfair Display](docs/karaoke.jpg)

Where they come from, in order: an `.lrc` file in the lyrics folder (`%LOCALAPPDATA%\MusicBar\Lyrics`,
named `Artist - Title.lrc`; drop your own in too), then LRCLIB by title, artist, album and length,
then NetEase (on unless you turn it off). A song with no lyrics is remembered for a day so it isn't asked
again (turning NetEase on asks again at once). If your network needs a proxy, fill it in under **Settings → Lyrics → Proxy**.

- **The right artist:** a song of the same name by a different artist is dropped, unless it's
  exactly as long as what's playing (the same recording, the artist just spelled differently).
  Better to say there are none than to show another song's words.
- **Word-by-word karaoke:** LRCLIB can now store when each word is sung. Where a song has that,
  each word lights up exactly when it's sung, instead of being estimated from the line's length.
  Not many songs have it yet.
- **Share lyrics to LRCLIB:** have an `.lrc` of your own that LRCLIB lacks, or corrected the timing
  of some lyrics? In the lyrics panel click *Share on LRCLIB*, fix the title and artist if needed,
  and *Send*. MusicBar solves LRCLIB's anti-spam puzzle (a few seconds) and sends it; corrected
  timing becomes a new version of that very song on LRCLIB rather than a duplicate. From then on
  everyone using LRCLIB has it.
- **NetEase as a fallback:** the switch is **Settings → Lyrics → Also ask NetEase**. When LRCLIB has no
  synced lyrics (or is down), MusicBar asks NetEase Cloud Music, strong on Korean, Chinese and
  Japanese music. Only the right artist and length, no live, remix or cover versions. NetEase
  results show in the manual search too. It's on for new installs; an install updated from an
  earlier version keeps what it had (off before). It's an unofficial source and may stop working any time.
- **YouTube titles of any length:** something like "HIEUTHUHAI - Không Thể Say (prod. by Kewtiie) |
  Album 'Ai Cũng Phải Bắt Đầu Từ Đâu Đó'" is split at `-` and `|`, loses the Album, OST, prod.,
  Official MV, Lyrics Video, track numbers and hashtags, the channel name is recognized, and what's
  left is "Không Thể Say". If LRCLIB still has nothing, MusicBar keeps trying shorter names, and the
  title without an artist (a label's channel is often not the singer's name).
- **The whole song:** click the lyrics beside the cover (or right-click → Lyrics → Whole song's
  lyrics…). The line being sung lights up and stays in the middle; click a line to play from it;
  there are earlier / later buttons (hold one to keep moving, faster and faster), and *Find other
  lyrics* for when the wrong song came up: type the name, pick the right result (✓ marks the ones
  as long as what's playing), and MusicBar remembers your pick next time.
- **Offsets read like film subtitles:** *Lyrics later +0.50 s* shows the lyrics later, *Lyrics
  earlier −0.50 s* shows them sooner. Inside an `.lrc` file the `[offset:]` tag keeps the LRC
  standard (positive is earlier) so other players read it right.
- **Karaoke at the singer's pace:** for lines without word timing, the color sweeps at a speed
  estimated from the syllables (one per word in Vietnamese, one per character in Chinese, kana
  and Hangul) instead of stretching to the next line, so it stays with the voice.
- **Romanization of Korean, Japanese and Chinese:** under each line or instead of it (**Settings →
  Lyrics → Romanization**), and in the lyrics panel. Korean follows the standard romanization (with
  sound changes across syllables), Chinese becomes pinyin with tone marks, Japanese becomes romaji.
  Windows only reads Japanese kanji with Japanese installed; without it, kana still becomes romaji
  and kanji stay as they are.

## Your year in music

MusicBar counts what you listen to: songs, artists, minutes, days, hours and apps, kept in a small
file per year in `%LOCALAPPDATA%\MusicBar\Stats`. It goes nowhere; turn it off in **Settings →
About → Count what I listen to**. A song counts as a play after half a minute, and every repeat
counts again.

From Christmas Eve to January 7 a banner in the corner of the screen offers your recap (at most
once a day; tick *Don't remind me this year* to stop it). Any other time, right-click →
*Your year in music…*. The card has your total hours, top 5 songs, favorite artist, busiest day,
golden hour, longest streak, favorite app, and what kind of listener you are: Night owl, Early
bird, Nine-to-five groover, On repeat, Explorer or The all-rounder. *Save picture* writes a
1080 × 1350 PNG (Facebook's portrait size), *Copy* lets you paste it straight into a post.

## Build

You need the .NET 10 SDK, and MSVC (Visual Studio or Build Tools with the "Desktop development with
C++" workload) for the Windows 11 taskbar module.

```powershell
dotnet build src/MusicBar/MusicBar.csproj
dotnet run --project src/MusicBar -- --demo
.\installer\build.ps1 -Version 1.8.0     # into artifacts\: Setup.exe (Inno Setup 6) and portable.zip
```

## Inside

```
src/MusicBar/
  App.xaml(.cs)                 startup, single instance, tray icon, --uninstall
  Models/AppSettings.cs         every option (%LOCALAPPDATA%\MusicBar\settings.json)
  Services/MediaService.cs      follows the media session, interpolates the position, controls playback
  Services/LyricsService.cs     .lrc cache + LRCLIB;  LrcParser.cs reads LRC
  Services/TitleCleaner.cs      cleans YouTube titles, the names to try one after another
  Services/Lyricsfile.cs        reads LRCLIB's Lyricsfile (word timing)
  Services/NetEaseLyrics.cs     NetEase Cloud Music as a fallback lyrics source
  Services/WallpaperTone.cs     how bright the wallpaper behind the taskbar is, for the text color
  Services/Romanizer.cs         Korean / Japanese / Chinese romanization (pinyin from Unihan data)
  Services/ListeningStats.cs    listening time for the recap;  YearRecap.cs works the recap out
  Services/UpdateService.cs     looks for new versions (website first, GitHub as fallback), downloads, checks SHA-256, installs
  Services/SkinLibrary.cs       compiles skins (HLSL) with the shader compiler Windows has
  Services/SystemSnapshot.cs    Windows as it was before MusicBar, put back on uninstall
  Services/FocusTimer.cs        the Pomodoro timer;  SystemSense.cs CPU, network, battery for the face
  Services/AudioOutputs.cs      output devices, volume, switching the default speaker
  Services/NotificationFeed.cs  reads / dismisses Windows notifications
  Services/WifiNetworks.cs      scans, joins, forgets Wi-Fi networks (wlanapi)
  Services/BluetoothDevices.cs  Bluetooth devices, pairing;  BluetoothAudioLink.cs connects headphones
  Interop/TaskbarTracker.cs     where the taskbar is (vertical and auto-hiding ones too), its free space
  Interop/TaskbarAutomation.cs  reads the taskbar's buttons through UI Automation COM, without leaking
  Services/Community.cs         the online gallery: browse, install (SHA-256 checked), share; sponsor banners
  Services/LookPresets.cs       presets from the gallery (a style, three colors, strength, direction; no code)
  Interop/XamlTaskbar.cs        loads the Windows 11 taskbar transparency module
  Interop/TrayMerge.cs          hides the volume / bell icons, folds the tray, restores it
  Views/SeekRenderer.cs         the 14 progress styles, the visualizers, 4 directions;
                                SeekRenderer.Leds.cs the RGB LED strip
  Views/CpuRendering.cs         the taskbar's windows draw on the CPU (saves up to 200 MB of driver memory)
  Views/LyricsPresenter.cs      lyrics and their effects;  WordStackPresenter.cs for vertical taskbars
  Views/TaskbarOverlay.cs       ties it together: layering, colors, lyrics, menu
  Views/TaskbarOverlays.cs      one overlay per chosen taskbar
  Views/LyricsPanelWindow       the whole song's lyrics, manual lyrics search
  Views/RecapWindow             the year-in-music card;  RecapBannerWindow the year-end banner
  Views/Face/                   the face: FaceBrain (moods), FaceRig (motion), FacePainters (8 looks),
                                FaceProps (hats, glasses, mic), FaceSeasons (seasonal hats, lunar calendar)
src/MusicBar.TaskbarTap/        the C++ module that runs in Explorer (built with MSVC along with the app)
installer/                      Inno Setup + build.ps1
promo/                          the promo film (HyperFrames)
```

## The promo film

The 52-second film is in the [release](https://github.com/lvt1693/music-bar/releases/latest): a
taskbar dozing off, a sad trumpet, a record scratch, then the stage lights come on. Everything
MusicBar in the film is drawn by the app's own code (`promo/tools/capture` calls SeekRenderer,
LyricsPresenter and the control panel directly); the rest is HTML put together with
[HyperFrames](https://hyperframes.heygen.com). Voice from queen-tts, music and sound effects from
ElevenLabs. To rebuild it: go to `promo/` and run `npx hyperframes preview`.

## Honest notes

- Tested properly on Windows 11 (build 26300), both the native taskbar and ExplorerPatcher's.
  Windows 10 has code paths, and icons and fonts have Windows 10 fallbacks, but it hasn't been
  tried on a real Windows 10 machine. If something breaks on Windows 10, please send
  `%LOCALAPPDATA%\MusicBar\musicbar.log`.
- The Live captions tile only exists from Windows 11 22H2; older versions won't show it.
- The Windows 11 taskbar module stays in Explorer until Explorer restarts, even after MusicBar is
  uninstalled. It does nothing while MusicBar isn't running. Antivirus software may notice a DLL
  being loaded into Explorer.
- Not code-signed yet, so SmartScreen asks the first time.
- Several screens have only been tested from users' logs; the development PC has one screen. A
  vertical taskbar has been run for real on the test PC (standing on the left).
- The Bluetooth list hasn't been tried with real devices (the test PC has no Bluetooth). Connecting
  and disconnecting headphones talks straight to the Bluetooth audio driver, as Windows has no
  public API for it. If it fails, please send the log.
- On Windows 11, the network / volume button is recognized by its display name (English and
  Vietnamese Windows).
- The Night light tile writes an undocumented CloudStore value (the format was worked out by the
  community). If it finds a field it doesn't know, MusicBar doesn't write.
- Clicking the taskbar makes Windows bring it to the top. MusicBar puts the order back at once,
  and checks again every 250 ms.
- NetEase is an unofficial source. Asked too often, it answers with unrelated songs; MusicBar then
  says there are no lyrics rather than showing wrong ones, which also means NetEase sometimes
  "sleeps" for a while. Sharing lyrics to LRCLIB is the lasting way to fill the gaps.
- Chinese romanization reads each character with one fixed sound, so characters with several
  readings (长, 地, 得, 行…) can come out wrong. Japanese kanji need Japanese installed in Windows
  (the development PC doesn't have it, so that part was only tested with made-up data).
- The battery reactions haven't been tried on a real laptop (the test PC is a desktop). The
  year-end banner was tested with `--recap`; nobody has waited for a real Christmas Eve yet.
- Automatic updates only work from 1.4 on: older versions don't know to ask GitHub.
- The brightness slider has only run on a desktop whose monitor doesn't accept DDC/CI (the row
  hides correctly); the laptop path (through WMI, as Windows does it) hasn't been tried on a real
  laptop.
- Win + A is caught with a keyboard hook; MusicBar slips an invisible key in between so Windows
  doesn't open Start when the Windows key is released, the way PowerToys does it.
- Skins run on the graphics card (pixel shader 3.0, or Direct3D 11 for big ones). A PC without
  Direct3D 11 (some virtual machines, Remote Desktop) only has pixel shader 3.0, so a skin too big for it
  doesn't run; one with only software graphics only runs older shaders, and a long skin may not
  compile; MusicBar then keeps the progress style you had.
- Reading VLC uses VLC's own web interface: MusicBar turns it on in VLC's settings file (`vlcrc`,
  keeping a copy as `vlcrc.musicbar-backup`), listening on this computer only (127.0.0.1), port
  47328, with a random password; a port or password you set there already is kept. VLC reads that
  file when it starts, so it needs opening again once. When another app is playing, MusicBar
  follows that one, unless you pin VLC.
- Hovering a video on YouTube makes the browser play a preview: MusicBar waits 2.5 seconds to see
  whether the song really changed before switching lyrics, so moving to a new song in the browser
  changes the lyrics a few seconds late.

## Thanks

[TranslucentTB](https://github.com/TranslucentTB/TranslucentTB) showed the way into the Windows 11
taskbar's XAML background. [LRCLIB](https://lrclib.net) for the lyrics.
[ExplorerPatcher](https://github.com/valinet/ExplorerPatcher) keeps the classic taskbar alive.
Fonts from [Google Fonts](https://fonts.google.com). The installer is built with
[Inno Setup](https://jrsoftware.org/isinfo.php).

If MusicBar is useful to you and you'd like to buy me a coffee, the QR code in the
[Vietnamese README](README.md) is a Vietnamese bank transfer (VPBank · 178971468 · LE VAN TRUC).

## License

[MIT](LICENSE). The bundled fonts are under the SIL Open Font License; their licenses are in
`Assets/Fonts/licenses` next to the app. The pinyin table comes from Unicode's Unihan database;
the Unicode license is in `Assets/Data/licenses`.
