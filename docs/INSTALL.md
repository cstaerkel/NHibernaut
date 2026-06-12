# Deploying the NHibernaut Dashboard as a service

There are two ways to consume NHibernaut:

1. **As a library** (the usual way) — add the NuGet packages to your app and call
   `EnableNHibernaut()`. See the [README](../README.md). The dashboard runs *in-process*.
2. **As a standalone dashboard service** (this guide) — install a self-contained web server that
   hosts the dashboard at `http://<host>:5005`, run from native OS installers.

> **What gets installed:** the `nhibernaut-dashboard` background service (a self-contained build of
> `NHibernaut.Server.Host`). It serves the same dashboard SPA + JSON API + SSE as the in-process
> server. Its data store starts **empty** — your apps feed it by **forwarding** their sealed sessions
> to it (see *Feed the dashboard* below). Installers are attached to each
> [GitHub Release](https://github.com/cstaerkel/NHibernaut/releases).

## Feed the dashboard (remote forwarding)

A deployed dashboard only shows data that apps send it. In each profiled app, turn on forwarding
right after enabling capture:

```csharp
cfg.EnableNHibernaut();                                                  // capture (Tier A)
NHibernaut.Server.RemoteForwarder.Enable("http://dashboard-host:5005", "<auth-token>");
```

Each time a session seals, the app ships it to the dashboard's `POST /api/ingest`. Forwarding is
asynchronous, bounded, and fail-safe — if the dashboard is unreachable, sessions are dropped rather
than queued without bound, and it never blocks or throws into your app. Pass the same
`NHIBERNAUT_AUTH_TOKEN` the dashboard was configured with (required whenever it binds beyond
loopback). Referencing `NHibernaut.Server` is enough; the app does not host its own dashboard.

---

## Install

### Linux (`.deb` / `.rpm`)

```bash
# Debian/Ubuntu
sudo dpkg -i nhibernaut-dashboard_<version>-1_amd64.deb
# RHEL/Fedora/SUSE
sudo rpm -i nhibernaut-dashboard-<version>-1.x86_64.rpm

# configure (see Configuration below), then:
sudo systemctl enable --now nhibernaut-dashboard
```

### Windows (`.msi`)

```powershell
msiexec /i nhibernaut-dashboard-<version>-win-x64.msi /quiet
# installs and starts the "NHibernautDashboard" Windows service
```

### macOS (`.pkg`)

```bash
sudo installer -pkg nhibernaut-dashboard-<version>-osx-arm64.pkg -target /
# installs and loads the com.nhibernaut.dashboard LaunchDaemon
```

---

## Configuration

The service is configured with three environment variables:

| Variable | Default | Meaning |
|---|---|---|
| `NHIBERNAUT_BIND` | `0.0.0.0` | Bind address. Use `127.0.0.1` to restrict to loopback. |
| `NHIBERNAUT_PORT` | `5005` | TCP port. |
| `NHIBERNAUT_AUTH_TOKEN` | *(empty)* | Required when bound to a non-loopback address (see below). |

**Auth token requirement.** The dashboard exposes SQL and parameter values, so the server **refuses
to bind a non-loopback address without a token**. With the default `0.0.0.0` bind and no token set,
the service **generates a random token on start and logs it** — then enforces it on every request
(`X-NHibernaut-Token` header or `?token=`). Find the generated token in the service log, or set your
own `NHIBERNAUT_AUTH_TOKEN` for a stable value.

Where to set the variables and read the log, per platform:

- **Linux:** edit `/etc/nhibernaut-dashboard/dashboard.env`, then
  `sudo systemctl restart nhibernaut-dashboard`. Logs: `journalctl -u nhibernaut-dashboard`.
- **Windows:** set the service's **per-service** environment block (a `REG_MULTI_SZ` named
  `Environment`, one `NAME=value` per line) — the Service Control Manager applies it on the next
  service start. In an elevated PowerShell:

  ```powershell
  $svc = "HKLM:\SYSTEM\CurrentControlSet\Services\NHibernautDashboard"
  New-ItemProperty -Path $svc -Name Environment -PropertyType MultiString `
    -Value @("NHIBERNAUT_AUTH_TOKEN=<your-token>", "NHIBERNAUT_BIND=0.0.0.0") -Force
  Restart-Service NHibernautDashboard
  ```

  Machine-level environment variables (set by the MSI or System Properties) are **not** re-read by
  an already-installed service on `Restart-Service` — they take effect only after a **reboot**, so
  prefer the per-service block above. Logs: Windows **Event Viewer → Application**.
- **macOS:** edit `EnvironmentVariables` in
  `/Library/LaunchDaemons/com.nhibernaut.dashboard.plist`, then
  `sudo launchctl unload <plist> && sudo launchctl load -w <plist>`. Logs:
  `/var/log/nhibernaut-dashboard.log`.

---

## Manage / uninstall

| | Linux | Windows | macOS |
|---|---|---|---|
| Start | `systemctl start nhibernaut-dashboard` | `Start-Service NHibernautDashboard` | `launchctl load -w <plist>` |
| Stop | `systemctl stop nhibernaut-dashboard` | `Stop-Service NHibernautDashboard` | `launchctl unload <plist>` |
| Uninstall | `apt remove` / `rpm -e nhibernaut-dashboard` | Apps & Features, or `msiexec /x` | `launchctl unload <plist>` + delete files under `/usr/local/nhibernaut-dashboard` and the plist |

---

## Security & signing caveats

- **Unsigned artifacts.** These installers are **not code-signed or notarized**:
  - **Windows:** SmartScreen may warn ("Windows protected your PC" → *More info* → *Run anyway*).
  - **macOS:** Gatekeeper **blocks unsigned packages that install a root LaunchDaemon**. Installing
    via `sudo installer -pkg … -target /` from a trusted terminal is the supported path; the
    double-click GUI flow may be refused. Signing + notarization with an Apple Developer ID is a
    planned follow-up.
- **Treat the dashboard as sensitive.** It shows SQL and parameter values. Always set
  `NHIBERNAUT_AUTH_TOKEN` when binding beyond loopback, and prefer to expose it only on a trusted
  network or behind a reverse proxy with TLS.
