const state = {
  mode: "login",
  user: null,
  view: "dashboard",
  notes: [],
  templates: [],
  current: null,
  tags: [],
  tagFilter: [],
  graph: null,
  sim: null,
  journal: null,
  taskFilter: "open",
  showPreview: false,
  noteSort: "updated",
  noteOrder: "desc"
};

let previewTimer;

const $ = (id) => document.getElementById(id);
const titles = {
  dashboard: ["امروز", "داشبورد"],
  notes: ["خزانه", "یادداشت‌ها"],
  trash: ["بازیابی", "سطل زباله"],
  templates: ["الگو", "قالب‌ها"],
  journal: ["تهران", "دفتر روزانه"],
  tasks: ["چک‌لیست", "کارها"],
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
  const loaders = { dashboard: loadDashboard, notes: loadNotes, trash: loadTrash, templates: loadTemplates, journal: loadJournal, tasks: loadTasks, search: renderSearch, tags: loadTags, graph: loadGraph, sync: loadSync, import: renderImport, backups: loadBackups, settings: loadSettings };
  loaders[name]();
}

function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, (ch) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[ch]));
}

function formatIsoTehran(iso) {
  if (!iso) return "—";
  try {
    return new Date(iso).toLocaleString("fa-IR", { timeZone: "Asia/Tehran" });
  } catch {
    return iso;
  }
}

function wordCount(text) {
  return (text || "").trim().split(/\s+/).filter(Boolean).length;
}

function highlight(snippet) {
  return escapeHtml(snippet).replace(/&lt;b&gt;/g, "<b>").replace(/&lt;\/b&gt;/g, "</b>");
}

async function loadDashboard() {
  const data = await api("/api/dashboard");
  const tasks = await api("/api/tasks?open=true").catch(() => ({ openCount: 0, doneCount: 0 }));
  const trash = await api("/api/trash").catch(() => []);
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
      <article class="card"><span>کار باز</span><strong>${tasks.openCount ?? 0}</strong></article>
      <article class="card"><span>سطل زباله</span><strong>${trash.length ?? 0}</strong></article>
      <article class="card"><span>تاریخ تهران</span><strong style="font-size:18px">${escapeHtml(data.tehranDate)}</strong></article>
      ${comparison}
      <div class="card" style="grid-column: 1 / -1">
        <h3>سنجاق‌شده</h3>
        <div class="list" style="margin-top:10px">
          ${(data.pinned || []).map((note) => `<button class="note-item" data-open="${note.id}"><span class="pill">سنجاق</span> ${escapeHtml(note.title)}</button>`).join("") || `<p class="muted">یادداشت سنجاق‌شده‌ای نیست.</p>`}
        </div>
      </div>
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
  const params = new URLSearchParams();
  if (state.tagFilter.length) params.set("tagIds", state.tagFilter.join(","));
  if (state.noteSort) params.set("sort", state.noteSort);
  if (state.noteOrder) params.set("order", state.noteOrder);
  const query = params.toString() ? `?${params.toString()}` : "";
  state.notes = await api("/api/notes" + query);
  state.templates = await api("/api/templates");
  const currentId = state.current?.note?.id;
  if (currentId && state.notes.some((note) => note.id === currentId)) await openNote(currentId);
  else {
    state.current = null;
    renderNotes();
  }
}

async function refreshPreview() {
  const panel = $("note-preview");
  if (!state.showPreview || !panel) return;
  const content = $("note-content")?.value ?? "";
  const noteId = state.current?.note?.id ?? null;
  try {
    const data = await api("/api/markdown/preview", { method: "POST", body: { content, noteId } });
    panel.innerHTML = data.html || "";
    panel.querySelectorAll('a[href^="#note-"]').forEach((link) => {
      link.onclick = (event) => {
        event.preventDefault();
        openNote(link.getAttribute("href").slice(6));
      };
    });
  } catch {
    panel.innerHTML = `<p class="muted">پیش‌نمایش در دسترس نیست.</p>`;
  }
}

function schedulePreview() {
  clearTimeout(previewTimer);
  previewTimer = setTimeout(() => { refreshPreview(); }, 400);
}

