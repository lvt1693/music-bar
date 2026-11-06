/* Every page: clips that play only while seen, presets drawn live, copy buttons, the upload drop zone. */
(function () {
  "use strict";
  const root = document.documentElement;
  root.classList.remove("nojs");
  root.classList.add("js");
  const still = () => root.classList.contains("still") || window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  // Clips load and play only while on screen (and not when motion is off). The home page's film plays
  // unless the visitor turned motion off themselves, or asks the system for less motion.
  const turnedOff = () => { try { return localStorage.getItem("mb-motion") === "off"; } catch (_) { return false; } };
  const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  const clips = new IntersectionObserver((entries) => {
    for (const e of entries) {
      const v = e.target;
      if (e.isIntersecting) {
        if (v.dataset.lazyVideo && !v.getAttribute("src")) v.src = v.dataset.lazyVideo;
        const may = v.hasAttribute("data-film") ? !reduced && !turnedOff() : !still();
        if (may) v.play().catch(() => {}); else v.pause();
      } else if (!v.paused) v.pause();
    }
  }, { rootMargin: "160px" });
  document.querySelectorAll("video[data-film]").forEach((v) => {
    if (reduced || turnedOff()) { v.removeAttribute("autoplay"); v.pause(); }
    clips.observe(v);
  });
  document.querySelectorAll("video[data-lazy-video], video[data-auto-video]").forEach((v) => {
    if (v.hasAttribute("data-auto-video") && still()) { v.removeAttribute("autoplay"); v.pause(); }
    clips.observe(v);
  });
  window.addEventListener("mb:motion", (e) => {
    document.querySelectorAll("video[data-lazy-video], video[data-auto-video], video[data-film]").forEach((v) => {
      const r = v.getBoundingClientRect();
      if (!e.detail) v.pause();
      else if (r.bottom > 0 && r.top < window.innerHeight) v.play().catch(() => {});
    });
  });

  // Gallery cards show a still picture; the one under the pointer plays its clip.
  document.addEventListener("pointerover", (e) => {
    const card = e.target.closest && e.target.closest(".look");
    if (!card || card.contains(e.relatedTarget)) return;
    const v = card.querySelector("video[data-hover-video]");
    if (!v) return;
    if (v.dataset.hoverVideo && !v.getAttribute("src")) v.src = v.dataset.hoverVideo;
    v.play().then(() => v.classList.add("on")).catch(() => {});
  });
  document.addEventListener("pointerout", (e) => {
    const card = e.target.closest && e.target.closest(".look");
    if (!card || card.contains(e.relatedTarget)) return;
    const v = card.querySelector("video[data-hover-video]");
    if (v) { v.pause(); v.classList.remove("on"); }
  });

  // The gallery's quick look: a click on a card opens it over the page, arrows step to the next one.
  // The card's link still leads to the look's own page (a new tab, or no scripts).
  const quick = document.getElementById("quick");
  if (quick && quick.showModal) {
    const q = (n) => quick.querySelector(`[data-q="${n}"]`);
    const cards = [...document.querySelectorAll(".look[data-item]")];
    const fileLabel = q("file").textContent;
    let index = -1;
    const show = (i) => {
      index = (i + cards.length) % cards.length;
      const d = JSON.parse(cards[index].dataset.item);
      q("name").textContent = d.name;
      q("by").textContent = d.by;
      q("about").textContent = d.about || "";
      q("about").hidden = !d.about;
      q("downloads").textContent = d.downloads;
      const swatch = q("colors");
      swatch.textContent = "";
      for (const c of d.colors || []) {
        const dot = document.createElement("i");
        dot.style.background = c;
        dot.title = c;
        swatch.appendChild(dot);
      }
      const img = q("img"), v = q("video");
      img.hidden = !d.preview;
      if (d.preview) img.src = d.preview;
      v.classList.remove("on");
      v.pause();
      if (d.video) { v.src = d.video; v.play().then(() => v.classList.add("on")).catch(() => {}); }
      else v.removeAttribute("src");
      quick.querySelector(".desk").style.setProperty("--shot", d.preview ? `url('${d.preview}')` : "none");
      q("apply").href = `musicbar://apply/${d.slug}`;
      q("apply").dataset.file = d.file;
      q("file").href = d.file;
      q("file").textContent = `${fileLabel}, ${d.size}`;
      q("page").href = d.page;
      if (window.MBTaskbar) window.MBTaskbar.tryOn(JSON.parse(cards[index].dataset.try));
      // The address names the open look, so it can be shared as it is.
      history.replaceState(null, "", `#look=${d.slug}`);
    };
    // A shared link (#look=slug) opens that look; a plain visit opens nothing.
    const opened = /^#look=([a-z0-9-]+)$/.exec(location.hash);
    if (opened) {
      const first = cards.findIndex((c) => JSON.parse(c.dataset.item).slug === opened[1]);
      if (first >= 0) { show(first); quick.showModal(); }
    }
    document.addEventListener("click", (e) => {
      if (e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey) return;
      const card = e.target.closest(".look[data-item]");
      if (!card || e.target.closest("[data-open-app]")) return;
      e.preventDefault();
      show(cards.indexOf(card));
      if (!quick.open) quick.showModal();
    });
    quick.addEventListener("click", (e) => {
      if (e.target === quick) { quick.close(); return; } // the backdrop
      const b = e.target.closest("[data-q]");
      if (!b) return;
      if (b.dataset.q === "close") quick.close();
      else if (b.dataset.q === "prev") show(index - 1);
      else if (b.dataset.q === "next") show(index + 1);
    });
    quick.addEventListener("keydown", (e) => {
      if (e.key === "ArrowLeft") show(index - 1);
      else if (e.key === "ArrowRight") show(index + 1);
    });
    quick.addEventListener("close", () => {
      q("video").pause();
      history.replaceState(null, "", location.pathname + location.search);
      if (window.MBTaskbar) window.MBTaskbar.restore();
    });
  }

  // The coffee QR (Vietnamese pages): the little one opens into the full code, Esc or × folds it back.
  const donate = document.getElementById("donate");
  if (donate) {
    const pill = donate.querySelector(".donate-pill");
    const card = donate.querySelector(".donate-card");
    const fold = () => { card.hidden = true; pill.setAttribute("aria-expanded", "false"); pill.focus(); };
    pill.addEventListener("click", () => { card.hidden = false; pill.setAttribute("aria-expanded", "true"); donate.querySelector(".donate-x").focus(); });
    donate.querySelector(".donate-x").addEventListener("click", fold);
    document.addEventListener("keydown", (e) => { if (e.key === "Escape" && !card.hidden) fold(); });
  }

  // Presets drawn live with the page's own copy of the bar.
  if (window.MBBar) {
    document.querySelectorAll("canvas[data-preset]").forEach((c, i) => {
      let p = {};
      try { p = JSON.parse(c.dataset.preset); } catch (_) { /* drawn with the defaults */ }
      const state = (t) => ({ style: p.style || "Spectrum", colors: p.colors, t: t + i * 1.7, progress: 0.62 });
      if (!still()) { window.MBBar.animate(c, state); return; }
      // Motion off: one frame, drawn at the canvas's own size.
      const dpr = Math.min(2, window.devicePixelRatio || 1), s = state(12);
      c.width = Math.max(1, Math.round(c.clientWidth * dpr));
      c.height = Math.max(1, Math.round(c.clientHeight * dpr));
      window.MBBar.draw(c.getContext("2d"), c.width, c.height, s.style, s.colors, s.t, s.progress, { dpr });
    });
  }

  // Filters that apply as soon as they change.
  document.querySelectorAll("select[data-autosubmit]").forEach((s) => s.addEventListener("change", () => s.form.submit()));

  // Copy buttons (hashes): a Windows toast says it worked.
  document.addEventListener("click", (e) => {
    const b = e.target.closest("[data-copy]");
    if (!b) return;
    navigator.clipboard.writeText(b.dataset.copy).then(() => {
      const strings = JSON.parse(document.getElementById("tb-strings").textContent);
      if (window.MBTaskbar) window.MBTaskbar.toast({ title: strings.copied, body: b.dataset.copy.slice(0, 16) + "…", seconds: 3 });
    });
  });

  // Upload zone: drop a file on it, see its name.
  document.querySelectorAll(".drop").forEach((zone) => {
    const input = zone.querySelector("input[type=file]");
    const label = zone.querySelector("[data-file-name]");
    const show = () => { if (label && input.files[0]) label.textContent = `${input.files[0].name}, ${Math.ceil(input.files[0].size / 1024)} KB`; };
    input.addEventListener("change", show);
    ["dragenter", "dragover"].forEach((ev) => zone.addEventListener(ev, (e) => { e.preventDefault(); zone.classList.add("over"); }));
    ["dragleave", "drop"].forEach((ev) => zone.addEventListener(ev, () => zone.classList.remove("over")));
    zone.addEventListener("drop", (e) => {
      e.preventDefault();
      if (e.dataTransfer.files.length) { input.files = e.dataTransfer.files; show(); }
    });
  });
})();
