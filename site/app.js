/*
 * TechDigest web arşivi — framework'süz, tek dosya.
 *
 * Veri: data/index.json (gün listesi) + data/days/YYYY-MM-DD.json (günün bülteni).
 * Rotalar: #/ (en son gün) · #/YYYY-MM-DD · #/archive · #/words
 *
 * Güvenlik: dışarıdan gelen hiçbir metin innerHTML ile yazılmaz; her şey createElement + textContent.
 * Linkler sadece http(s) ise tıklanabilir. CSP (index.html) satır içi script'i zaten engelliyor.
 */
(() => {
  "use strict";

  const main = document.getElementById("main");
  const store = {
    get: (k, d) => { try { return JSON.parse(localStorage.getItem(k)) ?? d; } catch { return d; } },
    set: (k, v) => { try { localStorage.setItem(k, JSON.stringify(v)); } catch { /* gizli mod */ } },
  };
  const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)").matches;

  // ---------- Küçük yardımcılar ----------

  /** Güvenli eleman üretici: metinler textContent ile, özellikler setAttribute ile. */
  function h(tag, props = {}, ...children) {
    const el = document.createElement(tag);
    for (const [key, value] of Object.entries(props)) {
      if (value == null || value === false) continue;
      if (key === "class") el.className = value;
      else if (key === "style") for (const [p, v] of Object.entries(value)) el.style.setProperty(p, v);
      else if (key.startsWith("on")) el.addEventListener(key.slice(2), value);
      else el.setAttribute(key, value === true ? "" : value);
    }
    for (const child of children.flat()) {
      if (child == null || child === false) continue;
      el.append(child instanceof Node ? child : document.createTextNode(String(child)));
    }
    return el;
  }

  const safeUrl = (url) => {
    try { const u = new URL(url); return u.protocol === "https:" || u.protocol === "http:" ? u.href : null; }
    catch { return null; }
  };

  const extLink = (url, props, ...children) => {
    const href = safeUrl(url);
    return href ? h("a", { href, target: "_blank", rel: "noopener noreferrer", ...props }, ...children) : h("span", props, ...children);
  };

  const icon = (paths) => {
    const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    svg.setAttribute("viewBox", "0 0 24 24");
    svg.setAttribute("class", "icon");
    svg.setAttribute("aria-hidden", "true");
    for (const d of paths) {
      const p = document.createElementNS("http://www.w3.org/2000/svg", "path");
      p.setAttribute("d", d);
      svg.append(p);
    }
    return svg;
  };
  const ICONS = {
    search: ["M11 19a8 8 0 1 0 0-16 8 8 0 0 0 0 16z", "M21 21l-4.3-4.3"],
    left: ["M15 18l-6-6 6-6"],
    right: ["M9 18l6-6-6-6"],
    alert: ["M12 9v4", "M12 17h.01", "M10.3 3.9L1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z"],
    shuffle: ["M16 3h5v5", "M4 20L21 3", "M21 16v5h-5", "M15 15l6 6", "M4 4l5 5"],
  };

  const parseDate = (iso) => { const [y, m, d] = iso.split("-").map(Number); return new Date(y, m - 1, d); };
  const fmt = (date, opts) => date.toLocaleDateString("en-GB", opts);
  const isToday = (iso) => iso === new Date().toLocaleDateString("en-CA", { timeZone: "Europe/Istanbul" });

  function when(published, dayIso) {
    const date = new Date(published);
    if (isToday(dayIso)) {
      const mins = Math.max(0, (Date.now() - date) / 60000);
      if (mins < 1) return "just now";
      if (mins < 60) return `${Math.floor(mins)}m ago`;
      if (mins < 1440) return `${Math.floor(mins / 60)}h ago`;
    }
    return date.toLocaleString("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit", timeZone: "Europe/Istanbul" });
  }

  /** Örnek cümlede kelimeyi (çekimli hâliyle) vurgular — DOM düğümleriyle, HTML string'i olmadan. */
  function highlight(example, word) {
    const idx = example.toLowerCase().indexOf(word.toLowerCase());
    if (idx < 0 || !word) return [example];
    let end = idx + word.length;
    while (end < example.length && /\p{L}/u.test(example[end])) end++;
    return [example.slice(0, idx), h("mark", {}, example.slice(idx, end)), example.slice(end)];
  }

  // ---------- Veri ----------

  const cache = new Map();
  async function load(path) {
    if (!cache.has(path)) {
      cache.set(path, fetch(path, { cache: "no-cache" }).then((r) => {
        if (!r.ok) throw new Error(`${path}: HTTP ${r.status}`);
        return r.json();
      }).catch((e) => { cache.delete(path); throw e; }));
    }
    return cache.get(path);
  }
  const loadIndex = () => load("data/index.json");
  const loadDay = (date) => load(`data/days/${date}.json`);

  // ---------- Tema ----------

  const themeBtn = document.getElementById("theme-toggle");
  const systemDark = matchMedia("(prefers-color-scheme: dark)");
  function applyTheme(theme) {
    if (theme) document.documentElement.dataset.theme = theme;
    else delete document.documentElement.dataset.theme;
    const dark = theme ? theme === "dark" : systemDark.matches;
    themeBtn.setAttribute("aria-pressed", String(dark));
    themeBtn.setAttribute("aria-label", dark ? "Switch to light mode" : "Switch to dark mode");
  }
  applyTheme(store.get("theme", null));
  systemDark.addEventListener("change", () => applyTheme(store.get("theme", null)));
  themeBtn.addEventListener("click", () => {
    const dark = document.documentElement.dataset.theme
      ? document.documentElement.dataset.theme === "dark"
      : systemDark.matches;
    const next = dark ? "light" : "dark";
    store.set("theme", next);
    transition(() => applyTheme(next));
  });

  // ---------- Görünümler ----------

  /** Kartların sırayla belirmesi için artan gecikme sırası (her sayfa çiziminde sıfırlanır). */
  let reveal = 0;

  function storyCard(story, section, dayIso, hero = false) {
    const coverage = story.coverage > 1 ? h("span", { class: "badge" }, `${story.coverage} outlets`) : null;
    const meta = h("div", { class: "story__meta" },
      hero ? null : [story.source, h("span", { class: "dot" })],
      hero ? story.source : null,
      hero ? h("span", { class: "dot" }) : null,
      when(story.published, dayIso),
      coverage ? [h("span", { class: "dot" }), coverage] : null);

    const tr = story.tr ? h("div", { class: "tr" }, h("span", { class: "tr__tag" }, "TR"), h("span", {}, story.tr)) : null;
    const article = h("article", {
      class: `card story reveal${hero ? " hero" : ""}`,
      style: { "--c": section.color, "--i": String(reveal++) },
      "data-topic": section.id,
      "data-search": [story.title, story.summary, story.tr, story.source].filter(Boolean).join(" ").toLowerCase(),
    },
      hero ? h("div", { class: "eyebrow" }, `Top story · ${section.name}`) : null,
      hero ? null : meta,
      h(hero ? "h2" : "h3", { class: "story__title" }, extLink(story.url, {}, story.title)),
      hero ? meta : null,
      story.summary ? h("p", { class: "story__summary" }, story.summary) : null,
      tr);

    if (hero && story.related?.length) {
      const max = Math.max(story.coverage, 1);
      article.append(h("div", { class: "coverage" },
        h("div", { class: "coverage__label" }, `Reported by ${story.coverage} outlets`),
        h("div", { class: "coverage__bar" }, h("div", { class: "coverage__fill", style: { "--w": `${Math.min(100, 20 + max * 4)}%` } })),
        h("div", { class: "sources" }, story.related.map((rel) => extLink(rel.url, { class: "source-pill", title: rel.title }, rel.source)))));
    }
    return article;
  }

  function flashcard(word, i) {
    const card = h("button", {
      class: "flash reveal", type: "button", "aria-pressed": "false", style: { "--i": String(i) },
      "aria-label": `${word.word}: tap to reveal the meaning`,
      "data-search": `${word.word} ${word.meaningEn} ${word.meaningTr}`.toLowerCase(),
    },
      h("div", { class: "flash__inner" },
        h("div", { class: "flash__face flash__front" },
          h("span", { class: "flash__flip" }, "Tap to flip"),
          h("div", { class: "flash__word" }, word.word),
          h("div", { class: "flash__pos" }, word.partOfSpeech),
          h("div", { class: "flash__example" }, "“", highlight(word.example, word.word), "”")),
        h("div", { class: "flash__face flash__back", "aria-hidden": "true" },
          h("div", { class: "flash__pos" }, word.partOfSpeech),
          h("div", { class: "flash__en" }, word.meaningEn),
          h("div", { class: "flash__tr" }, word.meaningTr),
          word.storyUrl ? extLink(word.storyUrl, { class: "flash__link", onclick: (e) => e.stopPropagation() }, "Read the story →") : null)));

    card.addEventListener("click", () => {
      const flipped = card.getAttribute("aria-pressed") === "true";
      card.setAttribute("aria-pressed", String(!flipped));
      card.querySelector(".flash__front").setAttribute("aria-hidden", String(!flipped));
      card.querySelector(".flash__back").setAttribute("aria-hidden", String(flipped));
    });
    return card;
  }

  function emptyState(title, text, paths = ICONS.alert) {
    return h("div", { class: "empty reveal" }, icon(paths), h("h2", {}, title), h("p", {}, text));
  }

  function setNav(name) {
    for (const a of document.querySelectorAll("[data-nav]")) {
      if (a.dataset.nav === name) a.setAttribute("aria-current", "page");
      else a.removeAttribute("aria-current");
    }
  }

  async function renderDay(date) {
    setNav("day");
    const index = await loadIndex();
    if (!index.days?.length) return [emptyState("No briefings yet", "The first digest will appear here after the next morning run.")];
    const iso = date ?? index.days[0].date;
    const pos = index.days.findIndex((d) => d.date === iso);
    if (pos < 0) return [emptyState("Day not found", `There is no briefing for ${iso}. Try the archive.`)];

    const day = await loadDay(iso);
    const sections = day.sections ?? [];
    const topicColor = Object.fromEntries(sections.map((s) => [s.id, s]));
    const newer = index.days[pos - 1], older = index.days[pos + 1];
    document.title = `TechDigest — ${fmt(parseDate(iso), { day: "numeric", month: "long", year: "numeric" })}`;

    const head = h("section", { class: "day-head" },
      h("div", {},
        h("div", { class: "eyebrow" }, isToday(iso) ? "Today's briefing" : "Daily briefing"),
        h("h1", { class: "day-title" }, fmt(parseDate(iso), { weekday: "long", day: "numeric", month: "long" })),
        h("div", { class: "stats" },
          h("span", { class: "stat" }, h("b", {}, day.stats.stories), "stories"),
          h("span", { class: "stat" }, h("b", {}, day.stats.sources), "outlets"),
          h("span", { class: "stat" }, h("b", {}, day.stats.fetched), "articles scanned"),
          day.vocabulary?.length ? h("span", { class: "stat" }, h("b", {}, day.vocabulary.length), "IELTS words") : null)),
      h("nav", { class: "pager", "aria-label": "Other days" },
        older ? h("a", { href: `#/${older.date}` }, icon(ICONS.left), "Older") : h("span", {}, icon(ICONS.left), "Older"),
        newer ? h("a", { href: `#/${newer.date}` }, "Newer", icon(ICONS.right)) : h("span", {}, "Newer", icon(ICONS.right))));

    // Araç çubuğu: arama, konu filtresi, Türkçe gizle (IELTS pratiği: önce İngilizce oku)
    const active = new Set();
    const search = h("input", { type: "search", placeholder: "Search today's stories…", "aria-label": "Search stories", autocomplete: "off" });
    const chips = sections.map((s) => h("button", {
      class: "chip", type: "button", "aria-pressed": "false", style: { "--c": s.color }, "data-topic": s.id,
      onclick: (e) => { const b = e.currentTarget; const on = b.getAttribute("aria-pressed") !== "true"; b.setAttribute("aria-pressed", String(on)); on ? active.add(s.id) : active.delete(s.id); filter(); },
    }, s.name, h("small", {}, s.stories.length)));
    const hideTr = store.get("hideTr", false);
    const trToggle = h("button", {
      class: "toggle", type: "button", "aria-pressed": String(hideTr), title: "Hide Turkish summaries to practise reading in English",
      onclick: (e) => { const on = e.currentTarget.getAttribute("aria-pressed") !== "true"; e.currentTarget.setAttribute("aria-pressed", String(on)); e.currentTarget.textContent = on ? "Show Turkish" : "Hide Turkish"; store.set("hideTr", on); content.classList.toggle("hide-tr", on); },
    }, hideTr ? "Show Turkish" : "Hide Turkish");

    const toolbar = h("div", { class: "toolbar", role: "search" },
      h("label", { class: "search" }, icon(ICONS.search), search, h("span", { class: "kbd", "aria-hidden": "true" }, "/")),
      h("div", { class: "chips", role: "group", "aria-label": "Filter by topic" }, chips, trToggle));

    const content = h("div", { class: hideTr ? "hide-tr" : "" });
    if (day.headline) content.append(storyCard(day.headline, topicColor[day.headline.topic] ?? { id: day.headline.topic, name: "", color: "#4f46e5" }, iso, true));
    for (const s of sections) {
      content.append(h("section", { class: "section", id: `topic-${s.id}`, "data-section": s.id, style: { "--c": s.color } },
        h("div", { class: "section__head reveal", style: { "--i": String(reveal++) } },
          h("span", { class: "section__swatch" }), h("h2", {}, s.name), h("span", { class: "section__count" }, s.stories.length)),
        h("div", { class: "grid" }, s.stories.map((st) => storyCard(st, s, iso)))));
    }
    const noMatch = emptyState("No stories match", "Try a different word or clear the topic filters.", ICONS.search);
    noMatch.hidden = true;
    content.append(noMatch);

    if (day.vocabulary?.length) {
      content.append(h("section", { class: "section", id: "words" },
        h("div", { class: "words-head" },
          h("div", {}, h("div", { class: "eyebrow" }, "IELTS vocabulary"), h("h2", {}, `${day.vocabulary.length} words from today's news`)),
          h("span", { class: "hint" }, "Tap a card to see the meaning")),
        h("div", { class: "cards" }, day.vocabulary.map(flashcard))));
    }

    function filter() {
      const q = search.value.trim().toLowerCase();
      let visible = 0;
      for (const card of content.querySelectorAll(".story")) {
        const show = (!active.size || active.has(card.dataset.topic)) && (!q || card.dataset.search.includes(q));
        card.hidden = !show;
        if (show) visible++;
      }
      for (const sec of content.querySelectorAll("[data-section]")) sec.hidden = !sec.querySelector(".story:not([hidden])");
      noMatch.hidden = visible > 0;
    }
    search.addEventListener("input", filter);

    return [head, toolbar, content];
  }

  async function renderArchive() {
    setNav("archive");
    document.title = "TechDigest — Archive";
    const index = await loadIndex();
    const days = index.days ?? [];
    const search = h("input", { type: "search", placeholder: "Search headlines…", "aria-label": "Search the archive", autocomplete: "off" });
    const list = h("div");
    const colors = { ai: "#7c3aed", chips: "#0891b2", markets: "#16a34a", software: "#2563eb", security: "#dc2626", policy: "#d97706" };

    function draw() {
      const q = search.value.trim().toLowerCase();
      list.replaceChildren();
      const shown = days.filter((d) => !q || (d.headline ?? "").toLowerCase().includes(q) || d.date.includes(q));
      if (!shown.length) { list.append(emptyState("Nothing found", "No day's headline matches that search.", ICONS.search)); return; }
      const byMonth = Map.groupBy ? Map.groupBy(shown, (d) => d.date.slice(0, 7)) : shown.reduce((m, d) => m.set(d.date.slice(0, 7), [...(m.get(d.date.slice(0, 7)) ?? []), d]), new Map());
      let i = 0;
      for (const [month, items] of byMonth) {
        list.append(h("section", { class: "month" },
          h("h2", {}, fmt(parseDate(`${month}-01`), { month: "long", year: "numeric" })),
          h("div", { class: "days" }, items.map((d) => h("a", { class: "card day reveal", href: `#/${d.date}`, style: { "--i": String(i++), "--c": colors[d.topic] ?? "#4f46e5" } },
            h("div", { class: "day__date" }, h("b", {}, parseDate(d.date).getDate()), h("span", {}, fmt(parseDate(d.date), { weekday: "short" }))),
            h("div", {}, h("div", { class: "day__headline" }, d.headline ?? "No headline"),
              h("div", { class: "day__topics", "aria-hidden": "true" }, (d.topics ?? []).map((t) => h("i", { style: { "--c": colors[t] ?? "#4f46e5" } })))),
            h("div", { class: "day__count" }, `${d.stories} stories`))))));
      }
    }
    search.addEventListener("input", draw);
    draw();

    return [
      h("section", { class: "archive-head" }, h("div", { class: "eyebrow" }, "Archive"), h("h1", {}, "Every morning, kept."),
        h("div", { class: "stats" }, h("span", { class: "stat" }, h("b", {}, days.length), "briefings"),
          h("span", { class: "stat" }, h("b", {}, days.reduce((n, d) => n + (d.stories ?? 0), 0)), "stories"))),
      h("div", { class: "toolbar", role: "search" }, h("label", { class: "search" }, icon(ICONS.search), search, h("span", { class: "kbd", "aria-hidden": "true" }, "/"))),
      list,
    ];
  }

  async function renderWords() {
    setNav("words");
    document.title = "TechDigest — IELTS words";
    const index = await loadIndex();
    const recent = (index.days ?? []).slice(0, 30);
    const daysData = (await Promise.allSettled(recent.map((d) => loadDay(d.date)))).filter((x) => x.status === "fulfilled").map((x) => x.value);
    const seen = new Set();
    const words = daysData.flatMap((d) => d.vocabulary ?? []).filter((w) => !seen.has(w.word.toLowerCase()) && seen.add(w.word.toLowerCase()));

    const search = h("input", { type: "search", placeholder: "Search words or meanings…", "aria-label": "Search words", autocomplete: "off" });
    const grid = h("div", { class: "cards" });
    let order = words;
    const draw = () => {
      const q = search.value.trim().toLowerCase();
      grid.replaceChildren(...order.filter((w) => !q || `${w.word} ${w.meaningEn} ${w.meaningTr}`.toLowerCase().includes(q)).map(flashcard));
    };
    search.addEventListener("input", draw);
    draw();

    const shuffle = h("button", { class: "toggle", type: "button", onclick: () => { order = [...words].sort(() => Math.random() - 0.5); transition(draw); } }, icon(ICONS.shuffle), "Shuffle");

    return [
      h("section", { class: "archive-head" }, h("div", { class: "eyebrow" }, "IELTS vocabulary"), h("h1", {}, "Your word bank."),
        h("div", { class: "stats" }, h("span", { class: "stat" }, h("b", {}, words.length), "words"),
          h("span", { class: "stat" }, h("b", {}, daysData.length), "days of news"))),
      h("div", { class: "toolbar", role: "search" }, h("label", { class: "search" }, icon(ICONS.search), search, h("span", { class: "kbd", "aria-hidden": "true" }, "/")), shuffle),
      words.length ? grid : emptyState("No words yet", "Vocabulary appears here once Gemini has summarised a day's news."),
    ];
  }

  // ---------- Yönlendirme ----------

  function transition(update) {
    if (document.startViewTransition && !reducedMotion) document.startViewTransition(update);
    else update();
  }

  async function route() {
    const path = location.hash.replace(/^#\/?/, "");
    reveal = 0;
    let nodes;
    try {
      if (path === "archive") nodes = await renderArchive();
      else if (path === "words") nodes = await renderWords();
      else if (/^\d{4}-\d{2}-\d{2}$/.test(path)) nodes = await renderDay(path);
      else if (path.startsWith("topic-") || path === "") nodes = await renderDay(null);
      else nodes = [emptyState("Page not found", "That link doesn't lead anywhere.")];
    } catch (e) {
      console.error(e);
      nodes = [emptyState("Couldn't load the briefing", "The archive is unreachable right now. Please refresh in a moment.")];
    }
    transition(() => {
      main.replaceChildren(...nodes);
      window.scrollTo(0, 0);
    });
  }

  // "/" ile aramaya odaklan, Esc ile temizle.
  document.addEventListener("keydown", (e) => {
    const input = main.querySelector(".search input");
    if (!input) return;
    if (e.key === "/" && document.activeElement !== input && !/input|textarea/i.test(document.activeElement?.tagName ?? "")) {
      e.preventDefault();
      input.focus();
    } else if (e.key === "Escape" && document.activeElement === input) {
      input.value = "";
      input.dispatchEvent(new Event("input"));
      input.blur();
    }
  });

  window.addEventListener("hashchange", route);
  route();
})();
