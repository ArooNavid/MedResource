const state = {
  mode: "login",
  user: null,
  view: "dashboard",
  notes: [],
  current: null,
  tags: [],
  tagFilter: [],
  graph: null,
  sim: null
};

const $ = (id) => document.getElementById(id);
const titles = {
  dashboard: ["امروز", "داشبورد"],
  notes: ["خزانه", "یادداشت‌ها"],
  search: ["متن کامل", "جستجو"],
  tags: ["ابر", "برچسب‌ها"],
  graph: ["پیوندها", "گراف نیرو"],
  sync: ["Obsidian", "همگام‌سازی خزانه"],
  import: ["PDF", "ورود اتمی"],
  backups: ["بایگانی", "پشتیبان‌گیری"],
  settings: ["برنامه", "تنظیمات"]
};

async function api(path, options = {}) {
  const response = await fetch(path, {
    credentials: "same-origin",
    headers: options.body && !(options.body instanceof FormData) ? { "Content-Type": "application/json" } : undefined,
    ...options,
    body: options.body && !(options.body instanceof FormData) ? JSON.stringify(options.body) : options.body
  });
  if (response.status === 401 && !path.startsWith("/api/auth/login")) {
    showAuth();
    throw new Error("نشست تمام شد.");
  }
  const text = await response.text();
  const data = text ? JSON.parse(text) : null;
  if (!response.ok) throw new Error(data?.error || data?.Error || "درخواست ناموفق بود.");
  return data;
}

function toast(message) {
  const node = $("toast");
  if (!state.user?.notificationsEnabled && state.notifications === false) return;
  node.hidden = false;
  node.textContent = message;
  clearTimeout(toast._t);
  toast._t = setTimeout(() => { node.hidden = true; }, 4200);
}

function showAuth() {
  $("auth").hidden = false;
  $("app").hidden = true;
}

function showApp() {
  $("auth").hidden = true;
  $("app").hidden = false;
  $("who-name").textContent = state.user.displayName || state.user.username;
  $("who-role").textContent = state.user.role === "Admin" ? "مدیر" : "کاربر";
  openView(state.view);
  tickClock();
}

async function tickClock() {
  if (!state.user) return;
  try {
    const clock = await api("/api/clock");
    $("clock").textContent = clock.tehranNow;
  } catch { /* clock is decorative */ }
}

function openView(name) {
  state.view = name;
  document.querySelectorAll(".side nav button").forEach((button) => {
    button.classList.toggle("active", button.dataset.view === name);
  });
  document.querySelectorAll(".view").forEach((view) => { view.hidden = true; });
  $(`view-${name}`).hidden = false;
  const [kicker, title] = titles[name];
  $("view-kicker").textContent = kicker;
  $("view-title").textContent = title;
  const loaders = { dashboard: loadDashboard, notes: loadNotes, search: renderSearch, tags: loadTags, graph: loadGraph, sync: loadSync, import: renderImport, backups: loadBackups, settings: loadSettings };
  loaders[name]();
}

function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, (ch) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[ch]));
}

function highlight(snippet) {
  return escapeHtml(snippet).replace(/&lt;b&gt;/g, "<b>").replace(/&lt;\/b&gt;/g, "</b>");
}