function renderNotes() {
  const filter = ($("note-filter")?.value || "").trim();
  const visible = state.notes.filter((note) => !filter || note.title.includes(filter));
  $("view-notes").innerHTML = `
    <div class="split">
      <div>
        <div class="row" style="margin-bottom:8px;flex-wrap:wrap;gap:8px">
          <input id="note-filter" placeholder="فیلتر عنوان" value="${escapeHtml(filter)}" />
          <select id="note-sort" aria-label="مرتب‌سازی">
            <option value="updated:desc" ${state.noteSort === "updated" && state.noteOrder === "desc" ? "selected" : ""}>تازه‌ترین</option>
            <option value="updated:asc" ${state.noteSort === "updated" && state.noteOrder === "asc" ? "selected" : ""}>قدیمی‌ترین</option>
            <option value="title:asc" ${state.noteSort === "title" && state.noteOrder === "asc" ? "selected" : ""}>عنوان الفبا</option>
            <option value="title:desc" ${state.noteSort === "title" && state.noteOrder === "desc" ? "selected" : ""}>عنوان معکوس</option>
            <option value="created:desc" ${state.noteSort === "created" && state.noteOrder === "desc" ? "selected" : ""}>تاریخ ساخت</option>
            <option value="depth:asc" ${state.noteSort === "depth" && state.noteOrder === "asc" ? "selected" : ""}>عمق درخت</option>
          </select>
          <button class="primary" id="new-note">جدید</button>
          <button class="ghost" type="button" id="import-md">ورود .md</button>
          <input id="import-md-file" type="file" accept=".md,text/markdown" hidden />
        </div>
        <div class="list" id="note-list">
          ${visible.map((note) => `<button class="note-item ${state.current?.note.id === note.id ? "active" : ""}" data-id="${note.id}" style="padding-right:${8 + (note.depth - 1) * 14}px">
            <strong>${note.pinned ? `<span class="pill">سنجاق</span> ` : ""}${escapeHtml(note.title)}</strong>
            <div class="muted">عمق ${note.depth}</div>
          </button>`).join("") || `<p class="muted">یادداشتی با این فیلتر نیست.</p>`}
        </div>
      </div>
      <form id="editor" class="card">
        <div class="editor-head"><h3>ویرایش</h3><div class="row">
          <select id="template-pick" style="max-width:160px" ${state.current?.note?.id ? "" : "disabled"}>
            <option value="">قالب…</option>
            ${(state.templates || []).map((template) => `<option value="${escapeHtml(template.name)}">${escapeHtml(template.title)}</option>`).join("")}
          </select>
          <button class="ghost" type="button" id="apply-template" ${state.current?.note?.id ? "" : "disabled"}>درج قالب</button>
          <button class="ghost" type="button" id="duplicate-note" ${state.current?.note?.id ? "" : "disabled"}>رونوشت</button>
          <button class="ghost" type="button" id="toggle-pin" ${state.current?.note?.id ? "" : "disabled"}>${state.current?.note?.pinned ? "برداشتن سنجاق" : "سنجاق"}</button>
          <button class="ghost" type="button" id="toggle-preview" ${state.current ? "" : "disabled"}>${state.showPreview ? "ویرایش" : "پیش‌نمایش"}</button>
          <button class="ghost" type="button" id="sync-path" ${state.current?.note?.id ? "" : "disabled"}>هم‌نام فایل</button>
          <button class="ghost" type="button" id="export-md" ${state.current?.note?.id ? "" : "disabled"}>فایل .md</button>
          <button class="ghost" type="button" id="export-pdf" ${state.current ? "" : "disabled"}>PDF</button>
          <button class="danger" type="button" id="delete-note" ${state.current ? "" : "disabled"}>حذف</button>
        </div></div>
        <label>عنوان<input id="note-title" value="${escapeHtml(state.current?.note.title || "")}" /></label>
        <label>برچسب‌ها، با ویرگول<input id="note-tags" value="${escapeHtml((state.current?.tags || []).map((tag) => tag.name).join("، "))}" /></label>
        <label>نام‌های مستعار برای [[پیوند]]<input id="note-aliases" placeholder="مثلاً: نام کوتاه، عنوان قدیم" value="${escapeHtml((state.current?.aliases || []).join("، "))}" /></label>
        <label>والد در درخت
          <select id="note-parent" ${state.current?.note?.id ? "" : "disabled"}>
            <option value="">— ریشه —</option>
            ${(state.notes || []).filter((note) => note.id !== state.current?.note?.id).map((note) => `<option value="${note.id}" ${state.current?.note?.parentNoteId === note.id ? "selected" : ""}>${"·".repeat(Math.max(0, note.depth - 1))} ${escapeHtml(note.title)}</option>`).join("")}
          </select>
        </label>
        <label>متن مارک‌داون
          <div class="editor-split ${state.showPreview ? "preview-on" : ""}">
            <textarea id="note-content">${escapeHtml(state.current?.note.content || "")}</textarea>
            <div id="note-preview" class="markdown-preview" ${state.showPreview ? "" : "hidden"}></div>
          </div>
        </label>
        <button class="primary" type="submit">ذخیره</button>
      </form>
      <aside class="card" id="links-panel">${linksHtml()}</aside>
    </div>`;
  $("note-filter").oninput = () => renderNotes();
  $("note-sort").onchange = async () => {
    const [sort, order] = ($("note-sort").value || "updated:desc").split(":");
    state.noteSort = sort;
    state.noteOrder = order || "desc";
    await loadNotes();
  };
  $("note-parent")?.addEventListener("change", async () => {
    if (!state.current?.note?.id) return;
    const parentNoteId = $("note-parent").value ? Number($("note-parent").value) : null;
    try {
      const saved = await api(`/api/notes/${state.current.note.id}/parent`, { method: "PUT", body: { parentNoteId } });
      toast("محل در درخت به‌روز شد.");
      state.current.note = saved;
      await loadNotes();
    } catch (error) {
      toast(error.message);
    }
  });
  $("new-note").onclick = () => { state.current = { note: { title: "", content: "" }, tags: [], aliases: [], outgoing: [], backlinks: [] }; renderNotes(); };
  $("view-notes").querySelectorAll("[data-id]").forEach((button) => { button.onclick = () => openNote(button.dataset.id); });
  $("editor").onsubmit = saveNote;
  $("apply-template").onclick = applyTemplate;
  $("duplicate-note").onclick = async () => {
    if (!state.current?.note?.id) return;
    try {
      const copy = await api(`/api/notes/${state.current.note.id}/duplicate`, { method: "POST" });
      toast("رونوشت ساخته شد.");
      await loadNotes();
      await openNote(copy.id);
    } catch (error) {
      toast(error.message);
    }
  };
  $("toggle-pin").onclick = async () => {
    if (!state.current?.note?.id) return;
    const pinned = !state.current.note.pinned;
    const saved = await api(`/api/notes/${state.current.note.id}/pin`, { method: "PUT", body: { pinned } });
    toast(pinned ? "یادداشت سنجاق شد." : "سنجاق برداشته شد.");
    state.current.note = saved;
    await loadNotes();
    await openNote(saved.id);
  };
  $("toggle-preview").onclick = () => { state.showPreview = !state.showPreview; renderNotes(); };
  $("note-content").oninput = schedulePreview;
  if (state.showPreview) refreshPreview();
  $("delete-note").onclick = deleteNote;
  $("sync-path").onclick = async () => {
    if (!state.current?.note?.id) return;
    try {
      const saved = await api(`/api/notes/${state.current.note.id}/sync-path`, { method: "POST" });
      toast("نام فایل با عنوان هم‌خوان شد.");
      state.current.note = saved;
      await loadNotes();
      await openNote(saved.id);
    } catch (error) {
      toast(error.message);
    }
  };
  $("export-md").onclick = exportMarkdown;
  $("export-pdf").onclick = exportPdf;
  $("import-md").onclick = () => $("import-md-file").click();
  $("import-md-file").onchange = async () => {
    const file = $("import-md-file").files?.[0];
    if (!file) return;
    const body = new FormData();
    body.append("file", file);
    try {
      const note = await api("/api/notes/import-markdown", { method: "POST", body });
      toast("فایل مارک‌داون وارد شد.");
      await loadNotes();
      await openNote(note.id);
    } catch (error) {
      toast(error.message);
    } finally {
      $("import-md-file").value = "";
    }
  };
  $("links-panel").querySelectorAll("[data-go]").forEach((button) => { button.onclick = () => openNote(button.dataset.go); });
}

