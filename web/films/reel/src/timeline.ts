import {Easing, interpolate} from 'remotion';
import {COUNT} from './looks';

// ------------------------------------------------------------------ tempo
// 32 beats in 488 frames: 15.25 frames a beat at 30 fps = 118.03 BPM.
// 8 bars of 4 beats. Per bar: the list scrolls one row on beats 1, 2 and 3
// (3 x 8 = 24 rows, a whole turn of the list, so the loop is seamless), the
// row that lands in the middle slot on beat 3 is selected, and its look lands
// on the taskbar on the next downbeat.
export const FPS = 30;
export const BEATS = 32;
export const DURATION = 488;
export const BEAT = DURATION / BEATS;
export const BARS = BEATS / 4;
export const bf = (beat: number) => Math.round(beat * BEAT);
export const mod = (a: number, n: number) => ((a % n) + n) % n;

const STEP_DUR = 9; // a wheel notch
const FLIGHT_DUR = 13; // row strip to taskbar
const PILL_DUR = 5;
const ROLL_DUR = 10;

const STEP_EASE = Easing.bezier(0.65, 0, 0.25, 1);
const FLIGHT_EASE = Easing.bezier(0.62, 0, 0.18, 1);
const OUT_EASE = Easing.bezier(0.2, 0, 0, 1);

/** 0 -> 1, landing exactly on `end`. */
const ramp = (f: number, end: number, dur: number, easing: (t: number) => number) =>
  interpolate(f, [end - dur, end], [0, 1], {
    extrapolateLeft: 'clamp',
    extrapolateRight: 'clamp',
    easing,
  });

const ROWS_PER_BAR = COUNT / BARS; // 3
if (!Number.isInteger(ROWS_PER_BAR)) {
  throw new Error('Rows must divide evenly over the bars for a seamless loop');
}

const STEP_FRAMES: number[] = [];
for (let k = 0; k < BARS; k++) {
  for (let b = 1; b <= ROWS_PER_BAR; b++) {
    STEP_FRAMES.push(bf(4 * k + b));
  }
}

/** Rows scrolled so far (0..24, 24 = 0 again). */
export const scrollAt = (f: number) => STEP_FRAMES.reduce((a, end) => a + ramp(f, end, STEP_DUR, STEP_EASE), 0);

type Event = {frame: number; row: number};

// Selected when the third notch of a bar lands (beat 4k + 3).
const SELECT: Event[] = Array.from({length: BARS}, (_, k) => ({
  frame: bf(4 * k + 3),
  row: mod(ROWS_PER_BAR * (k + 1), COUNT),
}));

// The selected look lands on the taskbar on the next downbeat (beat 4m).
const LAND: Event[] = Array.from({length: BARS}, (_, m) => ({
  frame: bf(4 * m),
  row: mod(ROWS_PER_BAR * m, COUNT),
}));

/** Latest event at or before f, wrapping round the loop; `since` in frames. */
const latest = (events: Event[], f: number) => {
  let idx = events.length - 1;
  for (let i = 0; i < events.length; i++) {
    if (events[i].frame <= f) {
      idx = i;
    }
  }
  return {idx, since: mod(f - events[idx].frame, DURATION)};
};

export type Selection = {row: number; pill: number};

export const selectionAt = (f: number): Selection => {
  const {idx, since} = latest(SELECT, f);
  return {
    row: SELECT[idx].row,
    pill: interpolate(since, [0, PILL_DUR], [0, 1], {
      extrapolateLeft: 'clamp',
      extrapolateRight: 'clamp',
      easing: OUT_EASE,
    }),
  };
};

export type Band = {
  cur: number; // look on the taskbar
  next: number; // look on its way there
  flight: number; // 0..1 eased progress of the flying strip
  roll: number; // 0..1 the panel's text turning over to `next`
};

export const bandAt = (f: number): Band => {
  const {idx} = latest(LAND, f);
  const nextIdx = (idx + 1) % LAND.length;
  const nextFrame = nextIdx === 0 ? DURATION : LAND[nextIdx].frame;
  return {
    cur: LAND[idx].row,
    next: LAND[nextIdx].row,
    flight: ramp(f, nextFrame, FLIGHT_DUR, FLIGHT_EASE),
    roll: ramp(f, nextFrame, ROLL_DUR, STEP_EASE),
  };
};

