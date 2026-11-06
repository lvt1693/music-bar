/* The home page's story. One scene a feature, each driven by the scroll: the taskbar changes outfits
   (and the real taskbar at the bottom of the screen changes with it), the scroll sings a lyric, the
   taskbar melts into the wallpaper, the face peeks up and sings, the gallery slides by. Wide screens
   pin each scene while it plays; narrow ones play it as it passes. Reduced motion: still scenes. */
(function () {
  "use strict";
  const TB = window.MBTaskbar;
  const reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  // Words wrapped for the headline, the lyric and the scene titles.
  const split = (el, inner) => {
    const text = el.textContent.trim();
    el.textContent = "";
    return text.split(/\s+/).map((word, i) => {
      if (i) el.append(" ");
      const w = document.createElement("span");
      w.className = "w";
      if (inner) {
        const wi = document.createElement("span");
        wi.className = "wi";
        wi.textContent = word;
        w.append(wi);
      } else {
        w.textContent = word;
      }
      el.append(w);
      return w;
    });
  };
  document.querySelectorAll("[data-words]").forEach((el) => split(el));
  const titles = [...document.querySelectorAll(".scene-text h2")].map((h) => split(h, true));

  // Videos load when their scene comes near.
  const load = new IntersectionObserver((entries) => {
    for (const e of entries) {
      if (!e.isIntersecting) continue;
      e.target.querySelectorAll("video[data-src]").forEach((v) => { if (!v.getAttribute("src")) v.src = v.dataset.src; });
      load.unobserve(e.target);
    }
  }, { rootMargin: "600px 0px" });
  document.querySelectorAll(".scene").forEach((s) => load.observe(s));
  const play = (v) => { if (v && v.getAttribute("src")) v.play().catch(() => {}); };

  // ------------------------------------------------------------------ outfits
  const keys = ["aurora", "silk", "holo", "ink", "bokeh", "spectrum"];
  const outfits = [...document.querySelectorAll(".outfit")];
  const names = [...document.querySelectorAll(".names li")];
  const scrubBar = document.querySelector(".scrub");
  let current = -1;
  function wearOutfit(i) {
    if (i === current) return;
    current = i;
    outfits.forEach((v, k) => {
      v.classList.toggle("on", k === i);
      if (k === i) { if (!v.getAttribute("src")) v.src = v.dataset.src; play(v); } else v.pause();
    });
    names.forEach((n, k) => n.classList.toggle("on", k === i));
    const look = TB && TB.looks.find((l) => l.key === keys[i]);
    if (look) TB.choose(look);
  }

  // ------------------------------------------------------------------ karaoke
  const lyric = document.querySelector(".kara-line");
  const words = lyric ? [...lyric.querySelectorAll(".w")] : [];
  const lyricText = lyric ? lyric.textContent.trim() : "";
  function sing(p) {
    const index = Math.min(words.length, Math.floor(p * (words.length + 1)));
    words.forEach((w, i) => { w.classList.toggle("on", i < index); w.classList.toggle("now", i === index - 1); });
    if (TB) TB.karaoke(lyricText, index);
  }
  const unsing = () => { if (TB) TB.karaoke(null); };

  // ------------------------------------------------------------------ the rest: still, if motion is reduced
  if (reduce || !window.gsap || !window.ScrollTrigger) {
    wearOutfit(0);
    words.forEach((w) => w.classList.add("on"));
    document.querySelector(".clear-stage")?.style.setProperty("--solid", ".12");
    document.querySelector(".face-peek")?.style.setProperty("--rise", "0%");
    return;
  }
  gsap.registerPlugin(ScrollTrigger);
  document.documentElement.classList.add("story");
  const ease = "power3.out";

  // The headline arrives word by word; the reel rises under it.
  gsap.from(".hero-title .w", { yPercent: 60, opacity: 0, duration: .9, ease, stagger: .06 });
  gsap.from(".hero-sub, .hero .acts, .hero .fact", { y: 18, opacity: 0, duration: .8, ease, stagger: .08, delay: .3 });
  gsap.from("#reel", { y: 40, opacity: 0, scale: .97, duration: 1.1, ease, delay: .45 });
  // Leaving, the reel sinks back a little.
  gsap.to("#reel", { scale: .94, opacity: .45, ease: "none", scrollTrigger: { trigger: "#reel", start: "top 20%", end: "bottom top", scrub: true } });

  // Scene titles: each word comes up out of its own slot.
  titles.forEach((ws) => {
    gsap.from(ws.map((w) => w.firstChild), { yPercent: 110, duration: .8, ease, stagger: .05, scrollTrigger: { trigger: ws[0].closest(".scene-text"), start: "top 82%" } });
  });

  const mm = gsap.matchMedia();
  const wide = "(min-width: 901px)";
  const narrow = "(max-width: 900px)";

  // Outfits: one look a stretch of scroll.
  const outfitsUpdate = (self) => {
    wearOutfit(Math.min(keys.length - 1, Math.floor(self.progress * keys.length)));
    scrubBar.style.setProperty("--p", self.progress.toFixed(3));
  };
  mm.add(wide, () => {
    ScrollTrigger.create({ trigger: "#outfits", start: "top top", end: `+=${keys.length * 45}%`, pin: true, scrub: true, onUpdate: outfitsUpdate });
  });
  mm.add(narrow, () => {
    ScrollTrigger.create({ trigger: "#outfits .outfit-stage", start: "top 75%", end: "bottom 15%", scrub: true, onUpdate: outfitsUpdate });
  });

  // Karaoke: the scroll sings the line, on the page and on the taskbar.
  mm.add(wide, () => {
    ScrollTrigger.create({ trigger: "#karaoke", start: "top top", end: "+=110%", pin: true, scrub: true,
      onUpdate: (s) => sing(s.progress), onLeave: unsing, onLeaveBack: unsing, onEnter: () => play(document.querySelector(".kara-screen video")) });
  });
  mm.add(narrow, () => {
    ScrollTrigger.create({ trigger: ".kara-line", start: "top 80%", end: "bottom 30%", scrub: true,
      onUpdate: (s) => sing(s.progress), onLeave: unsing, onLeaveBack: unsing, onEnter: () => play(document.querySelector(".kara-screen video")) });
  });

  // Clear: the taskbar's glass melts away, the wallpaper settles.
  const melt = (trigger) => gsap.timeline({ scrollTrigger: trigger })
    .fromTo(".clear-stage", { "--solid": .94, "--zoom": 1.08 }, { "--solid": .1, "--zoom": 1, ease: "none" })
    .from(".clear-text", { y: 30, opacity: 0, ease: "none", duration: .4 }, 0);
  mm.add(wide, () => melt({ trigger: "#clear", start: "top top", end: "+=90%", pin: true, scrub: true, onEnter: () => play(document.querySelector(".mini-tb video")) }));
  mm.add(narrow, () => melt({ trigger: ".clear-stage", start: "top 80%", end: "bottom 35%", scrub: true, onEnter: () => play(document.querySelector(".mini-tb video")) }));

  // The face: peeks over the taskbar, looks left, then right, then sings.
  const face = document.querySelector(".face.big");
  const peek = (trigger) => gsap.timeline({ scrollTrigger: Object.assign(trigger, { onUpdate: (s) => face.classList.toggle("sing", s.progress > .72) }) })
    .fromTo(".face-peek", { "--rise": "100%" }, { "--rise": "0%", ease: "power2.out", duration: .4 })
    .fromTo(face, { "--look": "-4px" }, { "--look": "4px", ease: "sine.inOut", duration: .35 })
    .to(face, { "--look": "0px", ease: "sine.inOut", duration: .25 });
  mm.add(wide, () => peek({ trigger: "#face", start: "top top", end: "+=80%", pin: true, scrub: true }));
  mm.add(narrow, () => peek({ trigger: ".face-stage", start: "top 80%", end: "bottom 30%", scrub: true }));

  // The shelf: the gallery slides sideways while the page is held.
  mm.add(wide, () => {
    const track = document.querySelector(".shelf-track");
    const view = document.querySelector(".shelf-view");
    if (!track) return;
    const distance = () => Math.max(0, track.scrollWidth - view.clientWidth);
    gsap.to(track, { x: () => -distance(), ease: "none",
      scrollTrigger: { trigger: "#shelf", start: "top top", end: () => `+=${distance()}`, pin: true, scrub: true, invalidateOnRefresh: true } });
  });

  // The download panel lands last.
  gsap.from(".endcap", { y: 40, opacity: 0, duration: .9, ease, scrollTrigger: { trigger: ".endcap", start: "top 88%" } });
})();