function linksHtml() {
  const outgoing = state.current?.outgoing || [];
  const backlinks = state.current?.backlinks || [];
  const row = (item, id, title, resolved) => `<button class="note-item link-row" ${id ? `data-go="${id}"` : "disabled"}>
    <span class="${resolved ? "" : "dangling"}">${escapeHtml(title)}</span>
    <span class="dot" style="background:${resolved ? "var(--teal)" : "#b7b0a4"}"></span>
  </button>`;
  const note = state.current?.note;
  const meta = note ? `
    <h3>اطلاعات</h3>
    <dl class="note-meta">
      <div><dt>مسیر</dt><dd dir="ltr">${escapeHtml(note.relPath || "—")}</dd></div>
      <div><dt>ساخته</dt><dd>${escapeHtml(formatIsoTehran(note.createdAt))}</dd></div>
      <div><dt>ویرایش</dt><dd>${escapeHtml(formatIsoTehran(note.updatedAt))}</dd></div>
      <div><dt>کلمات</dt><dd>${wordCount(note.content)}</dd></div>
    </dl>` : "";
  return `
    ${meta}
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

async function openRandomNote() {
  try {
    const note = await api("/api/notes/random");
    toast("یادداشت تصادفی");
    await openNote(note.id);
  } catch (error) {
    toast(error.message);
  }
}

const journalMonthNames = ["", "ژانویه", "فوریه", "مارس", "آوریل", "مه", "ژوئن", "ژوئیه", "اوت", "سپتامبر", "اکتبر", "نوامبر", "دسامبر"];
const journalWeekdays = ["ش", "ی", "د", "س", "چ", "پ", "ج"];

async function openDailyDate(date) {
  const result = await api("/api/daily", { method: "POST", body: date ? { date } : {} });
  toast(result.created ? "یادداشت روز ساخته شد." : "یادداشت روز باز شد.");
  await openNote(result.note.id);
}

async function loadTasks() {
  const filter = state.taskFilter || "open";
  const query = filter === "all" ? "" : filter === "open" ? "?open=true" : "?open=false";
  const data = await api("/api/tasks" + query);
  const rows = (data.items || []).map((item) => `
    <article class="card task-row">
      <label class="task-check">
        <input type="checkbox" data-task-note="${item.noteId}" data-task-line="${item.lineIndex}" ${item.isDone ? "checked" : ""} />
        <span class="${item.isDone ? "done" : ""}">${escapeHtml(item.text || "بدون متن")}</span>
      </label>
      <button class="ghost" type="button" data-open-note="${item.noteId}">${escapeHtml(item.noteTitle)}</button>
    </article>`).join("") || `<p class="muted">کار ${filter === "open" ? "باز" : filter === "done" ? "تمام‌شده" : ""}ی نیست.</p>`;
  $("view-tasks").innerHTML = `
    <div class="card" style="max-width:820px">
      <div class="row" style="justify-content:space-between;align-items:center;margin-bottom:14px">
        <div>
          <h3 style="margin:0">چک‌لیست مارک‌داون</h3>
          <p class="muted" style="margin:6px 0 0">باز ${data.openCount} · انجام‌شده ${data.doneCount}</p>
        </div>
        <div class="row">
          <button class="ghost ${filter === "open" ? "active-filter" : ""}" type="button" data-task-filter="open">باز</button>
          <button class="ghost ${filter === "done" ? "active-filter" : ""}" type="button" data-task-filter="done">انجام‌شده</button>
          <button class="ghost ${filter === "all" ? "active-filter" : ""}" type="button" data-task-filter="all">همه</button>
        </div>
      </div>
      <div class="list">${rows}</div>
    </div>`;
  $("view-tasks").querySelectorAll("[data-task-filter]").forEach((button) => {
    button.onclick = () => { state.taskFilter = button.dataset.taskFilter; loadTasks(); };
  });
  $("view-tasks").querySelectorAll("[data-open-note]").forEach((button) => {
    button.onclick = () => openNote(button.dataset.openNote);
  });
  $("view-tasks").querySelectorAll("input[data-task-note]").forEach((input) => {
    input.onchange = async () => {
      try {
        await api("/api/tasks/toggle", {
          method: "POST",
          body: {
            noteId: Number(input.dataset.taskNote),
            lineIndex: Number(input.dataset.taskLine),
            done: input.checked
          }
        });
        toast(input.checked ? "انجام شد." : "دوباره باز شد.");
        loadTasks();
      } catch (error) {
        toast(error.message);
        input.checked = !input.checked;
      }
    };
  });
}

async function loadJournal() {
  const current = state.journal || {};
  const query = current.year && current.month ? `?year=${current.year}&month=${current.month}` : "";
  const month = await api("/api/daily/month" + query);
  state.journal = { year: month.year, month: month.month };
  const pads = Array.from({ length: month.leadingPadding }, () => `<div class="journal-cell pad"></div>`).join("");
  const cells = month.days.map((day) => {
    const dayNum = day.tehranDate.slice(-2).replace(/^0/, "");
    const classes = ["journal-cell", day.tehranDate === month.today ? "today" : "", day.hasNote ? "has-note" : ""].filter(Boolean).join(" ");
    return `<button type="button" class="${classes}" data-journal-day="${escapeHtml(day.tehranDate)}">
      <strong>${escapeHtml(dayNum)}</strong>
      <span class="muted">${day.hasNote ? "●" : ""}</span>
    </button>`;
  }).join("");
  $("view-journal").innerHTML = `
    <div class="card" style="max-width:920px">
      <div class="row" style="justify-content:space-between;align-items:center;margin-bottom:14px">
        <div>
          <h3 style="margin:0">${escapeHtml(journalMonthNames[month.month])} ${month.year}</h3>
          <p class="muted" style="margin:6px 0 0">روزهای پررنگ یادداشت دارند. امروز: ${escapeHtml(month.today)}</p>
        </div>
        <div class="row">
          <button class="ghost" type="button" id="journal-prev">ماه قبل</button>
          <button class="ghost" type="button" id="journal-today">امروز</button>
          <button class="ghost" type="button" id="journal-next">ماه بعد</button>
        </div>
      </div>
      <div class="journal-weekdays">${journalWeekdays.map((name) => `<span>${name}</span>`).join("")}</div>
      <div class="journal-grid">${pads}${cells}</div>
    </div>`;
  $("journal-prev").onclick = () => {
    let y = month.year;
    let m = month.month - 1;
    if (m < 1) { m = 12; y -= 1; }
    state.journal = { year: y, month: m };
    loadJournal();
  };
  $("journal-next").onclick = () => {
    let y = month.year;
    let m = month.month + 1;
    if (m > 12) { m = 1; y += 1; }
    state.journal = { year: y, month: m };
    loadJournal();
  };
  $("journal-today").onclick = () => { state.journal = null; loadJournal(); };
  $("view-journal").querySelectorAll("[data-journal-day]").forEach((button) => {
    button.onclick = async () => {
      try { await openDailyDate(button.dataset.journalDay); }
      catch (error) { toast(error.message); }
    };
  });
}

async function applyTemplate() {
  const name = $("template-pick").value;
  if (!name || !state.current?.note?.id) {
    toast("یک قالب انتخاب کنید.");
    return;
  }
  try {
    const saved = await api(`/api/notes/${state.current.note.id}/template`, { method: "POST", body: { name } });
    toast("قالب درج شد.");
    await loadNotes();
    await openNote(saved.id);
  } catch (error) {
    toast(error.message);
  }
}

async function loadTemplates() {
  state.templates = await api("/api/templates");
  const cards = state.templates.map((template) => `
    <article class="card">
      <div class="row" style="justify-content:space-between;align-items:center">
        <h3 style="margin:0">${escapeHtml(template.title)}</h3>
        <button class="danger" type="button" data-delete-template="${escapeHtml(template.name)}">حذف</button>
      </div>
      <p class="muted" style="margin:8px 0">${escapeHtml((template.tags || []).join("، ") || "بدون برچسب")}</p>
      <pre class="muted" style="white-space:pre-wrap;margin:0">${escapeHtml(template.content || "")}</pre>
    </article>`).join("") || `<p class="muted">هنوز قالبی نیست.</p>`;
  $("view-templates").innerHTML = `
    <div class="split">
      <form id="template-form" class="card">
        <h3>قالب تازه</h3>
        <p class="muted" style="margin:8px 0 14px">جاها: {{title}} {{date}} {{time}} {{yesterday}} {{tomorrow}}. عنوان daily متن یادداشت امروز را می‌سازد و این فایل‌ها یادداشت نیستند.</p>
        <label>عنوان<input id="template-title" required /></label>
        <label>برچسب‌ها، با ویرگول<input id="template-tags" /></label>
        <label>متن<textarea id="template-content"></textarea></label>
        <button class="primary" type="submit">ساخت قالب</button>
        <p id="template-error" class="error" hidden></p>
      </form>
      <div class="list">${cards}</div>
    </div>`;
  $("template-form").onsubmit = async (event) => {
    event.preventDefault();
    $("template-error").hidden = true;
    try {
      await api("/api/templates", {
        method: "POST",
        body: {
          title: $("template-title").value,
          content: $("template-content").value,
          tags: $("template-tags").value.split(/[,،]/).map((item) => item.trim()).filter(Boolean)
        }
      });
      toast("قالب ساخته شد.");
      loadTemplates();
    } catch (error) {
      $("template-error").hidden = false;
      $("template-error").textContent = error.message;
    }
  };
  $("view-templates").querySelectorAll("[data-delete-template]").forEach((button) => {
    button.onclick = async () => {
      await api("/api/templates?name=" + encodeURIComponent(button.dataset.deleteTemplate), { method: "DELETE" });
      toast("قالب حذف شد.");
      loadTemplates();
    };
  });
}

async function saveNote(event) {
  event.preventDefault();
  const body = {
    title: $("note-title").value,
    content: $("note-content").value,
    tags: $("note-tags").value.split(/[,،]/).map((item) => item.trim()).filter(Boolean),
    aliases: $("note-aliases").value.split(/[,،]/).map((item) => item.trim()).filter(Boolean)
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
  toast("یادداشت به سطل زباله رفت.");
  await loadNotes();
}

async function loadTrash() {
  const items = await api("/api/trash");
  $("view-trash").innerHTML = `
    <div class="card" style="max-width:720px">
      <div class="row" style="justify-content:space-between;align-items:center;flex-wrap:wrap;gap:8px">
        <div>
          <h3 style="margin:0">یادداشت‌های حذف‌شده</h3>
          <p class="muted" style="margin:6px 0 0">فایل در خزانه می‌ماند تا بازیابی یا حذف دائمی.</p>
        </div>
        <div class="row">
          <button class="ghost" type="button" id="trash-restore-all" ${items.length ? "" : "disabled"}>بازیابی همه</button>
          <button class="danger" type="button" id="trash-empty" ${items.length ? "" : "disabled"}>خالی کردن سطل</button>
        </div>
      </div>
      <div class="list" style="margin-top:12px">
        ${items.map((note) => `<article class="note-item trash-row">
          <div><strong>${escapeHtml(note.title)}</strong><div class="muted">${escapeHtml(formatIsoTehran(note.deletedAt))}</div></div>
          <div class="row">
            <button class="ghost" type="button" data-restore="${note.id}">بازیابی</button>
            <button class="danger" type="button" data-purge="${note.id}">حذف دائمی</button>
          </div>
        </article>`).join("") || `<p class="muted">سطل زباله خالی است.</p>`}
      </div>
    </div>`;
  $("trash-restore-all")?.addEventListener("click", async () => {
    const result = await api("/api/trash/restore-all", { method: "POST" });
    toast(`${result.count} یادداشت بازیابی شد.`);
    loadTrash();
  });
  $("trash-empty")?.addEventListener("click", async () => {
    if (!confirm("همهٔ یادداشت‌های سطل برای همیشه حذف شوند؟")) return;
    const result = await api("/api/trash", { method: "DELETE" });
    toast(`${result.count} یادداشت برای همیشه حذف شد.`);
    loadTrash();
  });
  $("view-trash").querySelectorAll("[data-restore]").forEach((button) => {
    button.onclick = async () => {
      await api(`/api/trash/${button.dataset.restore}/restore`, { method: "POST" });
      toast("یادداشت بازیابی شد.");
      loadTrash();
    };
  });
  $("view-trash").querySelectorAll("[data-purge]").forEach((button) => {
    button.onclick = async () => {
      await api(`/api/trash/${button.dataset.purge}`, { method: "DELETE" });
      toast("یادداشت برای همیشه حذف شد.");
      loadTrash();
    };
  });
}

async function ensureNotesForQuickOpen() {
  if (!state.notes.length) state.notes = await api("/api/notes");
}

function renderQuickOpenResults(query) {
  const needle = query.trim();
  const pool = needle
    ? state.notes.filter((note) => note.title.includes(needle))
    : state.notes.slice(0, 20);
  $("quick-open-results").innerHTML = pool.slice(0, 12).map((note) => `
    <button type="button" class="note-item" data-qid="${note.id}">${escapeHtml(note.title)}</button>`).join("")
    || `<p class="muted">یادنتی پیدا نشد.</p>`;
  $("quick-open-results").querySelectorAll("[data-qid]").forEach((button) => {
    button.onclick = () => {
      closeQuickOpen();
      openNote(button.dataset.qid);
    };
  });
}

function openQuickOpen() {
  ensureNotesForQuickOpen().then(() => {
    $("quick-open").hidden = false;
    $("quick-open-input").value = "";
    renderQuickOpenResults("");
    $("quick-open-input").focus();
  });
}

function closeQuickOpen() {
  $("quick-open").hidden = true;
}

function exportPdf() {
  if (!state.current?.note?.id) return;
  window.location = `/api/notes/${state.current.note.id}/pdf`;
}

function exportMarkdown() {
  if (!state.current?.note?.id) return;
  window.location = `/api/notes/${state.current.note.id}/markdown`;
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
    <div class="card" style="margin-bottom:14px">
      <h3>ورود فایل مارک‌داون</h3>
      <p class="muted" style="margin:8px 0 14px">یک فایل .md با frontmatter YAML به خزانه اضافه می‌شود و همان‌جا در پایگاه‌داده ثبت می‌شود.</p>
      <input id="import-md-page" type="file" accept=".md,text/markdown" />
      <div class="row" style="margin-top:12px"><button class="primary" id="import-md-go">ورود مارک‌داون</button></div>
      <p id="import-md-result" class="muted" style="margin-top:12px"></p>
    </div>
    <div class="card">
      <h3>ورود PDF به خزانه</h3>
      <p class="muted" style="margin:8px 0 14px">فایل در پوشهٔ staging نوشته می‌شود، سپس با وضعیت Committed به خزانه منتقل می‌شود. عمق درخت از ۱۵ بیشتر نمی‌شود. فایل تکراری با درهم‌ساز SHA-256 رد می‌شود.</p>
      <input id="pdf-files" type="file" accept="application/pdf,.pdf" multiple />
      <div class="row" style="margin-top:12px"><button class="primary" id="import-go">شروع ورود</button></div>
      <div id="import-results" class="list" style="margin-top:12px"></div>
    </div>`;
  $("import-md-go").onclick = async () => {
    const file = $("import-md-page").files?.[0];
    if (!file) return toast("فایل .md انتخاب نشده.");
    const body = new FormData();
    body.append("file", file);
    try {
      const note = await api("/api/notes/import-markdown", { method: "POST", body });
      $("import-md-result").textContent = `وارد شد: ${note.title}`;
      toast("مارک‌داون وارد شد.");
    } catch (error) {
      $("import-md-result").textContent = error.message;
      toast(error.message);
    }
  };
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
$("open-daily").onclick = async () => {
  try { await openDailyDate(); }
  catch (error) { toast(error.message); }
};

$("logout").onclick = async () => {
  await api("/api/auth/logout", { method: "POST" });
  state.user = null;
  state.view = "dashboard";
  state.current = null;
  showAuth();
};

$("quick-open-btn").onclick = () => openQuickOpen();
$("random-note").onclick = () => openRandomNote();
$("quick-open-backdrop").onclick = () => closeQuickOpen();
$("quick-open-input").oninput = () => renderQuickOpenResults($("quick-open-input").value);

document.addEventListener("keydown", (event) => {
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
    event.preventDefault();
    if (state.user) openQuickOpen();
  }
  if ((event.ctrlKey || event.metaKey) && event.shiftKey && event.key.toLowerCase() === "r") {
    event.preventDefault();
    if (state.user) openRandomNote();
  }
  if (event.key === "Escape" && !$("quick-open").hidden) closeQuickOpen();
});

setInterval(tickClock, 1000);
api("/api/auth/me").then((user) => { state.user = user; showApp(); }).catch(() => showAuth());