async function loadDashboard() {
  const data = await api("/api/dashboard");
  const today = data.today || {};
  const comparison = data.isAdmin ? `
    <div class="card" style="grid-column: 1 / -1">
      <h3>مقایسه کارکرد کاربران</h3>
      <table>
        <thead><tr><th>کاربر</th><th>نقش</th><th>PDF</th><th>یادداشت</th><th>نشست</th><th>زمان فعال (ثانیه)</th></tr></thead>
        <tbody>
          ${data.comparison.map((row) => `<tr>
            <td>${escapeHtml(row.displayName || row.username)}</td>
            <td>${row.role === "Admin" ? "مدیر" : "کاربر"}</td>
            <td>${row.totalPdfImports}</td>
            <td>${row.totalNotesCreated}</td>
            <td>${row.totalSessions}</td>
            <td>${row.totalActiveSecs}</td>
          </tr>`).join("")}
        </tbody>
      </table>
    </div>` : `<div class="card" style="grid-column: 1 / -1"><p class="muted">آمار کارکرد دیگران فقط برای مدیر قابل دیدن است.</p></div>`;
  $("view-dashboard").innerHTML = `
    <div class="grid stats">
      <article class="card"><span>یادداشت امروز</span><strong>${today.noteCreateCount ?? 0}</strong></article>
      <article class="card"><span>PDF امروز</span><strong>${today.pdfImportCount ?? 0}</strong></article>
      <article class="card"><span>نشست‌ها</span><strong>${today.sessionCount ?? 0}</strong></article>
      <article class="card"><span>تاریخ تهران</span><strong style="font-size:18px">${escapeHtml(data.tehranDate)}</strong></article>
      ${comparison}
      <div class="card" style="grid-column: 1 / -1">
        <h3>یادداشت‌های تازه</h3>
        <div class="list" style="margin-top:10px">
          ${(data.recent || []).map((note) => `<button class="note-item" data-open="${note.id}">${escapeHtml(note.title)}</button>`).join("") || `<p class="muted">هنوز یادداشتی نیست.</p>`}
        </div>
      </div>
    </div>`;
  $("view-dashboard").querySelectorAll("[data-open]").forEach((button) => {
    button.onclick = () => { openView("notes"); setTimeout(() => openNote(button.dataset.open), 30); };
  });
}

async function loadNotes() {
  const query = state.tagFilter.length ? `?tagIds=${state.tagFilter.join(",")}` : "";
  state.notes = await api("/api/notes" + query);
  const currentId = state.current?.note?.id;
  if (currentId && state.notes.some((note) => note.id === currentId)) await openNote(currentId);
  else {
    state.current = null;
    renderNotes();
  }
}

function renderNotes() {
  const filter = ($("note-filter")?.value || "").trim();
  const visible = state.notes.filter((note) => !filter || note.title.includes(filter));
  $("view-notes").innerHTML = `
    <div class="split">
      <div>
        <div class="row" style="margin-bottom:8px">
          <input id="note-filter" placeholder="فیلتر عنوان" value="${escapeHtml(filter)}" />
          <button class="primary" id="new-note">جدید</button>
        </div>
        <div class="list" id="note-list">
          ${visible.map((note) => `<button class="note-item ${state.current?.note.id === note.id ? "active" : ""}" data-id="${note.id}">
            <strong>${escapeHtml(note.title)}</strong>
            <div class="muted">عمق ${note.depth}</div>
          </button>`).join("") || `<p class="muted">یادداشتی با این فیلتر نیست.</p>`}
        </div>
      </div>
      <form id="editor" class="card">
        <div class="editor-head"><h3>ویرایش</h3><div class="row">
          <button class="ghost" type="button" id="export-pdf" ${state.current ? "" : "disabled"}>PDF</button>
          <button class="danger" type="button" id="delete-note" ${state.current ? "" : "disabled"}>حذف</button>
        </div></div>
        <label>عنوان<input id="note-title" value="${escapeHtml(state.current?.note.title || "")}" /></label>
        <label>برچسب‌ها، با ویرگول<input id="note-tags" value="${escapeHtml((state.current?.tags || []).map((tag) => tag.name).join("، "))}" /></label>
        <label>متن مارک‌داون<textarea id="note-content">${escapeHtml(state.current?.note.content || "")}</textarea></label>
        <button class="primary" type="submit">ذخیره</button>
      </form>
      <aside class="card" id="links-panel">${linksHtml()}</aside>
    </div>`;
  $("note-filter").oninput = () => renderNotes();
  $("new-note").onclick = () => { state.current = { note: { title: "", content: "" }, tags: [], outgoing: [], backlinks: [] }; renderNotes(); };
  $("view-notes").querySelectorAll("[data-id]").forEach((button) => { button.onclick = () => openNote(button.dataset.id); });
  $("editor").onsubmit = saveNote;
  $("delete-note").onclick = deleteNote;
  $("export-pdf").onclick = exportPdf;
  $("links-panel").querySelectorAll("[data-go]").forEach((button) => { button.onclick = () => openNote(button.dataset.go); });
}

