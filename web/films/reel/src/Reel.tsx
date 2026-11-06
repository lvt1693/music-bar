import React from 'react';
import {AbsoluteFill, Freeze, Img, OffthreadVideo, cancelRender, staticFile, useCurrentFrame} from 'remotion';
import {CATEGORY_GLYPH, COUNT, LOOKS, Look} from './looks';
import {
  BAND,
  Band,
  LIST_X,
  PANEL_H,
  PANEL_W,
  PANEL_X,
  PANEL_Y,
  ROW_H,
  ROW_W,
  Rect,
  SEL_STRIP,
  STRIP_H,
  STRIP_W,
  STRIP_X,
  STRIP_Y,
  TB_H,
  TB_TOP,
  TEXT_X,
  UI,
  W,
  bandAt,
  clipFrame,
  relOf,
  rowTop,
  rowVisible,
  scrollAt,
  selectionAt,
} from './timeline';

// The site is bilingual, so the film shows no sentences in either language:
// only look names, the author's name, and the category as a glyph.

// ------------------------------------------------------------------ tokens (web/DESIGN.md)
const GROUND = '#1C1C20';
const CARD = 'rgba(255,255,255,.05)';
const CARD_LINE = 'rgba(255,255,255,.07)';
const SELECTED = 'rgba(255,255,255,.06)';
const PANEL_BG = 'rgba(20,20,24,.96)';
const PANEL_EDGE = 'rgba(255,255,255,.14)';
const WASH = 'rgba(255,107,107,.13)';
const ACCENT = '#FF6B6B';
const SOFT = 'rgba(255,255,255,.10)';
const SOFT_LINE = 'rgba(255,255,255,.10)';
const TEXT = '#FFFFFF';
const TEXT_2 = 'rgba(255,255,255,.72)';
const TEXT_3 = 'rgba(255,255,255,.56)';
const SCREEN = '#0E0E10';
// the taskbar (site.css --win-line, .tb-start)
const WIN_LINE = 'rgba(255,255,255,.08)';
const START_BLUE = '#4CC2FF';

const R_CARD = 7 * UI;
const R_TILE = 8 * UI;
const R_PANEL = 14 * UI;
const R_STRIP = 4 * UI;
const R_BUTTON = 4 * UI;
const HAIR = 1 * UI;

const DISPLAY = '"Segoe UI Variable Display", "Segoe UI", sans-serif';
const BODY = '"Segoe UI Variable Text", "Segoe UI", sans-serif';

// ------------------------------------------------------------------ font guard
// Segoe UI Variable is a system font on Windows 11. If Chrome cannot find it the
// text silently falls back, so measure once and stop the render instead.
let fontsChecked = false;
const checkFonts = () => {
  if (fontsChecked) {
    return;
  }
  fontsChecked = true;
  const ctx = document.createElement('canvas').getContext('2d');
  if (!ctx) {
    return;
  }
  const probe = 'Lavender Lightsticks 0123';
  const width = (font: string) => {
    ctx.font = font;
    return ctx.measureText(probe).width;
  };
  const mono = width('600 40px monospace');
  const display = width('600 40px "Segoe UI Variable Display", monospace');
  const text = width('400 40px "Segoe UI Variable Text", monospace');
  if (Math.abs(display - mono) < 0.5 || Math.abs(text - mono) < 0.5) {
    cancelRender(new Error('Segoe UI Variable Display/Text is not installed'));
  }
};

const lerp = (a: number, b: number, t: number) => a + (b - a) * t;
const lerpRect = (a: Rect, b: Rect, t: number): Rect => ({
  x: lerp(a.x, b.x, t),
  y: lerp(a.y, b.y, t),
  w: lerp(a.w, b.w, t),
  h: lerp(a.h, b.h, t),
});

const Glyph: React.FC<{d: string; size: number; color: string}> = ({d, size, color}) => (
  <svg width={size} height={size} viewBox="0 0 256 256" style={{display: 'block', flex: 'none'}}>
    <path d={d} fill={color} />
  </svg>
);

// ------------------------------------------------------------------ a look's clip
const Clip: React.FC<{row: number; f: number; anchor?: number}> = ({row, f, anchor = 50}) => (
  <Freeze frame={clipFrame(row, f)}>
    <OffthreadVideo
      src={staticFile(`clips/${LOOKS[row].slug}.mp4`)}
      muted
      style={{
        width: '100%',
        height: '100%',
        objectFit: 'cover',
        objectPosition: `50% ${anchor}%`,
        display: 'block',
      }}
    />
  </Freeze>
);

// ------------------------------------------------------------------ the list
const CAT_SIZE = 18 * UI;

