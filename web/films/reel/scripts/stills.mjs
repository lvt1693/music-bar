// Headless stills for checking the film: bundles once, renders the given frames.
// node scripts/stills.mjs 0 30 52 61 [--out=out/stills]
import path from 'node:path';
import fs from 'node:fs';
import {bundle} from '@remotion/bundler';
import {renderStill, selectComposition} from '@remotion/renderer';

const args = process.argv.slice(2);
const out = (args.find((a) => a.startsWith('--out=')) ?? '--out=out/stills').slice(6);
const frames = args.filter((a) => !a.startsWith('--')).map(Number);

fs.mkdirSync(out, {recursive: true});
const serveUrl = await bundle({entryPoint: path.resolve('src/index.ts')});
const composition = await selectComposition({serveUrl, id: 'Reel'});
for (const frame of frames) {
  const output = path.join(out, `f${String(frame).padStart(3, '0')}.png`);
  await renderStill({composition, serveUrl, frame, output, imageFormat: 'png'});
  console.log(output);
}