function linksHtml() {
  const outgoing = state.current?.outgoing || [];
  const backlinks = state.current?.backlinks || [];
  const row = (item, id, title, resolved) => `<button class="note-item link-row" ${id ? `data-go="${id}"` : "disabled"}>
    <span class="${resolved ? "" : "dangling"}">${escapeHtml(title)}</span>
    <span class="dot" style="background:${resolved ? "var(--teal)" : "#b7b0a4"}"></span>
  </button>`;
  return `
    <h3>پیوندها</h3>
    <div class="list" style="margin:8px 0 14px">
      ${outgoing.map((link) => row(link, link.targetNoteId, link.targetTitle, link.isResolved)).join("") || `<p class="muted">پیوند خروجی نیست.</p>`}
    </div>
    <h3>بک‌لینک‌ها <span class="pill">${backlinks.length}</span></h3>
    <div class="list" style="margin-top:8px">
      ${backlinks.map((link) => row(link, link.sourceNoteId, link.sourceTitle, true)).join("") || `<p class="muted">هنوز کسی به اینجا پیوند نداده.</p>`}
    </div>`;
}

async function openNote(id) {
  state.current = await api(`/api/notes/${id}`);
  if (state.view !== "notes") openView("notes");
  else renderNotes();
}

async function saveNote(event) {
  event.preventDefault();
  const body = {
    title: $("note-title").value,
    content: $("note-content").value,
    tags: $("note-tags").value.split(/[,،]/).map((item) => item.trim()).filter(Boolean)
  };
  const saved = state.current?.note?.id
    ? await api(`/api/notes/${state.current.note.id}`, { method: "PUT", body })
    : await api("/api/notes", { method: "POST", body });
  toast("یادداشت ذخیره شد.");
  await loadNotes();
  await openNote(saved.id);
}

async function deleteNote() {
  if (!state.current?.note?.id) return;
  await api(`/api/notes/${state.current.note.id}`, { method: "DELETE" });
  state.current = null;
  toast("یادداشت حذف شد.");
  await loadNotes();
}

function exportPdf() {
  if (!state.current?.note?.id) return;
  window.location = `/api/notes/${state.current.note.id}/pdf`;
}

let searchTimer;
function renderSearch() {
  $("view-search").innerHTML = `
    <div class="card">
      <input id="search-box" placeholder="جستجو در یادداشت‌ها…" />
      <p id="search-status" class="muted" style="margin:8px 0"></p>
      <div id="search-results" class="list"></div>
    </div>`;
  $("search-box").oninput = () => {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(runSearch, 250);
  };
}

async function runSearch() {
  const q = $("search-box").value;
  if (!q.trim()) {
    $("search-results").innerHTML = "";
    $("search-status").textContent = "";
    return;
  }
  const results = await api(`/api/search?q=${encodeURIComponent(q)}`);
  $("search-status").textContent = results.length === 0 ? "نتیجه‌ای یافت نشد." : results.length === 1 ? "۱ نتیجه" : `${results.length} نتیجه`;
  $("search-results").innerHTML = results.map((item) => `
    <button class="result" data-open="${item.noteId}">
      <strong>${escapeHtml(item.title)}</strong>
      <div class="snippet">${highlight(item.snippet || "")}</div>
    </button>`).join("");
  $("search-results").querySelectorAll("[data-open]").forEach((button) => { button.onclick = () => openNote(button.dataset.open); });
}

async function loadTags() {
  state.tags = await api("/api/tags");
  $("view-tags").innerHTML = `
    <div class="card">
      <div class="editor-head">
        <h3>ابر برچسب</h3>
        <button class="ghost" id="clear-tags" ${state.tagFilter.length ? "" : "hidden"}>پاک کردن فیلتر</button>
      </div>
      <div class="chips" style="margin-top:12px">
        ${state.tags.map((tag) => `<span class="chip" style="border-width:${state.tagFilter.includes(tag.id) ? 2 : 1}px; background:${tag.colorHex}26">
          <button data-tag="${tag.id}" style="border:0;background:transparent"><span class="dot" style="background:${tag.colorHex}"></span> ${escapeHtml(tag.name)} (${tag.noteCount})</button>
          <button class="ghost" data-del="${tag.id}">✕</button>
        </span>`).join("") || `<p class="muted">هنوز برچسبی نیست. در یادداشت اضافه کنید.</p>`}
      </div>
    </div>`;
  $("clear-tags")?.addEventListener("click", () => { state.tagFilter = []; loadTags(); });
  $("view-tags").querySelectorAll("[data-tag]").forEach((button) => {
    button.onclick = () => {
      const id = Number(button.dataset.tag);
      state.tagFilter = state.tagFilter.includes(id) ? state.tagFilter.filter((item) => item !== id) : [...state.tagFilter, id];
      loadTags();
    };
  });
  $("view-tags").querySelectorAll("[data-del]").forEach((button) => {
    button.onclick = async () => {
      await api(`/api/tags/${button.dataset.del}`, { method: "DELETE" });
      toast("برچسب حذف شد.");
      loadTags();
    };
  });
}

