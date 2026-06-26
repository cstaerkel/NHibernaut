# NHibernaut — End-to-end MCP testing

How to prove the **`nhibernaut-mcp` MCP server returns the same profiler data the dashboard serves and
the desktop app shows** — by running the real binaries against one shared dashboard and comparing their
output. This is the manual/E2E counterpart to the automated `test/NHibernaut.Mcp.Tests` smoke tests; use
it to validate a build, a new tool, or a release candidate against live or frozen data.

For the MCP surface itself see the [MCP guide](MCP.md); for the topology see the [Overview](OVERVIEW.md);
for the desktop app see the [Desktop guide](DESKTOP.md).

---

## The idea

NHibernaut has **one Collector** (a dashboard HTTP API) and several **Viewers** that all read it through
the same client code (`NHibernaut.Client.HttpDashboardClient` → `GET /api/sessions`, `/api/sessions/{id}`,
`/api/aggregate`, `/api/alerts`, SSE `/api/stream`):

```
                 ┌─ desktop app (NHibernaut.App)   ── HttpDashboardClient ─┐
Producer ─▶ Collector (dashboard @ :5005) ─┤                              ├─▶ same DTOs
                 └─ nhibernaut-mcp (MCP server)    ── HttpDashboardClient ─┘
                 └─ curl / browser                 ── raw HTTP ────────────┘
```

So "the MCP server gets the same data the desktop has" reduces to: **point the desktop app and
`nhibernaut-mcp` at the same dashboard URL, then show the MCP tool output equals the dashboard API
responses.** `curl` against the same API is the ground truth.

Two ways to stand up the data:

| Method | Data | Good for | Caveat |
|---|---|---|---|
| **A — Live sample** | `samples/NHibernaut.Sample.Console` self-hosts a dashboard on `:5005` and generates N+1 / writes / unbounded selects **forever** | realistic, exercises the live SSE feed | the in-memory store is a **ring buffer** (200 sessions / 30 min) and the sample adds ~1.3 sessions/s, so the "recent N" window **churns** — anchored list comparisons drift and old sessions evict between calls |
| **B — Frozen dashboard** *(recommended for exact comparison)* | capture real sessions once, then re-serve them from the standalone host, which **does not generate workload** | deterministic, repeatable, exact id-for-id and field-for-field comparison | none — the set is stable |

---

## Prerequisites

- A Release build: `dotnet build -c Release` (produces `nhibernaut-mcp.dll`, `nhibernaut-app.dll`,
  `nhibernaut-dashboard.dll`, and the console sample).
