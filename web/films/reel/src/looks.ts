import manifest from './manifest.json';

export type Category = 'visualizer' | 'progress' | 'led';

type ManifestEntry = {
  slug: string;
  type: string;
  category: Category;
  name: string;
};

export type Look = {
  slug: string;
  name: string;
  category: Category;
  author: string;
  /** Vertical anchor (0 top, 100 bottom) when the strip is cropped to the taskbar's shape. */
  anchor: number;
};

// List order. Every third row (0, 3, 6 ... 21) is the one selected on a bar
// and put on the taskbar; the rows between them are only scrolled past.
// Neighbours differ in colour so the column never shows a run of one hue.
const ORDER = [
  'vapor-sunset', // put on, bar 1
  'lime-blur',
  'bubblegum-breathe',
  'northern-curtains', // bar 2
  'cinema-crimson',
  'lightstick-ocean',
  'pixel-rainbow-run', // bar 3
  'matcha-latte',
  'siren-sweep',
  'lava-lamp', // bar 4
  'y2k-chrome',
  'pumpkin-ember',
  'cyber-lime-chase', // bar 5
  'disco-fever',
  'code-rain',
  'heartbeat', // bar 6
  'lime-summer',
  'galaxy-mind',
  'gamer-rgb', // bar 7
  'vapor-horizon',
  'phosphor-vu',
  'golden-hour-silk', // bar 8
  'lavender-lightsticks',
  'rainy-window',
];

// Where the content of a strip sits, for the crop on the taskbar.
const ANCHOR: Record<string, number> = {
  'vapor-sunset': 0,
  'northern-curtains': 100,
  'lava-lamp': 100,
  heartbeat: 60,
  'lime-blur': 100,
  'lime-summer': 100,
};

// Every seed look in the gallery is published by MusicBar (web/server/cli.py seeds them with
// author="MusicBar"), so that is the true author for all 24.
const AUTHOR = 'MusicBar';

const bySlug = new Map((manifest as ManifestEntry[]).map((m) => [m.slug, m]));

export const LOOKS: Look[] = ORDER.map((slug) => {
  const m = bySlug.get(slug);
  if (!m) {
    throw new Error(`Missing manifest entry for ${slug}`);
  }
  return {
    slug,
    name: m.name,
    category: m.category,
    author: AUTHOR,
    anchor: ANCHOR[slug] ?? (m.category === 'led' ? 100 : 50),
  };
});

export const COUNT = LOOKS.length; // 24

if (new Set(ORDER).size !== COUNT || COUNT !== (manifest as ManifestEntry[]).length) {
  throw new Error('The list must hold every gallery look exactly once');
}

// ------------------------------------------------------------------ category glyphs
// The site is bilingual, so the film carries no words: only proper nouns (look
// names, the author) and the category as a Phosphor glyph, the same ones the
// site uses (web/server/templates/_icons.html: waveform, sliders-horizontal, lightbulb).
export const CATEGORY_GLYPH: Record<Category, string> = {
  visualizer:
    'M56,96v64a8,8,0,0,1-16,0V96a8,8,0,0,1,16,0ZM88,24a8,8,0,0,0-8,8V224a8,8,0,0,0,16,0V32A8,8,0,0,0,88,24Zm40,32a8,8,0,0,0-8,8V192a8,8,0,0,0,16,0V64A8,8,0,0,0,128,56Zm40,32a8,8,0,0,0-8,8v64a8,8,0,0,0,16,0V96A8,8,0,0,0,168,88Zm40-16a8,8,0,0,0-8,8v96a8,8,0,0,0,16,0V80A8,8,0,0,0,208,72Z',
  progress:
    'M40,88H73a32,32,0,0,0,62,0h81a8,8,0,0,0,0-16H135a32,32,0,0,0-62,0H40a8,8,0,0,0,0,16Zm64-24A16,16,0,1,1,88,80,16,16,0,0,1,104,64ZM216,168H199a32,32,0,0,0-62,0H40a8,8,0,0,0,0,16h97a32,32,0,0,0,62,0h17a8,8,0,0,0,0-16Zm-48,24a16,16,0,1,1,16-16A16,16,0,0,1,168,192Z',
  led: 'M176,232a8,8,0,0,1-8,8H88a8,8,0,0,1,0-16h80A8,8,0,0,1,176,232Zm40-128a87.55,87.55,0,0,1-33.64,69.21A16.24,16.24,0,0,0,176,186v6a16,16,0,0,1-16,16H96a16,16,0,0,1-16-16v-6a16,16,0,0,0-6.23-12.66A87.59,87.59,0,0,1,40,104.49C39.74,56.83,78.26,17.14,125.88,16A88,88,0,0,1,216,104Zm-16,0a72,72,0,0,0-73.74-72c-39,.92-70.47,33.39-70.26,72.39a71.65,71.65,0,0,0,27.64,56.3A32,32,0,0,1,96,186v6h64v-6a32.15,32.15,0,0,1,12.47-25.35A71.65,71.65,0,0,0,200,104Zm-16.11-9.34a57.6,57.6,0,0,0-46.56-46.55,8,8,0,0,0-2.66,15.78c16.57,2.79,30.63,16.85,33.44,33.45A8,8,0,0,0,176,104a9,9,0,0,0,1.35-.11A8,8,0,0,0,183.89,94.66Z',
};
