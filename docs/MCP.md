# NHibernaut MCP server

`nhibernaut-mcp` is a local Model Context Protocol server for AI clients. It connects to an existing
NHibernaut dashboard API and exposes read-only tools, resources, and prompts for profiling
NHibernate sessions.

The MCP server does not capture data by itself. Start one of the normal NHibernaut dashboard hosts
first, then point the MCP server at that dashboard.

---

## Prerequisites

- A running NHibernaut dashboard API:
  - in-process `NHibernautServer.Start()` at `http://127.0.0.1:5005`
  - the standalone `nhibernaut-dashboard` service
  - the desktop app embedded collector
- A local AI client that can launch stdio MCP servers.
- A dashboard auth value when the target dashboard is configured to require one.

---

## Install and run

Install the tool package, then configure your MCP client to run `nhibernaut-mcp` over stdio:

```bash
dotnet tool install --global NHibernaut.Mcp
```

For local package testing from this repository:

```bash
dotnet pack -c Release -o nupkgs
dotnet tool install --global NHibernaut.Mcp --add-source ./nupkgs
```

Run against the default local dashboard:

```bash
nhibernaut-mcp --url http://127.0.0.1:5005
```

Most MCP clients use a JSON configuration shape like this:

```json
{
  "mcpServers": {
    "nhibernaut": {
      "command": "nhibernaut-mcp",
      "args": ["--url", "http://127.0.0.1:5005"]
    }
  }
}
```

When the dashboard requires auth, pass it as an environment variable or CLI argument:

```json
{
  "mcpServers": {
    "nhibernaut": {
      "command": "nhibernaut-mcp",
      "args": ["--url", "http://dashboard-host:5005"],
      "env": {
        "NHIBERNAUT_DASHBOARD_TOKEN": "<dashboard-token>"
      }
    }
  }
}
```

`nhibernaut-mcp` uses stdio for the MCP protocol. Diagnostics are written to stderr so stdout remains
reserved for protocol messages.

---

## Dashboard targets

### Local in-process dashboard

Start capture and the dashboard in your app:

```csharp
cfg.EnableNHibernaut();
NHibernautServer.Start();
```

Then run the MCP server with the loopback URL:

```bash
nhibernaut-mcp --url http://127.0.0.1:5005
```

### Standalone dashboard service

The standalone `nhibernaut-dashboard` service exposes the same dashboard API. If the service has
`NHIBERNAUT_AUTH_TOKEN` configured, give the MCP server the same value through
`NHIBERNAUT_DASHBOARD_TOKEN` or `--token`:

```bash
NHIBERNAUT_DASHBOARD_URL=http://dashboard-host:5005 \
NHIBERNAUT_DASHBOARD_TOKEN=<dashboard-token> \
nhibernaut-mcp
```

PowerShell:

```powershell
$env:NHIBERNAUT_DASHBOARD_URL = "http://dashboard-host:5005"
$env:NHIBERNAUT_DASHBOARD_TOKEN = "<dashboard-token>"
nhibernaut-mcp
```

### Desktop embedded collector

When the desktop app runs in embedded collector mode, it starts an in-process dashboard API on the
configured bind address and port. Use that same URL from the MCP server:

```bash
nhibernaut-mcp --url http://<desktop-host>:<port> --token <dashboard-token>
```

Loopback embedded collectors normally do not require auth. Non-loopback embedded collectors require an
auth value and, on Windows, the same URL reservation described in the desktop guide.

---

## Configuration

CLI arguments:

| Argument | Default | Meaning |
|---|---|---|
| `--url` | `http://127.0.0.1:5005` | Dashboard API URL. |
| `--token` | *(empty)* | Dashboard auth value sent to the API. |
| `--timeout-ms` | `10000` | HTTP timeout for dashboard API calls. |
| `--max-output-chars` | `25000` | Maximum text returned by a tool or resource before truncation. |

Environment variables:

| Variable | Meaning |
|---|---|
| `NHIBERNAUT_DASHBOARD_URL` | Dashboard API URL. Used when `--url` is absent. |
| `NHIBERNAUT_DASHBOARD_TOKEN` | Dashboard auth value. Used when `--token` is absent. |
| `NHIBERNAUT_AUTH_TOKEN` | Fallback dashboard auth value for parity with the standalone service. |
| `NHIBERNAUT_MCP_TIMEOUT_MS` | HTTP timeout when `--timeout-ms` is absent. |
| `NHIBERNAUT_MCP_MAX_OUTPUT_CHARS` | Output cap when `--max-output-chars` is absent. |
| `NHIBERNAUT_MCP_ALLOW_SENSITIVE` | Set to `1` to allow parameter values and stack traces when a tool also requests them. |