async function loadGraph() {
  const data = await api("/api/graph");
  state.graph = {
    nodes: data.nodes.map((node) => ({ ...node, vx: 0, vy: 0 })),
    edges: data.edges
  };
  $("view-graph").innerHTML = `
    <div class="row" style="margin-bottom:8px">
      <input id="graph-search" placeholder="جستجو در گراف…" />
      <button class="ghost" id="graph-labels">Aa</button>
      <button class="ghost" id="graph-play">⏸</button>
      <button class="ghost" id="graph-reload">↺</button>
      <button class="ghost" id="graph-center">⊡</button>
    </div>
    <canvas id="graph-canvas"></canvas>
    ${data.nodes.length === 0 ? `<p class="muted">یادداشتی نیست. با [[پیوند]] گراف ساخته می‌شود.</p>` : ""}`;
  $("graph-reload").onclick = loadGraph;
  $("graph-labels").onclick = () => { state.showLabels = state.showLabels === false; drawGraph(); };
  $("graph-play").onclick = () => {
    state.simRunning = state.simRunning === false;
    $("graph-play").textContent = state.simRunning === false ? "▶" : "⏸";
  };
  $("graph-search").oninput = drawGraph;
  $("graph-center").onclick = centerGraph;
  const canvas = $("graph-canvas");
  const rect = canvas.getBoundingClientRect();
  canvas.width = rect.width * devicePixelRatio;
  canvas.height = rect.height * devicePixelRatio;
  state.pan = { x: canvas.width / 2, y: canvas.height / 2 };
  state.scale = 1;
  state.simRunning = true;
  state.showLabels = true;
  bindGraph(canvas);
  if (state.sim) cancelAnimationFrame(state.sim);
  const loop = () => {
    if (state.view === "graph" && state.simRunning !== false) stepGraph();
    if (state.view === "graph") drawGraph();
    state.sim = requestAnimationFrame(loop);
  };
  loop();
}

function stepGraph() {
  const nodes = state.graph.nodes;
  const edges = state.graph.edges;
  const byId = Object.fromEntries(nodes.map((node) => [node.noteId, node]));
  nodes.forEach((node) => { node.fx = 0; node.fy = 0; });
  for (let i = 0; i < nodes.length; i++) {
    for (let j = i + 1; j < nodes.length; j++) {
      let dx = nodes[i].x - nodes[j].x;
      let dy = nodes[i].y - nodes[j].y;
      let distSq = dx * dx + dy * dy || 0.01;
      const dist = Math.sqrt(distSq);
      const force = 9000 / distSq;
      const fx = force * dx / dist;
      const fy = force * dy / dist;
      nodes[i].fx += fx; nodes[i].fy += fy;
      nodes[j].fx -= fx; nodes[j].fy -= fy;
    }
  }
  edges.forEach((edge) => {
    const a = byId[edge.sourceNoteId];
    const b = byId[edge.targetNoteId];
    if (!a || !b) return;
    const dx = b.x - a.x;
    const dy = b.y - a.y;
    const dist = Math.sqrt(dx * dx + dy * dy) || 0.001;
    const force = 0.06 * (dist - 160);
    a.fx += force * dx / dist; a.fy += force * dy / dist;
    b.fx -= force * dx / dist; b.fy -= force * dy / dist;
  });
  nodes.forEach((node) => {
    if (node.pinned) return;
    node.vx = (node.vx + node.fx * 0.016) * 0.82;
    node.vy = (node.vy + node.fy * 0.016) * 0.82;
    const speed = Math.hypot(node.vx, node.vy);
    if (speed > 24) { node.vx = node.vx / speed * 24; node.vy = node.vy / speed * 24; }
    node.x += node.vx * 0.016;
    node.y += node.vy * 0.016;
  });
}

