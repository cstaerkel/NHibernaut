"use strict";
// NOTE: This dashboard is read-only and local. All data-derived content (SQL, parameter values,
// entity names, ids) is escaped via esc() before being placed in markup, so innerHTML use is XSS-safe.
(function () {
  // ---- token (for non-loopback / token-protected dashboards) ----
  const token = new URLSearchParams(location.search).get("token") || "";
  // base path: the dashboard runs at root (HttpListener) or under a mount path (Tier C MapNHibernaut).
  // Derive it from where app.js itself was loaded so API/SSE calls resolve in both cases.
  const base = (() => {
    const src = (document.currentScript && document.currentScript.src) || location.href;
    try { return new URL(".", src).pathname.replace(/\/$/, ""); } catch (e) { return ""; }
  })();
  const q = (extra) => {
    const p = new URLSearchParams(extra || {});
    if (token) p.set("token", token);
    const s = p.toString();
    return s ? "?" + s : "";
  };
  const api = (path, extra) => fetch(base + path + q(extra)).then(r => {
    if (!r.ok) throw new Error(path + " -> " + r.status);
    return r.json();
  });

  // ---- state ----
  const state = {
    sessions: [],
    selectedSession: null,
    selectedStatement: null,
    detail: null,
    live: true,
    editorScheme: "vscode",
    compareA: null,
    compareB: null,
  };

  const app = document.getElementById("app");
  const esc = (s) => String(s == null ? "" : s).replace(/[&<>"']/g, c =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
  const ms = (n) => (n == null ? "—" : (n < 1 ? n.toFixed(2) : Math.round(n)) + " ms");
  const time = (iso) => iso ? new Date(iso).toLocaleTimeString() : "—";
  const byId = (id) => document.getElementById(id);
  const setHtml = (el, html) => { if (el) el.innerHTML = html; };

  // ---- top bar counters ----
  function updateCounters() {
    const s = state.sessions;
    byId("c-sessions").textContent = s.length;
    byId("c-queries").textContent = s.reduce((a, x) => a + x.statementCount, 0);
    byId("c-time").textContent = Math.round(s.reduce((a, x) => a + x.totalDurationMs, 0));
    byId("c-nplus1").textContent = s.filter(x => x.maxSeverity).length;
  }

  // ---- routing ----
  function route() {
    const hash = location.hash || "#/sessions";
    document.querySelectorAll(".tab").forEach(t =>
      t.classList.toggle("active", hash.startsWith("#/" + t.dataset.tab)));

    if (hash.startsWith("#/sessions")) {
      const m = hash.match(/#\/sessions\/([0-9a-fA-F-]+)/);
      renderSessions(m ? m[1] : null);
    } else if (hash.startsWith("#/aggregate")) {
      renderAggregate();
    } else if (hash.startsWith("#/compare")) {
      renderCompare();
    } else {
      renderSessions(null);
    }
  }

  // ---- sessions view ----
  async function loadSessions() {
    state.sessions = await api("/api/sessions", { take: 500 });
    sortSessions();
    updateCounters();
  }

  function sortSessions() {
    const sev = { Error: 3, Warning: 2, Info: 1 };
    state.sessions.sort((a, b) =>
      (sev[b.maxSeverity] || 0) - (sev[a.maxSeverity] || 0) ||
      new Date(b.startedAt) - new Date(a.startedAt));
  }

  function renderSessions(selectId) {
    setHtml(app, '<div class="split"><div class="list-pane" id="slist"></div><div class="detail-pane" id="sdetail"><div class="empty">Select a session</div></div></div>');
    renderSessionList();
    if (selectId) selectSession(selectId);
  }

  function renderSessionList() {
    const list = byId("slist");
    if (!list) return;
    if (!state.sessions.length) { setHtml(list, '<div class="empty">No sessions captured yet.</div>'); return; }
    setHtml(list, state.sessions.map(s => `
      <div class="srow ${s.id === state.selectedSession ? "selected" : ""}" data-id="${esc(s.id)}">
        <span class="sev-dot sev-${esc(s.maxSeverity || "")}"></span>
        <div>
          <div class="nums">${s.statementCount} queries · ${ms(s.totalDurationMs)}</div>
          <div class="muted" style="font-size:11px">${time(s.startedAt)}${s.isSealed ? "" : " · open"}</div>
        </div>
        <div class="meta">${s.alertCount ? `<span class="badge ${esc(s.maxSeverity)}">${s.alertCount} alert${s.alertCount > 1 ? "s" : ""}</span>` : ""}<br>${s.totalRowsRead} rows · ${s.writeCount} writes</div>
      </div>`).join(""));
    list.querySelectorAll(".srow").forEach(r =>
      r.onclick = () => { location.hash = "#/sessions/" + r.dataset.id; });
  }

  async function selectSession(id) {
    state.selectedSession = id;
    state.selectedStatement = null;
    renderSessionList();
    state.detail = await api("/api/sessions/" + id);
    renderDetail();
  }

  function renderDetail() {
    const d = state.detail, host = byId("sdetail");
    if (!d || !host) return;
    const s = d.summary;
    setHtml(host, `
      <h2>Session ${esc(s.id.slice(0, 8))} <span class="muted">${time(s.startedAt)} · ${s.statementCount} queries · ${ms(s.totalDurationMs)} · ${s.threadCount} thread(s)</span></h2>
      <div id="alerts">${renderAlerts(d.alerts)}</div>
      <h3>Waterfall</h3><div id="wf"></div>
      <h3>Statements</h3><div id="stmts"></div>
      <div id="whatloaded"></div>`);
    renderWaterfall(d.statements);
    renderStatements(d.statements);
    document.querySelectorAll(".alert").forEach(a => a.onclick = () => {
      const ids = JSON.parse(a.dataset.related || "[]");
      document.querySelectorAll(".wf-row").forEach(r => r.classList.toggle("hl", ids.includes(r.dataset.id)));
    });
  }

  function renderAlerts(alerts) {
    if (!alerts.length) return '<div class="muted">No alerts. 👍</div>';
    return alerts.map(a => `
      <div class="alert ${esc(a.severity)}" data-related='${esc(JSON.stringify(a.relatedStatementIds))}'>
        <div class="title">${esc(a.title)} <span class="badge ${esc(a.severity)}">${esc(a.severity)}</span></div>
        <div class="desc">${esc(a.description)}</div>
        ${a.suggestion ? `<div class="sugg">💡 ${esc(a.suggestion)}</div>` : ""}
      </div>`).join("");
  }

  function renderWaterfall(statements) {
    const wf = byId("wf");
    if (!statements.length) { setHtml(wf, '<div class="muted">No statements.</div>'); return; }
    const starts = statements.map(s => new Date(s.startedAt).getTime());
    const ends = statements.map(s => new Date(s.startedAt).getTime() + (s.durationMs || 0));
    const t0 = Math.min.apply(null, starts), t1 = Math.max.apply(null, ends);
    const span = Math.max(t1 - t0, 1);
    wf.className = "waterfall";
    setHtml(wf, statements.map(s => {
      const left = ((new Date(s.startedAt).getTime() - t0) / span) * 100;
      const width = Math.max(((s.durationMs || 0) / span) * 100, 0.6);
      const slow = (s.durationMs || 0) > 200;
      return `<div class="wf-row" data-id="${esc(s.id)}" title="${esc(s.sql)}">
        <div class="wf-bar ${esc(s.kind)} ${slow ? "slow" : ""}" style="left:${left}%;width:${width}%"></div>
        <div class="wf-label">${esc(s.kind)} · ${ms(s.durationMs)}${s.rowsRead != null ? " · " + s.rowsRead + "r" : ""}</div>
      </div>`;
    }).join(""));
    wf.querySelectorAll(".wf-row").forEach(r => r.onclick = () => selectStatement(r.dataset.id));
  }

  function fold(statements) {
    const groups = [];
    for (const s of statements) {
      const last = groups[groups.length - 1];
      if (last && last.key === s.normalizedSql) last.items.push(s);
      else groups.push({ key: s.normalizedSql, items: [s] });
    }
    return groups;
  }

  function renderStatements(statements) {
    const host = byId("stmts");
    const groups = fold(statements);
    setHtml(host, groups.map((g, gi) => {
      if (g.items.length === 1) return stmtRow(g.items[0]);
      return `<div class="stmt fold-group" data-gi="${gi}">
        <div class="sql"><span class="fold">×${g.items.length}</span>${esc(firstLine(g.items[0].sql))}</div>
        <div class="stmt-meta"><span>folded N+1 — click to expand</span><span>Σ ${ms(g.items.reduce((a, x) => a + (x.durationMs || 0), 0))}</span></div>
      </div><div class="fold-items" data-gi="${gi}" style="display:none">${g.items.map(stmtRow).join("")}</div>`;
    }).join(""));
    host.querySelectorAll(".fold-group").forEach(el => el.onclick = () => {
      const items = host.querySelector('.fold-items[data-gi="' + el.dataset.gi + '"]');
      items.style.display = items.style.display === "none" ? "block" : "none";
    });
    host.querySelectorAll(".stmt[data-id]").forEach(el =>
      el.onclick = (e) => { e.stopPropagation(); selectStatement(el.dataset.id); });
  }

  function stmtRow(s) {
    const slow = (s.durationMs || 0) > 200;
    return `<div class="stmt ${s.id === state.selectedStatement ? "selected" : ""}" data-id="${esc(s.id)}">
      <div class="sql">${esc(firstLine(s.sql))}</div>
      <div class="stmt-meta">
        <span class="badge">${esc(s.kind)}</span>
        <span style="${slow ? "color:var(--error)" : ""}">${ms(s.durationMs)}</span>
        ${s.rowsRead != null ? `<span>${s.rowsRead} rows</span>` : ""}
        ${s.entityLoadCount ? `<span>${s.entityLoadCount} loaded</span>` : ""}
        ${writeSummaryShort(s.id)}
        ${s.exception ? `<span style="color:var(--error)">⚠ ${esc(s.exception)}</span>` : ""}
      </div></div>`;
  }

  const firstLine = (sql) => { const t = (sql || "").trim(); return t.length > 200 ? t.slice(0, 200) + "…" : t; };

  function selectStatement(id) {
    state.selectedStatement = id;
    const d = state.detail;
    const s = d.statements.find(x => x.id === id);
    if (!s) return;
    document.querySelectorAll(".stmt").forEach(e => e.classList.toggle("selected", e.dataset.id === id));

    // hydrated entities grouped by type, listing primary keys (capped for readability)
    const loads = d.entityLoads.filter(e => e.statementId === id);
    const byType = {};
    loads.forEach(l => (byType[l.entityType] = byType[l.entityType] || []).push(l.id));
    const hydratedChips = Object.entries(byType).map(([t, ids]) => {
      const shown = ids.slice(0, 8).map(x => "#" + esc(x)).join(", ");
      const more = ids.length > 8 ? " +" + (ids.length - 8) : "";
      return `<span class="chip">${ids.length} × ${esc(shortType(t))} <span class="muted">${shown}${more}</span></span>`;
    }).join("");

    const inits = d.collectionInits.filter(c => c.statementId === id);
    const writes = writeLines(id);

    setHtml(byId("whatloaded"), `
      <h3>What this statement did</h3>
      <div class="panel">
        <pre class="sql">${esc(s.sql)}</pre>
        ${s.parameters.length ? `<div class="kv">${s.parameters.map(p => `<div class="k">${esc(p.name)}</div><div>${esc(p.value)} <span class="muted">${esc(p.dbType)}</span></div>`).join("")}</div>` : '<div class="muted">No parameters.</div>'}
        ${writes}
        <div style="margin-top:8px"><b>hydrated:</b> ${hydratedChips ? `<span class="chips">${hydratedChips}</span>` : '<span class="muted">none</span>'}</div>
        <div style="margin-top:4px"><b>initialized:</b> ${inits.length ? `<span class="chips">${inits.map(i => `<span class="chip">${esc(shortRole(i.role))}</span>`).join("")}</span>` : '<span class="muted">none</span>'}</div>
        ${renderSource(s.stackTrace)}
      </div>`);
  }

  const shortType = (t) => (t || "").split(".").pop();
  const shortRole = (r) => { const p = (r || "").split("."); return p.slice(-2).join("."); };
  const writeVerb = (k) => ({ Insert: "created", Update: "updated", Delete: "deleted" }[k] || (k || "").toLowerCase());

  function writesFor(id) {
    const d = state.detail;
    return d && d.writes ? d.writes.filter(w => w.statementId === id) : [];
  }

  // Compact "created Blog #42, updated 3 × Order" shown inline on a statement row.
  function writeSummaryShort(id) {
    const ws = writesFor(id);
    if (!ws.length) return "";
    const map = {};
    ws.forEach(w => { const k = w.kind + "|" + shortType(w.entityType); (map[k] = map[k] || []).push(w); });
    const parts = Object.entries(map).map(([k, list]) => {
      const kind = k.split("|")[0], type = k.split("|")[1];
      return list.length === 1
        ? `${writeVerb(kind)} ${esc(type)} #${esc(list[0].id)}`
        : `${writeVerb(kind)} ${list.length} × ${esc(type)}`;
    });
    return `<span style="color:var(--accent)">${parts.join(", ")}</span>`;
  }

  // Full "created: Blog #42, Blog #43" lines for the detail panel.
  function writeLines(id) {
    const ws = writesFor(id);
    if (!ws.length) return "";
    const byKind = {};
    ws.forEach(w => (byKind[w.kind] = byKind[w.kind] || []).push(w));
    return Object.entries(byKind).map(([kind, list]) => {
      const chips = list.slice(0, 24).map(w =>
        `<span class="chip">${esc(shortType(w.entityType))} #${esc(w.id)}</span>`).join("");
      const more = list.length > 24 ? ` <span class="muted">+${list.length - 24}</span>` : "";
      return `<div style="margin-top:4px"><b>${writeVerb(kind)}:</b> <span class="chips">${chips}${more}</span></div>`;
    }).join("");
  }

  function renderSource(stack) {
    if (!stack) return '<div class="muted" style="margin-top:6px">Stack traces off (enable CaptureStackTraces).</div>';
    const lines = stack.split("\n").filter(Boolean);
    const appFrame = lines.find(l => /\(.*:\d+\)/.test(l));
    let link = "";
    if (appFrame) {
      const m = appFrame.match(/\((.*):(\d+)\)/);
      if (m) {
        const file = m[1], line = m[2];
        const base = file.split(/[\\/]/).pop();
        link = `<a class="src-link" href="${esc(state.editorScheme)}://file/${esc(file)}:${esc(line)}">${esc(base)}:${esc(line)}</a>`;
      }
    }
    return `<div style="margin-top:8px"><b>source:</b> ${link || '<span class="muted">unknown</span>'}
      <details style="margin-top:4px"><summary class="muted">stack</summary><pre class="stack">${esc(stack)}</pre></details></div>`;
  }

  // ---- aggregate view ----
  async function renderAggregate() {
    setHtml(app, '<div class="pad"><h2>Worst offenders</h2><div id="agg"><div class="empty">Loading…</div></div></div>');
    const rows = await api("/api/aggregate");
    setHtml(byId("agg"), `<table>
      <thead><tr><th>Query shape</th><th class="num">Calls</th><th class="num">Total</th><th class="num">Avg</th><th class="num">Max rows</th><th class="num">Sessions</th><th class="num">N+1</th></tr></thead>
      <tbody>${rows.map(r => `<tr>
        <td class="sql" title="${esc(r.normalizedSql)}">${esc(r.normalizedSql)}</td>
        <td class="num">${r.executionCount}</td>
        <td class="num">${ms(r.totalDurationMs)}</td>
        <td class="num">${ms(r.avgDurationMs)}</td>
        <td class="num">${r.maxRowsRead}</td>
        <td class="num">${r.sessionCount}</td>
        <td class="num">${r.nPlusOneIncidence ? `<span class="badge Warning">${r.nPlusOneIncidence}</span>` : "—"}</td>
      </tr>`).join("")}</tbody></table>`);
  }

  // ---- compare view ----
  function renderCompare() {
    const opts = state.sessions.map(s => `<option value="${esc(s.id)}">${esc(s.id.slice(0, 8))} · ${s.statementCount}q · ${ms(s.totalDurationMs)}</option>`).join("");
    setHtml(app, `<div class="pad"><h2>Compare sessions</h2>
      <div class="row"><label>A <select id="cmpA"><option value="">—</option>${opts}</select></label>
      <label>B <select id="cmpB"><option value="">—</option>${opts}</select></label></div>
      <div id="cmpout" class="pad"></div></div>`);
    byId("cmpA").value = state.compareA || "";
    byId("cmpB").value = state.compareB || "";
    byId("cmpA").onchange = (e) => { state.compareA = e.target.value; doCompare(); };
    byId("cmpB").onchange = (e) => { state.compareB = e.target.value; doCompare(); };
    doCompare();
  }

  async function doCompare() {
    const out = byId("cmpout");
    if (!state.compareA || !state.compareB) { setHtml(out, '<div class="muted">Pick two sessions.</div>'); return; }
    const results = await Promise.all([api("/api/sessions/" + state.compareA), api("/api/sessions/" + state.compareB)]);
    const a = results[0], b = results[1];
    const cmp = (name, av, bv, lowerBetter) => {
      if (lowerBetter === undefined) lowerBetter = true;
      const delta = bv - av;
      const cls = delta === 0 ? "" : (lowerBetter === (delta < 0) ? "diff-better" : "diff-worse");
      return `<tr><td>${esc(name)}</td><td class="num">${av}</td><td class="num">${bv}</td><td class="num ${cls}">${delta > 0 ? "+" : ""}${Math.round(delta * 100) / 100}</td></tr>`;
    };
    setHtml(out, `<table><thead><tr><th>Metric</th><th class="num">A</th><th class="num">B</th><th class="num">Δ</th></tr></thead><tbody>
      ${cmp("Queries", a.summary.statementCount, b.summary.statementCount)}
      ${cmp("Total ms", Math.round(a.summary.totalDurationMs), Math.round(b.summary.totalDurationMs))}
      ${cmp("Rows read", a.summary.totalRowsRead, b.summary.totalRowsRead)}
      ${cmp("Writes", a.summary.writeCount, b.summary.writeCount)}
      ${cmp("Alerts", a.alerts.length, b.alerts.length)}
    </tbody></table>`);
  }

  // ---- live SSE ----
  function connectLive() {
    let es;
    try { es = new EventSource(base + "/api/stream" + q()); } catch (e) { return; }
    es.addEventListener("session", (e) => {
      if (!state.live) return;
      try {
        const summary = JSON.parse(e.data);
        const i = state.sessions.findIndex(x => x.id === summary.id);
        if (i >= 0) state.sessions[i] = summary; else state.sessions.unshift(summary);
        sortSessions();
        updateCounters();
        if (location.hash.startsWith("#/sessions")) renderSessionList();
      } catch (err) { /* ignore */ }
    });
    es.onerror = () => { /* browser auto-reconnects */ };
  }

  // ---- controls ----
  byId("live-toggle").onclick = (e) => {
    state.live = !state.live;
    e.target.textContent = state.live ? "⏸ Live" : "▶ Paused";
    e.target.classList.toggle("paused", !state.live);
  };
  byId("clear-btn").onclick = async () => {
    await fetch(base + "/api/sessions" + q(), { method: "DELETE" });
    state.sessions = []; state.detail = null; state.selectedSession = null;
    updateCounters(); route();
  };
  window.addEventListener("hashchange", route);

  // ---- boot ----
  (async function boot() {
    try { state.editorScheme = (await api("/api/config")).editorLinkScheme || "vscode"; } catch (e) { /* default */ }
    await loadSessions();
    connectLive();
    route();
  })();
})();