Precedence is CLI first, then MCP-specific environment variables, then defaults. For the auth value,
`NHIBERNAUT_AUTH_TOKEN` is only a fallback after `--token` and `NHIBERNAUT_DASHBOARD_TOKEN`.

---

## Tools

All tools are read-only. Markdown is the default response format; pass `response_format="json"` when
your AI workflow needs structured data.

| Tool | Use |
|---|---|
| `nhibernaut_list_sessions` | List recent sessions and choose a session to inspect. |
| `nhibernaut_get_session` | Read one session summary, alerts, and bounded statements. |
| `nhibernaut_list_alerts` | List alerts globally or for one session. |
| `nhibernaut_rank_query_shapes` | Rank normalized SQL shapes by total time, average time, count, or N+1 incidence. |
| `nhibernaut_compare_sessions` | Compare two sessions for before/after validation. |
| `nhibernaut_get_statement` | Inspect one statement plus related entity loads, writes, and collection initializations. |
| `nhibernaut_summarize_session` | Produce an evidence-based summary and next MCP calls for one session. |

Typical workflow:

1. Call `nhibernaut_list_sessions`.
2. Call `nhibernaut_get_session` for the highest-severity or most expensive session.
3. Use `nhibernaut_get_statement`, `nhibernaut_rank_query_shapes`, or
   `nhibernaut_compare_sessions` for the next focused question.

---

## Resources

Resources provide model-readable context. Use tools for targeted analysis and resources when your MCP
client wants to attach dashboard context.

| Resource | Content |
|---|---|
| `nhibernaut://config` | MCP-visible connection settings, excluding credential values. |
| `nhibernaut://sessions` | Bounded recent session summaries. |
| `nhibernaut://sessions/{session_id}` | One session detail with the same sensitive-output defaults as `nhibernaut_get_session`. |
| `nhibernaut://aggregate` | Bounded aggregate query-shape ranking. |
| `nhibernaut://alerts` | Bounded alert feed. |

---

## Prompts

Prompts guide an AI client through common NHibernate profiling workflows. They do not embed captured
session data; they instruct the client which tools to call.

| Prompt | Use |
|---|---|
| `triage_recent_nhibernate_activity` | Start from recent sessions and summarize the top issues. |
| `explain_session_alerts` | Explain alerts and related statements for one session. |
| `compare_nhibernate_sessions` | Compare two sessions for regression or improvement evidence. |
| `investigate_n_plus_one` | Inspect N+1 evidence across alerts, statements, and query shapes. |

---

## Sensitive data model

NHibernaut profiling data can include SQL, parameter values, entity ids, and stack traces.

- SQL text is included by default because it is the main profiling evidence. Pass `include_sql=false`
  to suppress it for session reads.
- Parameter values are hidden unless the tool call sets `include_parameters=true` and the process has
  `NHIBERNAUT_MCP_ALLOW_SENSITIVE=1`.
- Stack traces are hidden unless the tool call sets `include_stack_traces=true` or
  `include_stack_trace=true` and the process has `NHIBERNAUT_MCP_ALLOW_SENSITIVE=1`.
- `nhibernaut://config` reports whether auth is configured but never returns the auth value.

If in doubt, keep `NHIBERNAUT_MCP_ALLOW_SENSITIVE` unset and ask for statement ids, query shapes,
alert titles, row counts, and timing first.

---

## Troubleshooting

### Could not connect to the dashboard

Start the dashboard first. For local development, make sure your app has called
`NHibernautServer.Start()` or that the desktop embedded collector is running. For a deployed service,
check the service status and the configured bind/port.

### Unauthorized

The dashboard requires auth. Set `NHIBERNAUT_DASHBOARD_TOKEN` in the MCP client environment or pass
`--token <dashboard-token>`.

### Empty sessions

The MCP server can only report captured data. Generate activity in a profiled app, run the console
sample, or enable forwarding to the standalone service or desktop embedded collector. Then call
`nhibernaut_list_sessions` again.

### Output is truncated

Reduce the requested `take`, `limit`, or `max_statements`, or increase `--max-output-chars`. Truncated
responses include a follow-up hint.