const Row: React.FC<{row: number; rel: number; f: number; pill: number}> = ({row, rel, f, pill}) => {
  const look = LOOKS[row];
  const selected = pill > 0;
  const inner = ROW_H - 2 * HAIR;
  return (
    <div
      style={{
        position: 'absolute',
        left: LIST_X,
        top: rowTop(rel),
        width: ROW_W,
        height: ROW_H,
        boxSizing: 'border-box',
        borderRadius: R_CARD,
        background: CARD,
        border: `${HAIR}px solid ${CARD_LINE}`,
        overflow: 'hidden',
      }}
    >
      {selected ? <div style={{position: 'absolute', inset: 0, background: SELECTED}} /> : null}
      {selected ? (
        // The app's nav marker (SettingsWindow NavItem): a 3x16 coral pill on the left edge.
        <div
          style={{
            position: 'absolute',
            left: 0,
            top: inner / 2 - (16 * UI * pill) / 2,
            width: 3 * UI,
            height: 16 * UI * pill,
            borderRadius: 1.5 * UI,
            background: ACCENT,
          }}
        />
      ) : null}
      <div
        style={{
          position: 'absolute',
          left: STRIP_X - HAIR,
          top: STRIP_Y - HAIR,
          width: STRIP_W,
          height: STRIP_H,
          borderRadius: R_STRIP,
          overflow: 'hidden',
          background: SCREEN,
        }}
      >
        <Clip row={row} f={f} />
      </div>
      <div
        style={{
          position: 'absolute',
          left: TEXT_X - HAIR,
          right: 40 + CAT_SIZE,
          top: 0,
          height: inner,
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'center',
          gap: 1,
        }}
      >
        <div
          style={{
            fontFamily: DISPLAY,
            fontWeight: 600,
            fontSize: 26,
            lineHeight: '32px',
            color: TEXT,
            whiteSpace: 'nowrap',
          }}
        >
          {look.name}
        </div>
        <div style={{fontFamily: BODY, fontSize: 20, lineHeight: '25px', color: TEXT_2, whiteSpace: 'nowrap'}}>
          {look.author}
        </div>
      </div>
      <div style={{position: 'absolute', right: 20, top: (inner - CAT_SIZE) / 2}}>
        <Glyph d={CATEGORY_GLYPH[look.category]} size={CAT_SIZE} color={TEXT_3} />
      </div>
    </div>
  );
};

const List: React.FC<{f: number}> = ({f}) => {
  const s = scrollAt(f);
  const sel = selectionAt(f);
  const rows = Array.from({length: COUNT}, (_, row) => ({row, rel: relOf(row, s)})).filter(({rel}) =>
    rowVisible(rel),
  );
  return (
    <>
      {rows.map(({row, rel}) => (
        <Row key={LOOKS[row].slug} row={row} rel={rel} f={f} pill={row === sel.row ? sel.pill : 0} />
      ))}
    </>
  );
};

// ------------------------------------------------------------------ the panel
// Laid out like the app's setting card: the category glyph in a soft tile on the
// left, the look's name and its author on the right.
const PAD = 40;
const TILE = 64 * UI; // 96
const BLOCK_TOP = 40; // panel-relative clip the block moves in
const BLOCK_H = PANEL_H - 2 * BLOCK_TOP; // 128

const Details: React.FC<{look: Look; y: number}> = ({look, y}) => (
  <div
    style={{
      position: 'absolute',
      left: 0,
      right: 0,
      top: 0,
      height: BLOCK_H,
      translate: `0 ${y}px`,
      display: 'flex',
      alignItems: 'center',
      gap: 28,
    }}
  >
    <div
      style={{
        width: TILE,
        height: TILE,
        boxSizing: 'border-box',
        borderRadius: R_TILE,
        background: SOFT,
        border: `${HAIR}px solid ${SOFT_LINE}`,
        display: 'grid',
        placeItems: 'center',
        flex: 'none',
      }}
    >
      <Glyph d={CATEGORY_GLYPH[look.category]} size={32 * UI} color={TEXT} />
    </div>
    <div style={{minWidth: 0}}>
      <div
        style={{
          fontFamily: DISPLAY,
          fontWeight: 600,
          fontSize: 56,
          lineHeight: '66px',
          letterSpacing: '-0.01em',
          color: TEXT,
          whiteSpace: 'nowrap',
        }}
      >
        {look.name}
      </div>
      <div style={{fontFamily: BODY, fontSize: 22, lineHeight: '30px', color: TEXT_2, whiteSpace: 'nowrap'}}>
        {look.author}
      </div>
    </div>
  </div>
);

const Panel: React.FC<{band: Band}> = ({band}) => (
  <div
    style={{
      position: 'absolute',
      left: PANEL_X,
      top: PANEL_Y,
      width: PANEL_W,
      height: PANEL_H,
      boxSizing: 'border-box',
      borderRadius: R_PANEL,
      background: PANEL_BG,
      border: `${HAIR}px solid ${PANEL_EDGE}`,
      overflow: 'hidden',
    }}
  >
    <div
      style={{
        position: 'absolute',
        inset: 0,
        background: `linear-gradient(180deg, ${WASH} 0%, rgba(255,107,107,0) 70%)`,
      }}
    />
    {/* As the taskbar puts the next look on, this block is pushed up and out
        and the next one comes in under it, the same move as the list. */}
    <div
      style={{
        position: 'absolute',
        left: PAD - HAIR,
        right: PAD - HAIR,
        top: BLOCK_TOP - HAIR,
        height: BLOCK_H,
        overflow: 'hidden',
      }}
    >
      <Details look={LOOKS[band.cur]} y={-band.roll * BLOCK_H} />
      {band.roll > 0 ? <Details look={LOOKS[band.next]} y={(1 - band.roll) * BLOCK_H} /> : null}
    </div>
  </div>
);

