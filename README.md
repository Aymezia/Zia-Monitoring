<div align="center">

![Zia Monitoring](docs/brand/banner.png)

# ⚡ Zia Monitoring

**Windows PC monitoring, optimization, security and maintenance — all in one app**

![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11-blue?style=flat-square)
![Framework](https://img.shields.io/badge/.NET-8.0-purple?style=flat-square)
![UI](https://img.shields.io/badge/UI-WinUI%203-violet?style=flat-square)
![Release](https://img.shields.io/badge/Release-v1.3.0-orange?style=flat-square)
![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)
![Open Source](https://img.shields.io/badge/Open%20Source-Yes-brightgreen?style=flat-square)

</div>

---

## Overview

Zia Monitoring is a comprehensive system utility for Windows 10/11, built in **C# / WinUI 3**. It monitors your PC in real time, runs a full health audit, optimizes Windows for gaming and performance, cleans up disk space, and checks your machine's security — with no mandatory third-party dependency and no account required (everything runs locally).

The app **starts without administrator rights**: it only asks to relaunch elevated for the specific actions that genuinely need it (telemetry/task debloat, firewall kill switch, DNS changes, DISM/SFC, hardware sensors, registry-level tweaks).

---

## Features

### 📊 Real-time dashboard
CPU, RAM, CPU/GPU temperature and usage, VRAM, fan RPM, network up/down and ping, disk I/O, estimated FPS, top processes, and a global health score — with a terminal-style UI where a cold→hot thermal gradient is reserved for real physical values (load, temperature).

### 🖥️ PC mapping
- Full hardware inventory (CPU, GPU, motherboard, BIOS, RAM, disks, uptime)
- Installed games (Steam, Epic, Riot, Battle.net) with playtime and one-click launch
- **Steam library map**: which game sits on which disk, and whether that disk is an SSD or HDD
- Advanced hardware diagnostics: XMP/EXPO profile, single/dual-channel RAM, GPU PCIe link width, SSD wear
- Monitors (resolution / refresh rate / HDR), controllers, **Bluetooth device battery**, and a live **controller stick-drift tester**

### 🩺 Audit & recommendations
- **Full PC audit** that aggregates security, privacy, debloat, startup, stability and hardware signals into a single prioritized list with a **0–100 score**
- Known BSOD stop-code / faulting-module correlation to official sources
- **Weekly health report** (HTML + PDF) with the score trend over time

### 🚀 Boost & Windows tweaks
- Safe boost engine with preview and full rollback
- Startup program manager (StartupApproved — compatible with Task Manager)
- Reversible gaming tweaks: HAGS, Nagle's algorithm, network throttling, system responsiveness, "Ultimate Performance" power plan, SysMain, Fast Startup, visual effects, and **Memory Integrity (HVCI / Core Isolation)**

### 🔒 Security & privacy
Firewall/UAC status, S.M.A.R.T. warnings, obsolete drivers, suspicious startup entries, known malware signatures, keylogger-hook indicators, multiple-antivirus conflict detection, one-click Windows privacy hardening, guided debloat (categorized, reversible), webcam/mic/folder access audit, browser extension audit, Have I Been Pwned password check, per-app network kill switch, and duplicate/trial software audit.

### 🌐 Network
DNS switching (Cloudflare/Google/Quad9) + DoH, latency comparison, VPN detection, traceroute, packet loss, regional datacenter latency, connection geo-location, Wi-Fi channel analysis, Wake-on-LAN, public-IP-change alerts, **per-app network usage history**, and **network-adapter power management** (stop Windows from turning the NIC off mid-game).

### 🧹 Maintenance
Disk space forecast, shader cache / page file analysis, duplicate & large file finder, DISM/SFC repair, service dependencies, boot-time trend, stability diagnostics (app crashes, WHEA, last BSOD, memory-leak suspects, thermal drift), Windows Update history + one-click KB uninstall, TRIM status, disk-optimization audit, warranty CSV export, advanced cleanup (hiberfil.sys, WinSxS, Windows Update cache, Windows.old, old installers, restore points), system hygiene (restart Explorer, repair icon cache, context-menu inventory), **Storage Sense** control, **pending-reboot detector**, and a **DPC/latency probe** (audio crackle / micro-stutter diagnosis).

### 🎮 Gamer & streamer
Active-game detection, automatic silent mode, game booster, per-game launch profiles, per-session reports (FPS, 1% lows, max temps), game-save backup with restore testing, anti-cheat inventory, FPS-drop correlation, and OBS scene auto-switching.

### 🔔 Integrations & notifications
Native Windows toasts (configurable thresholds), daily health summary, **remote phone notifications via ntfy.sh** (no account needed), **Discord Rich Presence**, OBS WebSocket, and a Prometheus exporter.

### ⚙️ Quality of life
Command palette (Ctrl+K), searchable settings, full settings backup/restore (DPAPI-encrypted), in-game overlay, always-on-top mini widget with themes, achievements, guest mode, **eco mode** (collection slows down when the window is hidden), **single-instance** guard, and **automatic updates** from GitHub Releases.

---

## Installation

The app does not require administrator rights to run — it only requests an elevated relaunch for the actions that need it.

### Option 1 — Portable (no installation)
Download `ZiaMonitoring-Portable-vX.Y.Z.zip` from the [Releases](../../releases), extract it anywhere, and run `ZiaMonitoring.App.exe`. It's self-contained (bundled .NET + Windows App SDK runtime), so there's nothing to install first.

### Option 2 — Installer
Download `ZiaMonitoring-Setup.exe` (when provided in a release) and run it. Per-user install with no UAC prompt, a standard entry in "Installed apps", and clean uninstall.

Once installed or on a portable build, Zia can update itself from GitHub Releases (About page → *Update now*, or automatic install on startup — off by default).

---

## Build from source

**Prerequisites:** [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8), Windows 10/11

```powershell
git clone https://github.com/Aymezia/Zia-Monitoring.git
cd "Zia-Monitoring/ZiaMonitoring.App"

# Debug build (whole solution: app + tests)
dotnet build ZiaMonitoring.sln

# Unit tests
dotnet test tests/ZiaMonitoring.Tests/ZiaMonitoring.Tests.csproj

# Portable release build (self-contained)
powershell -ExecutionPolicy Bypass -File .\installer\Build-Portable.ps1

# Installer (requires Inno Setup 6: https://jrsoftware.org/isdl.php)
powershell -ExecutionPolicy Bypass -File .\installer\Build-InnoSetup.ps1
```

The portable executable is produced in `publish\portable\ZiaMonitoring.App.exe`. A .NET 10 migration is prepared — see [docs/MIGRATION-NET10.md](docs/MIGRATION-NET10.md); deferred ideas live in [docs/ROADMAP.md](docs/ROADMAP.md).

---

## Project structure

```
ZiaMonitoring.App/
├── ZiaMonitoring.sln                  # Solution (app + tests)
├── App.xaml.cs                        # Bootstrap, DI container, exception handling, single-instance
├── MainWindow.xaml(.cs)               # Navigation shell + background monitoring loop
├── Core/
│   └── Models/MonitoringModels.cs     # Domain models (snapshot, profile, settings...)
├── Infrastructure/
│   ├── AppLog.cs                      # Shared logger (2 MB rotation, dedup)
│   ├── AdminElevation.cs              # On-demand elevation helper
│   └── Collectors/                    # WMI/Win32 collectors (CPU, GPU, network, disks...)
├── Application/                       # Business services (audit, boost, security, tweaks, cleanup, SQLite history...)
├── ViewModels/
│   └── AppStateViewModel.cs           # Shared MVVM state (CommunityToolkit.Mvvm)
├── Pages/                             # WinUI 3 pages (Dashboard, Mapping, Boost, Security, Network, Maintenance...)
├── tests/ZiaMonitoring.Tests/         # xUnit unit tests (478+)
├── docs/                              # .NET 10 migration guide, roadmap, brand assets
└── installer/                         # Portable + Inno Setup build scripts
```

---

## Tech stack

| Component | Technology |
|---|---|
| Framework | .NET 8 / C# 12 |
| UI | WinUI 3 (Windows App SDK 2.2) |
| Charts | LiveChartsCore 2 (SkiaSharp) |
| Packaging | Portable self-contained + Inno Setup 6 |
| Hardware collection | WMI / Win32 / LibreHardwareMonitor / PerformanceCounters |
| Settings storage | JSON (`%LOCALAPPDATA%\ZiaMonitoring`), secrets encrypted with DPAPI |
| Metrics history | SQLite (`metrics.db`) |
| Tests | xUnit (478+ tests) |

---

## Contributing

Contributions are welcome!

1. Fork the repository
2. Create a branch: `git checkout -b feature/my-feature`
3. Commit: `git commit -m "feat: description"`
4. Push: `git push origin feature/my-feature`
5. Open a Pull Request

---

## License

Released under the **MIT** license. See [LICENSE](LICENSE) for details.
