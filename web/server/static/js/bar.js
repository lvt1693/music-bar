/* MusicBar's bar, redrawn for the web: a few of the app's styles on a 2D canvas, driven by a made-up
   song (118 BPM, a kick on every beat, hats in between) so the page dances without any audio. */
(function () {
  "use strict";

  const PALETTES = {
    neon: ["#FF2BD6", "#00F0FF", "#8A2BFF"],
    sunset: ["#FF5F6D", "#FFC371", "#FF8C42"],
    ocean: ["#2E8BFF", "#00D4C7", "#7A6CFF"],
    lime: ["#8ACE00", "#F2FFCC", "#4E7A00"],
    aurora: ["#4CFFB0", "#47A9FF", "#B26CFF"],
  };

  const rgb = (hex) => {
    const n = parseInt(hex.slice(1), 16);
    return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
  };
  const mix = (a, b, k) => [a[0] + (b[0] - a[0]) * k, a[1] + (b[1] - a[1]) * k, a[2] + (b[2] - a[2]) * k];
  const css = (c, a) => `rgba(${c[0] | 0},${c[1] | 0},${c[2] | 0},${a === undefined ? 1 : a})`;
  const clamp = (v) => (v < 0 ? 0 : v > 1 ? 1 : v);

  /** The made-up song at time t (seconds). */
  function music(t) {
    const period = 60 / 118;
    const phase = (t / period) % 1;
    const kick = Math.exp(-phase * 7);
    const hat = Math.exp(-((phase + 0.5) % 1) * 14) * 0.45;
    const swell = 0.5 + 0.5 * Math.sin(t * 0.37);
    const level = clamp(0.38 + 0.36 * kick + 0.12 * swell + 0.06 * Math.sin(t * 5.3));
    return {
      level, beat: kick, hat,
      spectrum(x) {
        const bass = Math.pow(1 - x, 1.4) * (0.5 + 0.5 * kick);
        const mids = Math.exp(-Math.pow((x - 0.35) / 0.22, 2)) * (0.32 + 0.22 * swell);
        const highs = Math.pow(x, 0.9) * hat * 0.9;
        const shimmer = 0.14 * Math.sin(x * 23 + t * 3.1) * Math.sin(x * 7.3 - t * 1.7) + 0.06 * Math.sin(x * 61 + t * 9);
        return clamp((bass + mids + highs + shimmer) * (0.75 + 0.35 * level));
      },
      wave(x) {
        return 0.55 * level * Math.sin(Math.PI * 2 * (x * 3 + t * 0.9)) + 0.22 * Math.sin(Math.PI * 2 * (x * 11 - t * 2.3))
          + 0.3 * kick * Math.sin(Math.PI * 2 * x * 1.5 + t);
      },
    };
  }

  /** A color along the palette, cycling A → B → C → A. */
  function along(pal, u) {
    u = ((u % 1) + 1) % 1;
    const i = Math.floor(u * 3);
    const k = u * 3 - i;
    return mix(pal[i], pal[(i + 1) % 3], k * k * (3 - 2 * k));
  }

  function draw(ctx, w, h, style, colors, t, progress, opts) {
    opts = opts || {};
    const pal = (colors && colors.length === 3 ? colors : PALETTES.neon).map(rgb);
    const m = music(t);
    const dpr = opts.dpr || 1;
    ctx.clearRect(0, 0, w, h);
    ctx.globalCompositeOperation = "source-over";
    const s = STYLE_ALIAS[style] || style;
    (DRAW[s] || DRAW.spectrum)(ctx, w, h, pal, m, t, progress, dpr, s);
    ctx.globalCompositeOperation = "source-over";
  }

  const DRAW = {
    spectrum(ctx, w, h, pal, m, t, progress, dpr) {
      const col = Math.max(3, Math.round(4 * dpr)), gap = Math.max(1, Math.round(2 * dpr));
      const n = Math.floor(w / (col + gap));
      const grad = ctx.createLinearGradient(0, h, 0, 0);
      grad.addColorStop(0, css(pal[1], 0.95));
      grad.addColorStop(0.6, css(pal[0], 0.95));
      grad.addColorStop(1, css(pal[2], 1));
      for (let i = 0; i < n; i++) {
        const x = (i + 0.5) / n;
        const v = m.spectrum(Math.abs(x - 0.5) * 2);
        const bh = Math.max(dpr, v * h * 0.94);
        ctx.globalAlpha = x < progress ? 1 : 0.42;
        ctx.fillStyle = grad;
        ctx.fillRect(i * (col + gap), h - bh, col, bh);
      }
      ctx.globalAlpha = 1;
      glowEdge(ctx, w, h, pal, m, dpr);
    },

    led(ctx, w, h, pal, m, t, progress, dpr, kind) {
      const n = 48;
      const band = ctx.createLinearGradient(0, 0, w, 0);
      for (let i = 0; i <= n; i++) {
        const x = i / n;
        let c, b;
        const d = Math.abs(x - 0.5) * 2;
        switch (kind) {
          case "ledbreathe": c = along(pal, t * 0.05); b = 0.35 + 0.6 * (0.5 + 0.5 * Math.sin(t * 2.4)) * (0.6 + 0.4 * m.level); break;
          case "ledscanner": { const head = 0.5 + 0.48 * Math.sin(t * 2.1); c = along(pal, x < head ? 0 : 0.34); b = Math.exp(-Math.pow((x - head) / 0.07, 2)) + 0.08; break; }
          case "ledfire": c = mix(pal[0], pal[1], clamp(0.5 + 0.5 * Math.sin(x * 40 + t * 7) * Math.sin(x * 13 - t * 3))); b = 0.45 + 0.55 * m.spectrum(d) * (0.7 + 0.3 * Math.sin(x * 90 + t * 20)); break;
          case "ledchase": c = along(pal, Math.floor((x * 12 - t * 2.2) % 3) / 3); b = (Math.sin((x * 60 - t * 9)) > 0.2 ? 0.95 : 0.12) * (0.6 + 0.4 * m.level); break;
          case "ledstarlight": c = along(pal, x * 0.7 + t * 0.04); b = 0.12 + 0.88 * Math.pow(clamp(Math.sin(x * 97 + t * (1.3 + (i % 7) * 0.4))), 6); break;
          case "ledvu": c = x < 0.6 ? pal[1] : x < 0.85 ? pal[0] : pal[2]; b = x < m.level * 0.95 + 0.05 ? 0.95 : 0.1; break;
          case "ledmeteor": { const head = (t * 0.35) % 1.3; const dx = head - x; c = along(pal, x); b = dx > 0 ? Math.exp(-dx * 9) : 0.05; break; }
          default: c = along(pal, x * 1.15 - t * 0.12); b = 0.3 + 0.7 * m.spectrum(d) * (1 + 0.35 * m.beat);
        }
        band.addColorStop(x, css(c, clamp(b)));
      }
      const top = h * 0.18;
      ctx.fillStyle = band;
      ctx.globalAlpha = 0.9;
      ctx.fillRect(0, top, w, h - top);
      ctx.globalCompositeOperation = "destination-in";
      const fade = ctx.createLinearGradient(0, h, 0, top);
      fade.addColorStop(0, "rgba(0,0,0,1)");
      fade.addColorStop(0.35, "rgba(0,0,0,0.55)");
      fade.addColorStop(1, "rgba(0,0,0,0)");
      ctx.fillStyle = fade;
      ctx.fillRect(0, 0, w, h);
      ctx.globalCompositeOperation = "lighter";
      ctx.globalAlpha = 1;
      ctx.fillStyle = band;
      ctx.fillRect(0, h - Math.max(2, 2.5 * dpr), w, Math.max(2, 2.5 * dpr));
    },

    aurora(ctx, w, h, pal, m, t, progress) {
      ctx.globalCompositeOperation = "lighter";
      for (let k = 0; k < 3; k++) {
        const c = pal[k];
        ctx.beginPath();
        ctx.moveTo(0, h);
        const steps = 64;
        for (let i = 0; i <= steps; i++) {
          const x = i / steps;
          const y = h * (1 - (0.25 + 0.45 * m.spectrum(Math.abs(x - 0.5) * 2) * (0.8 + 0.2 * Math.sin(t * 0.8 + k))
            + 0.12 * Math.sin(x * (5 + k * 2) + t * (0.6 + k * 0.25))));
          ctx.lineTo(x * w, y);
        }
        ctx.lineTo(w, h);
        ctx.closePath();
        const g = ctx.createLinearGradient(0, h, 0, 0);
        g.addColorStop(0, css(c, 0.55));
        g.addColorStop(1, css(c, 0));
        ctx.fillStyle = g;
        ctx.fill();
      }
      ctx.globalCompositeOperation = "source-over";
      progressLine(ctx, w, h, pal, progress, 0.9);
    },

    scope(ctx, w, h, pal, m, t, progress, dpr) {
      const g = ctx.createLinearGradient(0, 0, w, 0);
      g.addColorStop(0, css(pal[1]));
      g.addColorStop(clamp(progress), css(pal[0]));
      g.addColorStop(1, css(pal[2], 0.6));
      ctx.lineWidth = Math.max(1.5, 2 * dpr);
      ctx.strokeStyle = g;
      ctx.shadowColor = css(pal[0], 0.8);
      ctx.shadowBlur = 10 * dpr;
      ctx.beginPath();
      const steps = Math.min(400, Math.floor(w / 3));
      for (let i = 0; i <= steps; i++) {
        const x = i / steps;
        const env = Math.sin(Math.PI * x);
        const y = h * 0.55 - m.wave(x) * h * 0.36 * env;
        i ? ctx.lineTo(x * w, y) : ctx.moveTo(0, y);
      }
      ctx.stroke();
      ctx.shadowBlur = 0;
    },

    classic(ctx, w, h, pal, m, t, progress, dpr) {
      const pw = w * clamp(progress);
      const g = ctx.createLinearGradient(0, 0, pw || 1, 0);
      g.addColorStop(0, css(pal[1], 0.15));
      g.addColorStop(1, css(pal[0], 0.55 + 0.25 * m.level));
      ctx.fillStyle = g;
      ctx.fillRect(0, 0, pw, h);
      const line = Math.max(2, 3 * dpr);
      ctx.fillStyle = css(pal[2], 0.95);
      ctx.fillRect(0, h - line, pw, line);
      const r = h * (0.8 + 0.4 * m.beat);
      const head = ctx.createRadialGradient(pw, h, 0, pw, h, r);
      head.addColorStop(0, css(pal[2], 0.85));
      head.addColorStop(1, css(pal[2], 0));
      ctx.fillStyle = head;
      ctx.fillRect(pw - r, 0, r * 2, h);
    },

    particles(ctx, w, h, pal, m, t, progress, dpr) {
      ctx.globalCompositeOperation = "lighter";
      for (let i = 0; i < 70; i++) {
        const seed = i * 12.9898;
        const x = ((Math.sin(seed) * 43758.5453) % 1 + 1) % 1;
        const speed = 0.08 + ((i * 7) % 10) / 60;
        const y = 1 - ((t * speed + x * 3) % 1);
        const glow = (0.4 + 0.6 * m.spectrum(Math.abs(x - 0.5) * 2)) * Math.sin(Math.PI * y);
        const r = (1.5 + 2.5 * glow) * dpr;
        const c = along(pal, x + t * 0.02);
        const g = ctx.createRadialGradient(x * w, y * h, 0, x * w, y * h, r * 3);
        g.addColorStop(0, css(c, 0.9 * glow));
        g.addColorStop(1, css(c, 0));
        ctx.fillStyle = g;
        ctx.fillRect(x * w - r * 3, y * h - r * 3, r * 6, r * 6);
      }
      ctx.globalCompositeOperation = "source-over";
      progressLine(ctx, w, h, pal, progress, 0.6);
    },
  };

  /** The app's style names (as in presets) mapped onto what this page can draw. */
  const STYLE_ALIAS = {
    Spectrum: "spectrum", Equalizer: "spectrum", Mosaic: "spectrum", Horizon: "aurora", Aura: "aurora", Aurora: "aurora",
    Liquid: "aurora", Silk: "aurora", Holo: "aurora", Mesh: "aurora", Ink: "aurora", Ribbon: "aurora",
    Oscilloscope: "scope", Waveform: "scope", Classic: "classic", Neon: "classic", Cinema: "classic",
    Fireflies: "particles", Bokeh: "particles",
    Led: "led", LedRainbow: "led", LedSpectrum: "led", LedRipple: "led", LedBreathe: "ledbreathe", LedScanner: "ledscanner",
    LedStarlight: "ledstarlight", LedFire: "ledfire", LedMeteor: "ledmeteor", LedChase: "ledchase", LedVu: "ledvu",
  };
  // The LED variants all draw through DRAW.led, which reads the variant from its last argument.
  for (const v of ["ledbreathe", "ledscanner", "ledstarlight", "ledfire", "ledmeteor", "ledchase", "ledvu"]) {
    DRAW[v] = (ctx, w, h, pal, m, t, progress, dpr) => DRAW.led(ctx, w, h, pal, m, t, progress, dpr, v);
  }

  function glowEdge(ctx, w, h, pal, m, dpr) {
    const g = ctx.createLinearGradient(0, h, 0, h - 10 * dpr);
    g.addColorStop(0, css(pal[0], 0.35 + 0.3 * m.beat));
    g.addColorStop(1, css(pal[0], 0));
    ctx.fillStyle = g;
    ctx.fillRect(0, h - 10 * dpr, w, 10 * dpr);
  }

  function progressLine(ctx, w, h, pal, progress, alpha) {
    ctx.fillStyle = css(pal[2], alpha);
    ctx.fillRect(0, h - 2, w * clamp(progress), 2);
  }

  /** Keeps a canvas drawing while it is on screen: fit to its box at the screen's pixel density. */
  function animate(canvas, getState) {
    const ctx = canvas.getContext("2d");
    let visible = false, raf = 0, w = 0, h = 0, dpr = 1;
    const fit = () => {
      dpr = Math.min(2, window.devicePixelRatio || 1);
      const r = canvas.getBoundingClientRect();
      w = Math.max(1, Math.round(r.width * dpr));
      h = Math.max(1, Math.round(r.height * dpr));
      if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; }
    };
    const frame = (now) => {
      raf = 0;
      if (!visible || document.hidden) return;
      const s = getState(now / 1000);
      draw(ctx, w, h, s.style, s.colors, s.t, s.progress, { dpr });
      raf = requestAnimationFrame(frame);
    };
    const start = () => { if (!raf && visible) raf = requestAnimationFrame(frame); };
    new ResizeObserver(fit).observe(canvas);
    new IntersectionObserver((entries) => { visible = entries[0].isIntersecting; start(); }, { rootMargin: "80px" }).observe(canvas);
    document.addEventListener("visibilitychange", start);
    fit();
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
      const s = getState(12);
      draw(ctx, w, h, s.style, s.colors, 12, s.progress, { dpr });
      visible = false;
    }
  }

  window.MBBar = { PALETTES, music, draw, animate };
})();
