// Prints the timing facts the loop depends on. Run: npm run check
import {LOOKS} from '../src/looks';
import {BEAT, BEATS, CLIP_FRAMES, DURATION, FPS, WINDOWS, bandAt, clipFrame, longestWindow, scrollAt, selectionAt} from '../src/timeline';

console.log(`${DURATION} frames at ${FPS} fps = ${(DURATION / FPS).toFixed(3)} s, ${BEATS} beats of ${BEAT} frames = ${((60 * FPS) / BEAT).toFixed(2)} BPM`);
console.log(`scroll at 0: ${scrollAt(0)}, at ${DURATION - 1}: ${scrollAt(DURATION - 1)}`);
console.log(`selected at 0: ${LOOKS[selectionAt(0).row].slug} (pill ${selectionAt(0).pill}), band at 0: ${LOOKS[bandAt(0).cur].slug}`);

let bad = 0;
WINDOWS.forEach((w, row) => {
  // Within its window a clip must advance one frame per film frame, never jumping back.
  let jumps = 0;
  for (let t = 1; t < w.length; t++) {
    const a = clipFrame(row, w.start + t - 1);
    const b = clipFrame(row, w.start + t);
    if (b !== a + 1) {
      jumps++;
    }
  }
  if (jumps || w.length > CLIP_FRAMES) {
    bad++;
  }
  console.log(
    `${LOOKS[row].slug.padEnd(22)} on screen ${String(w.length).padStart(3)} frames from ${String(w.start).padStart(3)}, clip ${clipFrame(row, w.start)}..${clipFrame(row, w.start + w.length - 1)}${jumps ? `  JUMPS ${jumps}` : ''}`,
  );
});
console.log(`longest window ${longestWindow} of ${CLIP_FRAMES}; ${bad ? `${bad} clips wrap on screen` : 'no clip wraps on screen'}`);
const shown = new Set<number>();
for (let f = 0; f < DURATION; f++) {
  shown.add(bandAt(f).cur);
}
console.log(`on the taskbar: ${[...shown].map((r) => LOOKS[r].slug).join(', ')}`);