function drawGraph() {
  const canvas = $("graph-canvas");
  if (!canvas) return;
  const ctx = canvas.getContext("2d");
  const query = ($("graph-search")?.value || "").trim();
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  ctx.fillStyle = "#102421";
  ctx.fillRect(0, 0, canvas.width, canvas.height);
  const byId = Object.fromEntries(state.graph.nodes.map((node) => [node.noteId, node]));
  const project = (node) => ({
    x: state.pan.x + node.x * state.scale * devicePixelRatio,
    y: state.pan.y + node.y * state.scale * devicePixelRatio
  });
  ctx.strokeStyle = "rgba(232, 214, 176, 0.45)";
  ctx.lineWidth = devicePixelRatio;
  state.graph.edges.forEach((edge) => {
    const a = byId[edge.sourceNoteId];
    const b = byId[edge.targetNoteId];
    if (!a || !b) return;
    const pa = project(a); const pb = project(b);
    ctx.beginPath(); ctx.moveTo(pa.x, pa.y); ctx.lineTo(pb.x, pb.y); ctx.stroke();
  });
  state.graph.nodes.forEach((node) => {
    const p = project(node);
    const hit = query && node.title.toLowerCase().includes(query.toLowerCase());
    ctx.beginPath();
    ctx.fillStyle = node.selected ? "#e7b255" : hit ? "#f2d48a" : "#2f8f84";
    ctx.globalAlpha = hit || node.selected ? 1 : 0.8;
    ctx.arc(p.x, p.y, node.radius * devicePixelRatio * 0.45, 0, Math.PI * 2);
    ctx.fill();
    ctx.globalAlpha = 1;
    if (state.showLabels !== false) {
      const label = node.title.length > 22 ? node.title.slice(0, 20) + "…" : node.title;
      ctx.fillStyle = "#f6f1e6";
      ctx.font = `${12 * devicePixelRatio}px Vazirmatn, Tahoma`;
      ctx.textAlign = "center";
      ctx.fillText(label, p.x, p.y - node.radius * devicePixelRatio * 0.7);
    }
    node._p = p;
  });
}

function centerGraph() {
  const canvas = $("graph-canvas");
  const nodes = state.graph.nodes;
  if (!nodes.length) return;
  const cx = nodes.reduce((sum, node) => sum + node.x, 0) / nodes.length;
  const cy = nodes.reduce((sum, node) => sum + node.y, 0) / nodes.length;
  state.pan = { x: canvas.width / 2 - cx * state.scale * devicePixelRatio, y: canvas.height / 2 - cy * state.scale * devicePixelRatio };
}

function bindGraph(canvas) {
  let drag = null;
  canvas.onpointerdown = (event) => {
    const node = hitNode(event);
    drag = { x: event.offsetX, y: event.offsetY, node, moved: false };
    if (node) node.pinned = true;
    canvas.setPointerCapture(event.pointerId);
  };
  canvas.onpointermove = (event) => {
    if (!drag) return;
    const dx = event.offsetX - drag.x;
    const dy = event.offsetY - drag.y;
    if (Math.hypot(dx, dy) > 4) drag.moved = true;
    if (drag.node && drag.moved) {
      drag.node.x += dx / state.scale;
      drag.node.y += dy / state.scale;
      drag.x = event.offsetX; drag.y = event.offsetY;
    } else if (!drag.node) {
      state.pan.x += dx * devicePixelRatio;
      state.pan.y += dy * devicePixelRatio;
      drag.x = event.offsetX; drag.y = event.offsetY;
    }
  };
  canvas.onpointerup = (event) => {
    if (drag?.node && !drag.moved) {
      state.graph.nodes.forEach((node) => { node.selected = false; });
      drag.node.selected = true;
      drag.node.pinned = false;
      openNote(drag.node.noteId);
    }
    drag = null;
  };
  canvas.onwheel = (event) => {
    event.preventDefault();
    const factor = event.deltaY < 0 ? 1.12 : 1 / 1.12;
    state.scale = Math.min(3, Math.max(0.15, state.scale * factor));
  };
}

function hitNode(event) {
  const x = event.offsetX * devicePixelRatio;
  const y = event.offsetY * devicePixelRatio;
  return state.graph.nodes.find((node) => node._p && Math.hypot(node._p.x - x, node._p.y - y) < node.radius * devicePixelRatio * 0.7);
}

