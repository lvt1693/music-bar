// Turns out/reel-master.mov (npm run render) into the site's files:
// web/server/static/media/reel.mp4 (H.264), reel.webm (VP9), reel-poster.jpg (frame 0).
// Needs ffmpeg/ffprobe on PATH. Usage: node scripts/encode.mjs [--crf=24] [--vp9crf=36]
import {execFileSync} from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';

const arg = (name, fallback) => {
  const hit = process.argv.find((a) => a.startsWith(`--${name}=`));
  return hit ? hit.slice(name.length + 3) : fallback;
};

const MASTER = path.resolve('out/reel-master.mov');
const MEDIA = path.resolve('../../server/static/media');
const MP4 = path.join(MEDIA, 'reel.mp4');
const WEBM = path.join(MEDIA, 'reel.webm');
const POSTER = path.join(MEDIA, 'reel-poster.jpg');
const LIMIT = 5 * 1024 * 1024;
const POSTER_LIMIT = 200 * 1024;

const run = (args) => execFileSync('ffmpeg', ['-hide_banner', '-loglevel', 'error', '-y', ...args], {stdio: 'inherit'});
const kb = (file) => `${(fs.statSync(file).size / 1024).toFixed(0)} KB`;

if (!fs.existsSync(MASTER)) {
  throw new Error(`No master at ${MASTER}; run npm run render first`);
}
fs.mkdirSync(MEDIA, {recursive: true});

run([
  '-i', MASTER, '-an',
  '-c:v', 'libx264', '-preset', 'veryslow', '-crf', arg('crf', '24'), '-tune', 'animation',
  '-pix_fmt', 'yuv420p', '-profile:v', 'high', '-level', '4.1',
  '-movflags', '+faststart',
  MP4,
]);

const passlog = path.resolve('out/vp9pass');
const vp9 = ['-c:v', 'libvpx-vp9', '-b:v', '0', '-crf', arg('vp9crf', '36'), '-row-mt', '1', '-tile-columns', '2', '-deadline', 'good', '-cpu-used', '1', '-pix_fmt', 'yuv420p', '-passlogfile', passlog];
run(['-i', MASTER, '-an', ...vp9, '-pass', '1', '-f', 'webm', process.platform === 'win32' ? 'NUL' : '/dev/null']);
run(['-i', MASTER, '-an', ...vp9, '-pass', '2', WEBM]);

run(['-i', MASTER, '-frames:v', '1', '-q:v', arg('jpgq', '3'), POSTER]);

for (const [file, limit] of [[MP4, LIMIT], [WEBM, LIMIT], [POSTER, POSTER_LIMIT]]) {
  const size = fs.statSync(file).size;
  console.log(`${path.basename(file).padEnd(16)} ${kb(file).padStart(8)}${size > limit ? '  OVER THE LIMIT' : ''}`);
}
