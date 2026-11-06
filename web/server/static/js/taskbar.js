/* The Windows 11 taskbar at the bottom of every page, with MusicBar playing on it: the bar drawn under
   the icons (the page's scroll is the song's progress), the player and its karaoke line on the left,
   and the Start menu, quick settings, notifications and toasts working like Windows'. */
(function () {
  "use strict";
  const tb = document.getElementById("tb");
  if (!tb) return;
  const S = JSON.parse(document.getElementById("tb-strings").textContent);
  const root = document.documentElement;
  const BEAT = 60 / 118;

  const store = {
    get(k) { try { return localStorage.getItem(k); } catch (_) { return null; } },
    set(k, v) { try { localStorage.setItem(k, v); } catch (_) { /* a private window: kept for this page only */ } },
  };

  // ------------------------------------------------------------------ the song's clock and the motion switch

  const reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  // Motion is off until the visitor turns it on (the play button, quick settings, Start): the page stays
  // light to scroll, and the taskbar shows a still frame of the look.
  let still = reduce || store.get("mb-motion") !== "on";
  let playing = !still;
  let songTime = 12, last = performance.now();
  const clock = () => {
    const now = performance.now();
    if (playing) songTime += Math.min(0.1, (now - last) / 1000);
    last = now;
    return songTime;
  };
  root.classList.toggle("still", still);

  // ------------------------------------------------------------------ looks

  // The app's own styles come with MusicBar's real render (a video with alpha); anything else (a
  // gallery look, an LED effect) is drawn by the page's copy of the bar.
  const LOOKS = S.looks;
  let lookIndex = 0;
  let base = Object.assign({ author: "MusicBar" }, LOOKS[0]);
  let look = base;
  let tryTimer = 0;
  let custom = false; // the visitor picked a look, or the page is about one
  const titleEl = document.getElementById("tb-title");
  const qsNow = document.getElementById("qs-now");
  const video = document.getElementById("tb-video");
  const face = tb.querySelector(".tb-face");
  const defaultTitle = titleEl.textContent;

  function wear(l, title) {
    look = l;
    titleEl.textContent = title || defaultTitle;
    if (qsNow) qsNow.textContent = qsNow.dataset.t.replace("{n}", l.name || l.style);
    const real = !!l.video;
    if (real && video.getAttribute("src") !== l.video) video.src = l.video;
    video.hidden = !real;
    canvas.hidden = real;
    if (real && playing) video.play().catch(() => {});
    else if (real) freeze();
    if (!real) kick();
    window.dispatchEvent(new CustomEvent("mb:wear", { detail: l }));
  }
  // The player shows the look like a song: its name, then who made it.
  const titleOf = (l) => (l.author && l.author !== "MusicBar" ? `${l.name}, ${S.by} ${l.author}` : l.name);
  function freeze() {
    const at = () => { video.pause(); if (video.duration) video.currentTime = video.duration * 0.55; };
    if (video.readyState >= 1) at(); else video.addEventListener("loadedmetadata", at, { once: true });
  }
  function tryOn(l) {
    clearTimeout(tryTimer);
    wear(Object.assign({}, l), titleOf(l));
  }
  function restore() {
    clearTimeout(tryTimer);
    tryTimer = setTimeout(() => wear(base, custom ? titleOf(base) : null), 260);
  }
  function step(d) {
    lookIndex = (lookIndex + d + LOOKS.length) % LOOKS.length;
    choose(LOOKS[lookIndex]);
  }
  // A choice that stays (a style tile, the player's buttons): it becomes what the taskbar goes back to.
  function choose(l) {
    clearTimeout(tryTimer);
    base = Object.assign({ author: "MusicBar" }, l);
    custom = true;
    const i = LOOKS.findIndex((x) => x.key && x.key === l.key);
    if (i >= 0) lookIndex = i;
    wear(base, titleOf(base));
  }

  // Rows that try their look on while the pointer is over them.
  document.addEventListener("pointerover", (e) => {
    const row = e.target.closest && e.target.closest("[data-try]");
    if (!row || row.contains(e.relatedTarget)) return;
    try { tryOn(JSON.parse(row.dataset.try)); } catch (_) { /* ignored */ }
  });
  document.addEventListener("pointerout", (e) => {
    const row = e.target.closest && e.target.closest("[data-try]");
    if (!row || row.contains(e.relatedTarget)) return;
    restore();
  });

  // A page about one look (an item) wears it from the start.
  const pageLook = document.querySelector("[data-try-page]");
  if (pageLook) {
    try {
      const l = JSON.parse(pageLook.dataset.tryPage);
      base = l;
      custom = true;
      queueMicrotask(() => wear(base, titleOf(base)));
    } catch (_) { /* keeps the default */ }
  }

  // ------------------------------------------------------------------ the bar under the icons

  const canvas = document.getElementById("tb-canvas");
  const ctx = canvas.getContext("2d");
  const np = tb.querySelector(".tb-np");
  let gain = Math.max(0, Math.min(100, parseInt(store.get("mb-gain") || "70", 10))) / 100;
  let w = 0, h = 0, dpr = 1;
  const fit = () => {
    dpr = Math.min(2, window.devicePixelRatio || 1);
    w = Math.max(1, Math.round(canvas.clientWidth * dpr));
    h = Math.max(1, Math.round(canvas.clientHeight * dpr));
    if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; }
  };
  new ResizeObserver(fit).observe(canvas);
  fit();

  const progress = () => {
    const max = document.documentElement.scrollHeight - window.innerHeight;
    return max > 0 ? Math.min(1, Math.max(0, window.scrollY / max)) : 1;
  };

  function paint(t) {
    if (!window.MBBar) return;
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, w, h);
    const g = 0.25 + 0.75 * gain;
    ctx.save();
    ctx.translate(0, h * (1 - g));
    ctx.scale(1, g);
    window.MBBar.draw(ctx, w, h, look.style, look.colors, t, progress(), { dpr });
    ctx.restore();
    // Keep the player's words readable: the bar fades out under them.
    const edge = (np.getBoundingClientRect().right + 24) * dpr;
    ctx.globalCompositeOperation = "destination-out";
    const fade = ctx.createLinearGradient(0, 0, edge + 60 * dpr, 0);
    fade.addColorStop(0, "rgba(0,0,0,.72)");
    fade.addColorStop(edge / (edge + 60 * dpr), "rgba(0,0,0,.72)");
    fade.addColorStop(1, "rgba(0,0,0,0)");
    ctx.fillStyle = fade;
    ctx.fillRect(0, 0, edge + 60 * dpr, h);
    ctx.globalCompositeOperation = "source-over";
  }

  // ------------------------------------------------------------------ karaoke on the taskbar

  const lyricEl = document.getElementById("tb-lyric");
  let words = [], lyricText = "", manual = -1;
  function setLyric(text) {
    text = (text || "").trim();
    if (text === lyricText) return;
    lyricText = text;
    lyricEl.textContent = "";
    words = text.split(/\s+/).filter(Boolean).map((word, i) => {
      const s = document.createElement("span");
      s.className = "w";
      s.textContent = (i ? " " : "") + word;
      lyricEl.appendChild(s);
      return s;
    });
  }
  function sing(index) {
    for (let i = 0; i < words.length; i++) {
      words[i].classList.toggle("on", i < index);
      words[i].classList.toggle("now", i === index - 1);
    }
    // Keep the word being sung in view when the line is longer than the space.
    const now = words[index - 1];
    if (now) lyricEl.scrollLeft = Math.max(0, now.offsetLeft + now.offsetWidth - lyricEl.clientWidth + 8);
  }
  // The section in view gives the line; with nothing tagged, the page's own line.
  const sections = [...document.querySelectorAll("[data-lyric]")].filter((el) => el !== document.body);
  setLyric(document.body.dataset.lyric);
  const lyricObserver = new IntersectionObserver((entries) => {
    for (const e of entries) e.target._ratio = e.intersectionRatio;
    let best = null, ratio = 0;
    for (const s of sections) if ((s._ratio || 0) > ratio) { best = s; ratio = s._ratio; }
    setLyric(best ? best.dataset.lyric : document.body.dataset.lyric);
  }, { threshold: [0, 0.2, 0.4, 0.6, 0.8, 1] });
  sections.forEach((s) => lyricObserver.observe(s));

  // ------------------------------------------------------------------ the frame loop

  let raf = 0;
  function frame() {
    raf = 0;
    if (document.hidden) return;
    const t = clock();
    if (!canvas.hidden) paint(t);
    if (manual < 0 && words.length) {
      const span = words.length + 3;
      sing(still ? words.length : Math.floor(t / BEAT) % span);
    }
    if (!still) raf = requestAnimationFrame(frame);
  }
  const kick = () => { if (!raf) raf = requestAnimationFrame(frame); };
  document.addEventListener("visibilitychange", kick);
  // Still: draw once more when the page scrolls (the progress moves), without a scroll listener.
  new ResizeObserver(kick).observe(document.body);
  setInterval(() => { if (still) kick(); }, 400);
  kick();

  // ------------------------------------------------------------------ player buttons and switches

  const playBtn = tb.querySelector('[data-tb="play"]');
  function setPlaying(on) {
    playing = on;
    playBtn.setAttribute("aria-pressed", String(on));
    playBtn.setAttribute("aria-label", on ? playBtn.dataset.on : playBtn.dataset.off);
    playBtn.querySelector(".pz").hidden = !on;
    playBtn.querySelector(".pl").hidden = on;
    face.classList.toggle("sing", on);
    if (on && !video.hidden) video.play().catch(() => {}); else if (!video.hidden) freeze(); else video.pause();
    window.dispatchEvent(new CustomEvent("mb:play", { detail: on }));
    kick();
  }
  function setStill(on) {
    still = on;
    root.classList.toggle("still", on);
    store.set("mb-motion", on ? "off" : "on");
    document.querySelectorAll('[data-tb="motion"]').forEach((b) => {
      if (b.hasAttribute("aria-pressed")) b.setAttribute("aria-pressed", String(!on));
      if (b.dataset.on) { b.setAttribute("aria-label", on ? b.dataset.off : b.dataset.on); b.dataset.tip = on ? b.dataset.off : b.dataset.on; }
    });
    setPlaying(!on);
    window.dispatchEvent(new CustomEvent("mb:motion", { detail: !on }));
  }
  // The quick-settings tile and the switch on the home page drive the same setting.
  function setClear(on) {
    tb.classList.toggle("clear", on);
    document.querySelectorAll('[data-tb="clear"]').forEach((b) => b.setAttribute("aria-pressed", String(on)));
    store.set("mb-clear", on ? "1" : "0");
  }
  setClear(store.get("mb-clear") === "1");
  setStill(still);

  const gainInput = document.querySelector('[data-tb="gain"]');
  if (gainInput) {
    gainInput.value = Math.round(gain * 100);
    gainInput.nextElementSibling.textContent = gainInput.value;
    gainInput.addEventListener("input", () => {
      gain = gainInput.value / 100;
      gainInput.nextElementSibling.textContent = gainInput.value;
      store.set("mb-gain", gainInput.value);
      kick();
    });
  }

  document.addEventListener("click", (e) => {
    const b = e.target.closest("[data-tb]");
    if (!b) return;
    switch (b.dataset.tb) {
      case "play": setStill(!still); break;
      case "prev": step(-1); break;
      case "next": step(1); break;
      case "motion": setStill(!still); break;
      case "clear": setClear(!tb.classList.contains("clear")); break;
      case "nc-clear":
        document.querySelectorAll("#fly-nc .note").forEach((n) => n.remove());
        tb.querySelector(".tb-badge")?.removeAttribute("data-n");
        break;
      default: return;
    }
    kick();
  });

  // ------------------------------------------------------------------ flyouts

  let open = null;
  function close() {
    if (!open) return;
    open.fly.classList.remove("on");
    open.btns.forEach((b) => b.setAttribute("aria-expanded", "false"));
    open.btns.forEach((b) => b.classList.remove("open", "focus"));
    open = null;
  }
  function show(id) {
    const fly = document.getElementById("fly-" + id);
    if (!fly) return;
    if (open && open.fly === fly) { close(); return; }
    close();
    const btns = [...tb.querySelectorAll(`[data-fly="${id}"]`)];
    fly.classList.add("on");
    btns.forEach((b) => b.setAttribute("aria-expanded", "true"));
    if (id === "hidden") {
      const r = btns[0].getBoundingClientRect();
      fly.style.left = Math.max(8, r.left + r.width / 2 - fly.offsetWidth / 2) + "px";
    }
    if (id === "nc") {
      const d = document.getElementById("nc-date");
      if (d) d.textContent = new Intl.DateTimeFormat(S.lang === "vi" ? "vi-VN" : "en-US", { weekday: "long", day: "numeric", month: "long" }).format(new Date());
    }
    open = { fly, btns };
    const field = fly.querySelector("input[type=search]");
    if (field && window.matchMedia("(pointer: fine)").matches) setTimeout(() => field.focus(), 60);
  }
  tb.addEventListener("click", (e) => {
    const b = e.target.closest("[data-fly]");
    if (!b) return;
    e.preventDefault();
    show(b.dataset.fly);
  });
  document.addEventListener("pointerdown", (e) => {
    if (open && !open.fly.contains(e.target) && !e.target.closest("[data-fly]")) close();
  });
  document.addEventListener("keydown", (e) => { if (e.key === "Escape") close(); });

  // ------------------------------------------------------------------ the clock

  const timeEl = document.getElementById("tb-time");
  const dateEl = document.getElementById("tb-date");
  function tick() {
    const d = new Date();
    if (S.lang === "vi") {
      timeEl.textContent = `${String(d.getHours()).padStart(2, "0")}:${String(d.getMinutes()).padStart(2, "0")}`;
      dateEl.textContent = `${String(d.getDate()).padStart(2, "0")}/${String(d.getMonth() + 1).padStart(2, "0")}/${d.getFullYear()}`;
    } else {
      timeEl.textContent = d.toLocaleTimeString("en-US", { hour: "numeric", minute: "2-digit" });
      dateEl.textContent = `${d.getMonth() + 1}/${d.getDate()}/${d.getFullYear()}`;
    }
  }
  tick();
  setInterval(tick, 10000);

  // ------------------------------------------------------------------ toasts

  const toasts = document.getElementById("toasts");
  function toast({ title, body, actions = [], seconds = 8 }) {
    const el = document.createElement("div");
    el.className = "toast";
    el.setAttribute("role", "status");
    const head = document.createElement("div");
    head.className = "app";
    head.innerHTML = '<img src="/static/img/musicbar-icon.png" alt="">MusicBar';
    const x = document.createElement("button");
    x.type = "button";
    x.setAttribute("aria-label", S.close);
    x.innerHTML = '<svg class="i" viewBox="0 0 256 256" fill="currentColor" aria-hidden="true"><path d="M205.66,194.34a8,8,0,0,1-11.32,11.32L128,139.31,61.66,205.66a8,8,0,0,1-11.32-11.32L116.69,128,50.34,61.66A8,8,0,0,1,61.66,50.34L128,116.69l66.34-66.35a8,8,0,0,1,11.32,11.32L139.31,128Z"/></svg>';
    head.appendChild(x);
    el.appendChild(head);
    const b = document.createElement("b");
    b.textContent = title;
    el.appendChild(b);
    if (body) el.appendChild(document.createTextNode(body));
    if (actions.length) {
      const row = document.createElement("div");
      row.className = "acts";
      for (const a of actions) {
        const n = document.createElement(a.href ? "a" : "button");
        if (a.href) n.href = a.href; else n.type = "button";
        n.textContent = a.label;
        if (!a.href) n.addEventListener("click", () => dismiss());
        row.appendChild(n);
      }
      el.appendChild(row);
    }
    const dismiss = () => { el.classList.add("out"); setTimeout(() => el.remove(), 260); };
    x.addEventListener("click", dismiss);
    toasts.appendChild(el);
    if (seconds) setTimeout(dismiss, seconds * 1000);
    return el;
  }

  // "Open in MusicBar": a musicbar:// link. When nothing takes it (no app, or an app older than 1.8),
  // the page stays focused, and a toast says what to do.
  document.addEventListener("click", (e) => {
    const a = e.target.closest("[data-open-app]");
    if (!a) return;
    // Counted for the admin's dashboard (whether the app opened, the page can't tell).
    const slug = /^musicbar:\/\/(?:apply|install)\/([a-z0-9-]+)/.exec(a.getAttribute("href") || "");
    if (slug && navigator.sendBeacon) navigator.sendBeacon(`/api/v1/apply/${slug[1]}`);
    let left = false;
    const away = () => { left = true; };
    window.addEventListener("blur", away, { once: true });
    document.addEventListener("visibilitychange", away, { once: true });
    setTimeout(() => {
      window.removeEventListener("blur", away);
      if (!left && document.hasFocus()) {
        const actions = [{ label: S.get, href: "/download" }];
        if (a.dataset.file) actions.push({ label: S.file, href: a.dataset.file });
        toast({ title: S.openT, body: S.openD, seconds: 14, actions });
      }
    }, 1500);
  });

  if (!pageLook) wear(base, null);

  window.MBTaskbar = {
    beat: BEAT,
    time: () => songTime,
    playing: () => playing,
    still: () => still,
    looks: LOOKS,
    tryOn, restore, choose,
    karaoke(text, index) { if (text === null) { manual = -1; return; } setLyric(text); manual = index; sing(index); },
    toast,
  };
})();