async function loadSync() {
  const report = await api("/api/sync");
  const lines = (report?.messages || []).map((line) => `<li>${escapeHtml(line)}</li>`).join("");
  $("view-sync").innerHTML = `
    <div class="card" style="max-width:720px">
      <h3>همگام‌سازی با Obsidian</h3>
      <p class="muted" style="margin:8px 0 14px">فایل‌های مارک‌داون خزانه و پایگاه‌داده دو طرفه هم‌خوان می‌شوند. برچسب‌ها و تاریخ‌ها در frontmatter فایل می‌مانند.</p>
      <div class="grid stats">
        <article class="card"><span>از فایل</span><strong>${report?.pulled ?? 0}</strong></article>
        <article class="card"><span>به فایل</span><strong>${report?.pushed ?? 0}</strong></article>
        <article class="card"><span>حذف</span><strong>${report?.deleted ?? 0}</strong></article>
        <article class="card"><span>تعارض</span><strong>${report?.conflicts ?? 0}</strong></article>
      </div>
      <p class="muted" style="margin-top:12px">${report ? `آخرین اجرا: ${escapeHtml(report.syncedAtUtc)} · بدون تغییر ${report.unchanged}` : "هنوز همگام‌سازی نشده است."}</p>
      <div class="row" style="margin-top:12px"><button class="primary" id="sync-now">همگام‌سازی اکنون</button></div>
      <ul>${lines}</ul>
    </div>`;
  $("sync-now").onclick = async () => {
    toast("در حال همگام‌سازی…");
    await api("/api/sync", { method: "POST" });
    toast("همگام‌سازی انجام شد.");
    loadSync();
  };
}

function renderImport() {
  $("view-import").innerHTML = `
    <div class="card">
      <h3>ورود PDF به خزانه</h3>
      <p class="muted" style="margin:8px 0 14px">فایل در پوشهٔ staging نوشته می‌شود، سپس با وضعیت Committed به خزانه منتقل می‌شود. عمق درخت از ۱۵ بیشتر نمی‌شود. فایل تکراری با درهم‌ساز SHA-256 رد می‌شود.</p>
      <input id="pdf-files" type="file" accept="application/pdf,.pdf" multiple />
      <div class="row" style="margin-top:12px"><button class="primary" id="import-go">شروع ورود</button></div>
      <div id="import-results" class="list" style="margin-top:12px"></div>
    </div>`;
  $("import-go").onclick = async () => {
    const files = $("pdf-files").files;
    if (!files.length) return toast("فایلی انتخاب نشده.");
    const body = new FormData();
    [...files].forEach((file) => body.append("files", file));
    const outcomes = await api("/api/import", { method: "POST", body });
    $("import-results").innerHTML = outcomes.map((item) => `<article class="note-item">
      <strong>${item.duplicate ? "تکراری" : item.success ? "Committed" : "Failed"}</strong>
      <div class="muted">${escapeHtml(item.error || `${item.notesCreated} یادداشت`)}</div>
    </article>`).join("");
    if (outcomes.some((item) => item.success && !item.duplicate)) toast("ورود انجام شد.");
  };
}

async function loadBackups() {
  const backups = await api("/api/backups");
  $("view-backups").innerHTML = `
    <div class="card">
      <div class="editor-head">
        <h3>پشتیبان‌های موجود</h3>
        <div class="row">
          <button class="ghost" id="reload-backups">بارگذاری مجدد</button>
          <button class="primary" id="create-backup">ایجاد پشتیبان</button>
        </div>
      </div>
      <div class="list" style="margin-top:12px">
        ${backups.map((item) => `<article class="backup">
          <strong>${escapeHtml(item.fileName)}</strong>
          <div class="muted">${escapeHtml(item.createdAt)} · ${escapeHtml(item.displaySize)}</div>
          <div class="row" style="margin-top:8px">
            <button class="ghost" data-restore="${escapeHtml(item.fileName)}">بازیابی</button>
            <button class="danger" data-delete="${escapeHtml(item.fileName)}">حذف</button>
          </div>
        </article>`).join("") || `<p class="muted">هیچ پشتیبانی یافت نشد.</p>`}
      </div>
    </div>`;
  $("reload-backups").onclick = loadBackups;
  $("create-backup").onclick = async () => {
    toast("در حال ایجاد پشتیبان…");
    const result = await api("/api/backups", { method: "POST" });
    toast("پشتیبان ساخته شد.");
    loadBackups();
    return result;
  };
  $("view-backups").querySelectorAll("[data-restore]").forEach((button) => {
    button.onclick = async () => {
      await api("/api/backups/restore", { method: "POST", body: { filePath: button.dataset.restore } });
      toast("بازیابی انجام شد. برنامه را دوباره اجرا کنید.");
    };
  });
  $("view-backups").querySelectorAll("[data-delete]").forEach((button) => {
    button.onclick = async () => {
      await api(`/api/backups?file=${encodeURIComponent(button.dataset.delete)}`, { method: "DELETE" });
      toast("پشتیبان حذف شد.");
      loadBackups();
    };
  });
}