// ------------------------------------------------------------------ the taskbar
// Phosphor, as used by the site's taskbar (web/server/templates/_icons.html).
const WINDOWS_LOGO =
  'M104,144v51.64a8,8,0,0,1-8,8,8.54,8.54,0,0,1-1.43-.13l-64-11.64A8,8,0,0,1,24,184V144a8,8,0,0,1,8-8H96A8,8,0,0,1,104,144Zm-2.87-89.78a8,8,0,0,0-6.56-1.73l-64,11.64A8,8,0,0,0,24,72v40a8,8,0,0,0,8,8H96a8,8,0,0,0,8-8V60.36A8,8,0,0,0,101.13,54.22ZM208,136H128a8,8,0,0,0-8,8v57.45a8,8,0,0,0,6.57,7.88l80,14.54A7.61,7.61,0,0,0,208,224a8,8,0,0,0,8-8V144A8,8,0,0,0,208,136Zm5.13-102.14a8,8,0,0,0-6.56-1.73l-80,14.55A8,8,0,0,0,120,54.55V112a8,8,0,0,0,8,8h80a8,8,0,0,0,8-8V40A8,8,0,0,0,213.13,33.86Z';
const MAGNIFIER =
  'M229.66,218.34l-50.07-50.06a88.11,88.11,0,1,0-11.31,11.31l50.06,50.07a8,8,0,0,0,11.32-11.32ZM40,112a72,72,0,1,1,72,72A72.08,72.08,0,0,1,40,112Z';

// Windows 11 with search set to "icon only": Start, Search, and MusicBar open.
const TaskbarIcons: React.FC = () => {
  const BTN = 40 * UI;
  const ICON = 24 * UI;
  const GAP = 4 * UI;
  const total = 3 * BTN + 2 * GAP;
  const button: React.CSSProperties = {
    position: 'relative',
    width: BTN,
    height: BTN,
    borderRadius: R_BUTTON,
    display: 'grid',
    placeItems: 'center',
    flex: 'none',
  };
  return (
    <div
      style={{
        position: 'absolute',
        left: (W - total) / 2,
        top: TB_TOP + (TB_H - BTN) / 2,
        height: BTN,
        display: 'flex',
        alignItems: 'center',
        gap: GAP,
      }}
    >
      <div style={button}>
        <Glyph d={WINDOWS_LOGO} size={ICON} color={START_BLUE} />
      </div>
      <div style={button}>
        <Glyph d={MAGNIFIER} size={20 * UI} color={TEXT} />
      </div>
      {/* MusicBar, open and focused: its window is the list above. */}
      <div style={{...button, background: 'rgba(255,255,255,.06)'}}>
        <Img src={staticFile('musicbar-icon.png')} style={{width: ICON, height: ICON, display: 'block'}} />
        <div
          style={{
            position: 'absolute',
            left: (BTN - 16 * UI) / 2,
            bottom: 1 * UI,
            width: 16 * UI,
            height: 3 * UI,
            borderRadius: 2 * UI,
            background: ACCENT,
          }}
        />
      </div>
    </div>
  );
};

const Taskbar: React.FC<{f: number; band: Band}> = ({f, band}) => {
  const fly = lerpRect(SEL_STRIP, BAND, band.flight);
  return (
    <>
      <div
        style={{
          position: 'absolute',
          left: BAND.x,
          top: BAND.y,
          width: BAND.w,
          height: BAND.h,
          overflow: 'hidden',
          background: SCREEN,
        }}
      >
        <Clip row={band.cur} f={f} anchor={LOOKS[band.cur].anchor} />
      </div>
      {band.flight > 0 ? (
        // The selected row's strip, carried down and opened out to the taskbar's width.
        <div
          style={{
            position: 'absolute',
            left: fly.x,
            top: fly.y,
            width: fly.w,
            height: fly.h,
            borderRadius: lerp(R_STRIP, 0, band.flight),
            overflow: 'hidden',
            background: SCREEN,
          }}
        >
          <Clip row={band.next} f={f} anchor={LOOKS[band.next].anchor} />
        </div>
      ) : null}
      <div style={{position: 'absolute', left: 0, top: TB_TOP, width: W, height: HAIR, background: WIN_LINE}} />
      <TaskbarIcons />
    </>
  );
};

// ------------------------------------------------------------------ film
export const Reel: React.FC = () => {
  checkFonts();
  const f = useCurrentFrame();
  const band = bandAt(f);
  return (
    <AbsoluteFill style={{background: GROUND, overflow: 'hidden'}}>
      <List f={f} />
      <Panel band={band} />
      <Taskbar f={f} band={band} />
    </AbsoluteFill>
  );
};