// ------------------------------------------------------------------ layout
// The UI is drawn at 1.5x (radius 7 -> 10.5, 14 -> 21, pill 3x16 -> 4.5x24)
// so that, shown at the gallery column's width, it matches the site's own sizes.
export const W = 1920;
export const H = 480;
export const UI = 1.5;

export const TB_H = 168;
export const TB_TOP = H - TB_H; // 312

export const LIST_X = 40;
export const ROW_W = 1080;
export const ROW_H = 72;
export const PITCH = 82;
export const SEL_Y = 120; // top of the selected slot, centred over the taskbar
export const STRIP_X = 20;
export const STRIP_Y = 8;
export const STRIP_H = ROW_H - 2 * STRIP_Y; // 56
export const STRIP_W = Math.round((STRIP_H * 760) / 84); // 507, the clips' own shape
export const TEXT_X = STRIP_X + STRIP_W + 28;

export const PANEL_X = LIST_X + ROW_W + 60; // 1180
export const PANEL_W = W - 40 - PANEL_X; // 700
export const PANEL_H = 208;
export const PANEL_Y = (TB_TOP - PANEL_H) / 2; // 52, level with the selected row

/** Row position relative to the selected slot, wrapped to -12..12. */
export const relOf = (row: number, s: number) => {
  let d = mod(row - s, COUNT);
  if (d >= COUNT / 2) {
    d -= COUNT;
  }
  return d;
};

export const rowTop = (rel: number) => SEL_Y + rel * PITCH;

/** Any part of the row shows between the top of the frame and the taskbar. */
export const rowVisible = (rel: number) => {
  const y = rowTop(rel);
  return y < TB_TOP && y + ROW_H > 0;
};

export type Rect = {x: number; y: number; w: number; h: number};

export const SEL_STRIP: Rect = {x: LIST_X + STRIP_X, y: SEL_Y + STRIP_Y, w: STRIP_W, h: STRIP_H};
export const BAND: Rect = {x: 0, y: TB_TOP, w: W, h: TB_H};

// ------------------------------------------------------------------ clip time
// The previews are 6 s (180 frames) and most do not loop cleanly. Each look is
// on screen for one stretch of the film (scrolling past, or scrolling past and
// then sitting on the taskbar), always shorter than 180 frames, so each clip
// runs at its own speed from inside that stretch and is never seen to wrap.
// The stretch is measured on the loop's circle, so frame 487 runs into frame 0.
export const CLIP_FRAMES = 180;

const visibleAt = (row: number, f: number) => {
  if (rowVisible(relOf(row, scrollAt(f)))) {
    return true;
  }
  const b = bandAt(f);
  return b.cur === row || (b.flight > 0 && b.next === row);
};

type Window = {start: number; length: number};

export const WINDOWS: Window[] = Array.from({length: COUNT}, (_, row) => {
  const seen: boolean[] = [];
  for (let f = 0; f < DURATION; f++) {
    seen.push(visibleAt(row, f));
  }
  // Longest run of unseen frames, going round the circle.
  let bestLen = 0;
  let bestEnd = 0;
  let run = 0;
  for (let n = 0; n < 2 * DURATION; n++) {
    if (!seen[n % DURATION]) {
      run++;
      if (run > bestLen) {
        bestLen = Math.min(run, DURATION);
        bestEnd = n % DURATION;
      }
    } else {
      run = 0;
    }
  }
  return {start: mod(bestEnd + 1, DURATION), length: DURATION - bestLen};
});

export const longestWindow = Math.max(...WINDOWS.map((w) => w.length));

/** Source frame of a look's clip at film frame f. */
export const clipFrame = (row: number, f: number) => {
  const {start, length} = WINDOWS[row];
  const t = mod(f - start, DURATION);
  if (length <= CLIP_FRAMES) {
    const offset = Math.floor((CLIP_FRAMES - length) / 2);
    return mod(offset + t, CLIP_FRAMES);
  }
  // Not expected with this timing; slow the clip down rather than wrap it.
  return Math.min(CLIP_FRAMES - 1, Math.floor((t * (CLIP_FRAMES - 1)) / (length - 1)));
};