async function loadSettings() {
  const data = await api("/api/settings");
  state.notifications = data.notificationsEnabled;
  $("view-settings").innerHTML = `
    <form id="settings-form" class="card" style="display:grid; gap:12px; max-width:640px">
      <label>مسیر خزانه<input name="vaultPath" value="${escapeHtml(data.vaultPath)}" required /></label>
      <label>مسیر پشتیبان<input name="backupPath" value="${escapeHtml(data.backupPath)}" /></label>
      <label>بازهٔ پشتیبان خودکار (ساعت)<input name="backupIntervalHours" type="number" min="1" max="168" value="${data.backupIntervalHours}" /></label>
      <label>پوسته
        <select name="theme">
          ${["System", "Light", "Dark"].map((theme) => `<option ${theme === data.theme ? "selected" : ""}>${theme}</option>`).join("")}
        </select>
      </label>
      <label class="row"><input name="notificationsEnabled" type="checkbox" ${data.notificationsEnabled ? "checked" : ""} /> اعلان‌ها</label>
      <p class="muted">پایگاه‌داده: ${escapeHtml(data.databasePath)}<br>فایل تنظیمات: ${escapeHtml(data.settingsFilePath)}</p>
      <p id="settings-error" class="error" hidden></p>
      <div class="row"><button class="primary" type="submit">ذخیره</button></div>
    </form>`;
  applyTheme(data.theme);
  $("settings-form").onsubmit = async (event) => {
    event.preventDefault();
    const form = new FormData(event.target);
    try {
      const result = await api("/api/settings", {
        method: "PUT",
        body: {
          vaultPath: form.get("vaultPath"),
          backupPath: form.get("backupPath"),
          backupIntervalHours: Number(form.get("backupIntervalHours")),
          theme: form.get("theme"),
          notificationsEnabled: form.get("notificationsEnabled") === "on"
        }
      });
      state.notifications = form.get("notificationsEnabled") === "on";
      applyTheme(form.get("theme"));
      toast(result.message);
    } catch (error) {
      $("settings-error").hidden = false;
      $("settings-error").textContent = error.message;
    }
  };
}

function applyTheme(theme) {
  const dark = theme === "Dark" || (theme === "System" && matchMedia("(prefers-color-scheme: dark)").matches);
  document.documentElement.style.setProperty("--paper", dark ? "#121816" : "#f4efe4");
  document.documentElement.style.setProperty("--card", dark ? "#1c2622" : "#fffaf2");
  document.documentElement.style.setProperty("--ink", dark ? "#f4efe4" : "#17211e");
  document.documentElement.style.setProperty("--line", dark ? "#31403b" : "#e2d8c6");
}

$("auth-toggle").onclick = () => {
  state.mode = state.mode === "login" ? "register" : "login";
  $("display-wrap").hidden = state.mode === "login";
  $("auth-submit").textContent = state.mode === "login" ? "ورود" : "ثبت‌نام";
  $("auth-toggle").textContent = state.mode === "login" ? "حساب تازه" : "حساب دارم";
};

$("auth-form").onsubmit = async (event) => {
  event.preventDefault();
  $("auth-error").hidden = true;
  try {
    const path = state.mode === "login" ? "/api/auth/login" : "/api/auth/register";
    state.user = await api(path, {
      method: "POST",
      body: { username: $("username").value, password: $("password").value, displayName: $("display-name").value }
    });
    showApp();
  } catch (error) {
    $("auth-error").hidden = false;
    $("auth-error").textContent = error.message;
  }
};

document.querySelectorAll(".side nav button").forEach((button) => {
  button.onclick = () => openView(button.dataset.view);
});
$("logout").onclick = async () => {
  await api("/api/auth/logout", { method: "POST" });
  state.user = null;
  state.view = "dashboard";
  state.current = null;
  showAuth();
};

setInterval(tickClock, 1000);
api("/api/auth/me").then((user) => { state.user = user; showApp(); }).catch(() => showAuth());