- `curl` and `python` for the comparison (any JSON-aware tooling works).
- For the **GUI screenshot** verification only: **Windows PowerShell** (`powershell.exe`, 5.1) and the
  `avalonia-desktop-testing` skill — see [Visual verification](#visual-verification-optional). The
  data-equality proof below needs no PowerShell.

Paths below are written from the repo root with the Release output layout.

---

## Driving the MCP server over stdio

`nhibernaut-mcp` speaks newline-delimited JSON-RPC over **stdio**. The robust way to drive it from a test
is the in-repo `test/NHibernaut.Mcp.Tests/Integration/McpProcessHarness.cs`: it launches the **pre-built
DLL** (not `dotnet run`, which would nest a build), keeps **stdin open**, writes one frame at a time, and
reads responses. Reuse that harness for automated checks.

For a quick **command-line** check, send the frames through a pipe — but **keep the write end open** so
EOF does not arrive before the server has answered. A bare `cat file | server` delivers EOF instantly and
the stdio transport shuts down with **zero responses**; appending a `sleep` fixes it:

```bash
MCP=src/NHibernaut.Mcp/bin/Release/net10.0/nhibernaut-mcp.dll

cat > /tmp/req.jsonl <<'EOF'
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"cli","version":"1.0.0"}}}
{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}
{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"nhibernaut_list_sessions","arguments":{"take":200,"response_format":"json"}}}
EOF

# (frames…) then keep stdin open ~8s so the server can start, read, and respond before EOF
( cat /tmp/req.jsonl; sleep 8 ) | dotnet "$MCP" --url http://127.0.0.1:5006 --max-output-chars 5000000 \
  > /tmp/mcp_out.jsonl 2> /tmp/mcp_err.log
```

- All **diagnostics go to stderr**; **stdout is pure JSON-RPC** (one frame per line). Verify every stdout
  line parses and carries `"jsonrpc":"2.0"`.
- `--max-output-chars` must be raised when you want a **full** large session detail in JSON: on overflow
  the limiter replaces the whole payload with `{"truncated":true,"continuationHint":…}` (it does not return
  a partial record), so a big N+1 session truncates to the marker at the default `25000`.
- Tool results arrive as `result.content[0].text`, whose value is the formatter's JSON/Markdown **string**
  (escaped inside the frame). Parse the frame, then parse that inner string — don't grep the escaped blob.

---

## Method B — frozen dashboard (deterministic)

### 1. Capture real sessions from a live dashboard

Run the sample (or any profiled app), then snapshot a handful of session details — they are real captured
NHibernate sessions with varied alerts (N+1, writes, unbounded):

```bash
dotnet samples/NHibernaut.Sample.Console/bin/Release/net10.0/NHibernaut.Sample.Console.dll &   # dashboard @ :5005
mkdir -p /tmp/seed
for id in $(curl -s "http://127.0.0.1:5005/api/sessions?take=12" \
            | grep -oE '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}'); do
  curl -s "http://127.0.0.1:5005/api/sessions/$id" -o "/tmp/seed/$id.json"
done
```

### 2. Re-serve them from the standalone host (no churn)

Start `NHibernaut.Server.Host` on a **loopback** bind (loopback needs no auth token) on a free port, and
ingest the captured details. `POST /api/ingest` reconstructs each session verbatim and the store does not
add anything on its own — the set is now **frozen**:

```bash
NHIBERNAUT_BIND=127.0.0.1 NHIBERNAUT_PORT=5006 \
  dotnet src/NHibernaut.Server.Host/bin/Release/net10.0/nhibernaut-dashboard.dll &

for f in /tmp/seed/*.json; do
  curl -s -o /dev/null -w "%{http_code}\n" -X POST -H "Content-Type: application/json" \
    --data-binary @"$f" "http://127.0.0.1:5006/api/ingest"     # expect 202
done
curl -s "http://127.0.0.1:5006/api/sessions?take=200" | grep -oc '"id":"'   # stable count
```

### 3. Run the MCP server against the frozen dashboard and compare

Drive `nhibernaut-mcp --url http://127.0.0.1:5006` (see [above](#driving-the-mcp-server-over-stdio)) with
`tools/call` for `nhibernaut_list_sessions`, `nhibernaut_get_session` (a few diverse ids), and
`nhibernaut_rank_query_shapes`, then diff against the dashboard API. Parse properly:

```python
import json
B = "/tmp/"
frames = {}
for line in open(B + "mcp_out.jsonl", encoding="utf-8"):
    line = line.strip()
    if line:
        o = json.loads(line)
        if isinstance(o, dict) and "id" in o and "result" in o:
            frames[o["id"]] = o

def tool_json(fid):                         # unwrap result.content[0].text → parse inner JSON
    for c in frames[fid]["result"]["content"]:
        if c.get("type") == "text":
            return json.loads(c["text"])

m = tool_json(4)                            # nhibernaut_get_session for some id
d = json.load(open(B + "d_session.json"))   # curl http://127.0.0.1:5006/api/sessions/<id>
assert m["summary"]["statementCount"] == d["summary"]["statementCount"]
assert sorted(a["type"] for a in m["alerts"]) == sorted(a["type"] for a in d["alerts"])
```

Compare three layers: **list** (the MCP session-id set equals the dashboard id set), **detail**
(`summary.statementCount` and the alert-type set match per session), and **aggregate** (top
`normalizedSql` + `executionCount` match). `nhibernaut_get_session` is bounded by `max_statements`
(default 50, max 100), so its returned `statements` array is a **prefix** of the dashboard's — compare
`summary.statementCount`, not array length.

### 4. Point the desktop app at the same dashboard

The desktop auto-connects on launch to `lastConnection` in its settings file
(`%APPDATA%/NHibernaut/settings.json` on Windows; see [Desktop → logs](DESKTOP.md#log-location)). Set the
URL and relaunch so it reads the **same frozen dashboard**:

```jsonc
// %APPDATA%/NHibernaut/settings.json
{ "lastConnection": { "mode": "Remote", "url": "http://127.0.0.1:5006",
                      "token": null, "bindAddress": "127.0.0.1", "port": 5006 },
  "logLevel": "Information", "theme": "Dark" }
```

Confirm (without any GUI automation) that the desktop pulled from the same dashboard by reading its log
(`%LOCALAPPDATA%/NHibernaut/logs/nhibernaut-<date>.log`) — the `HttpClient.dashboard` category logs each
request:

```bash
grep -oE "GET http://127.0.0.1:5006/api/[a-z?*/]+" "$LOCALAPPDATA/NHibernaut/logs/nhibernaut-$(date +%Y%m%d).log" | sort | uniq -c
# -> /api/config, /api/sessions?*, /api/aggregate, /api/stream  ⇒ the desktop reads the same Collector
```

Because the desktop and `nhibernaut-mcp` use the **identical** `HttpDashboardClient` against the identical
endpoints, matching the MCP output to the dashboard API proves it matches what the desktop displays.

---

## Visual verification (optional)

To additionally **screenshot the running desktop** and read its on-screen session list via the
accessibility tree, use the `avalonia-desktop-testing` skill (`Dump-UiaTree.ps1` / `Capture-Window.ps1`).
That path **requires Windows PowerShell** (`powershell.exe`, 5.1) — UI Automation assemblies do not load
under `pwsh` 7.x. If PowerShell is unavailable, the **log-based** check in step 4 is the substitute: it
proves the desktop consumed the same Collector, just without a pixel capture.

```bash
SK="$HOME/.claude/skills/avalonia-desktop-testing/scripts"
PID=$(powershell.exe -Command "(Get-Process nhibernaut-app,dotnet | ? { \$_.MainWindowTitle -eq 'NHibernaut' }).Id")
powershell.exe -ExecutionPolicy Bypass -File "$SK/Capture-Window.ps1" -ProcessId $PID -Out C:/tmp/desktop.png
powershell.exe -ExecutionPolicy Bypass -File "$SK/Dump-UiaTree.ps1"   -ProcessId $PID   # read on-screen text
```

**Robust counter assertion (no pixels).** The desktop toolbar shows process-wide totals that must equal the
dashboard arithmetic, so you can assert equality from the tree dump (or screenshot) against the API:

- toolbar **`queries: N`** == `sum(summary.statementCount)` over `GET /api/sessions`
- toolbar **`alerted: M`** == count of sessions with `alertCount > 0`

This ties the GUI numerically to the exact dataset the MCP server reads.

---

## Cleanup

```bash
# stop the background processes you started (sample, host, desktop)
taskkill //IM dotnet.exe //F        # Windows: kills all dotnet — scope to specific PIDs in shared envs
# or kill the specific PIDs you launched
```

---

## Worked example (verified 2026-06-25, Release build)

Frozen dashboard on `:5006` with **12** real captured sessions (3 N+1 sessions of 419–423 statements with
`SelectNPlusOne`+`TooManyQueries`+`UnboundedResultSet`, plus `WriteWithoutTransaction`, `UnboundedResultSet`,
and clean sessions). All three consumers pointed at it:

| Check | MCP server (`nhibernaut-mcp`) | Dashboard API (`curl`) | Result |
|---|---|---|---|
| `tools/list` | 7 tools, all `nhibernaut_`-prefixed | — | ✅ all present |
| **list** session ids | 12 | 12 | ✅ identical set (diff 0) |
| **detail** N+1 `070df404…` | `statementCount=421`, alerts `{SelectNPlusOne, TooManyQueries, UnboundedResultSet}` | same | ✅ match (MCP returned 100 statements per `max_statements`; count matches) |
| **detail** write `0cd215e0…` | `statementCount=1`, alerts `{WriteWithoutTransaction}` | same | ✅ match |
| **aggregate** top shape | `executionCount=1260`, `SELECT posts0_.blog_id …` | same | ✅ match |
| stdout hygiene | every line a JSON-RPC frame; logs only on stderr | — | ✅ clean |
| desktop (relaunched on `:5006`) | log shows `GET …:5006/api/{config,sessions,aggregate,stream}` | — | ✅ reads the same Collector |
| desktop GUI (PrintWindow + UIA) | toolbar URL `http://127.0.0.1:5006`; `queries: 1272`, `alerted: 9`; rows `423/421/419 queries · 3 alerts`, `1 queries · 1 alert · 150 rows`, `1 queries · 1 alert · 1 writes` | `sum(statementCount)=1272`, `alertCount>0 → 9` | ✅ on-screen totals match the API exactly |

Conclusion: `nhibernaut-mcp` serves exactly the data the dashboard exposes and the desktop app reads —
verified at the list, detail, and aggregate layers, and confirmed visually in the desktop GUI.
