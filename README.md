# C3850 GUI

A native Windows desktop manager for Cisco Catalyst 3850 switch stacks (and, in practice, most IOS-XE switches).
Fast, dark-themed Fluent UI, built-in terminal, and a **live Command Explorer** that exposes every command and
parameter your switch's exact IOS-XE build supports.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4) ![WPF](https://img.shields.io/badge/UI-WPF%20%2B%20WPF--UI-2E8BFF) ![License](https://img.shields.io/badge/license-MIT-green)

## Features

| Page | What it does |
|---|---|
| **Dashboard** | Version, model, uptime, CPU / memory, PoE budget, stack members, recent log |
| **Ports** | Front-panel view + table from `show interfaces status`. Multi-select → enable / disable / bounce / description / access or voice VLAN / trunk / PoE / speed-duplex / arbitrary interface-mode config. Details, running-config, counters, MACs per port |
| **VLANs** | Add / rename / delete VLANs, create SVIs, view trunks and spanning-tree |
| **Stack** | Members, roles, priorities, stack ports, redundancy, environment, inventory. Set priority, renumber, reload a member, force switchover, save, reload stack |
| **PoE** | Per-member budget, per-port draw. Auto / never / power-cycle a device |
| **MAC Table** | Searchable MAC table, ARP, CDP / LLDP neighbours, DHCP-snooping bindings |
| **Command Explorer** | Browses the switch's parser tree live by sending `?` — in EXEC, global config, interface, interface-range, VLAN, line, router or any custom mode. Every token and parameter your IOS version knows, with its help text. Build commands by clicking, get help at the cursor with `?` / Ctrl+Space, run single commands or batches |
| **Terminal** | Raw CLI on the *same* session the GUI uses, so every command the app sends is visible. Tab completion, `?`, history keys, selection / copy / paste, scrollback save. One terminal per open switch |
| **Configuration** | Running / startup config, save (`write memory`), local backups, diff running vs startup or vs a backup file, push a block of config lines |
| **Logs** | `show logging` with filtering and auto-refresh, flap filter, err-disable view, `show tech-support` to file |
| **Activity** | Audit trail of every command the app sent with full output |
| **Connections** | Multiple stacks: name, management IP / port, username, password or SSH key, enable secret. Secrets are stored with Windows DPAPI (per-user) |

Anything the curated pages don't cover is one click away in the Command Explorer or the Terminal — nothing is
hidden from you.

## Install

Download `C3850-GUI-Setup-<version>.exe` from [Releases](https://github.com/SirBiggin/C3850-GUI/releases) and run it.
The app is self-contained (no .NET install required), per-user by default, and adds Start-menu / optional desktop shortcuts.

Requirements: Windows 10 1809+ / Windows 11 (x64). The switch needs SSH enabled (`ip ssh version 2`, `transport input ssh`).

## First run

1. **Connections** → **New**. Enter a display name, the stack's management IP (SVI or the dedicated management port), the
   SSH port, username, and password (or a private key). If your user doesn't land in privileged mode, add the enable secret.
2. **Save & connect**. The app runs `terminal length 0` / `terminal width 0` on connect so output is never paged.
3. Add more profiles for more stacks; switch between them with the picker in the title bar. Sessions stay open in the
   background while you move between stacks.

## Build from source

```powershell
git clone https://github.com/SirBiggin/C3850-GUI.git
cd C3850-GUI
dotnet build src\C3850GUI\C3850GUI.csproj        # debug build
.\build.ps1                                       # self-contained publish + Inno Setup installer → dist\
.\build.ps1 -NoInstaller                          # just dist\publish\C3850GUI.exe
```

Needs the .NET 8 SDK; `build.ps1` needs [Inno Setup 6](https://jrsoftware.org/isinfo.php) for the installer step
(`winget install JRSoftware.InnoSetup`).

## How it works

* **SSH.NET** opens one interactive shell per switch. Programmatic commands are serialised through a lock and wait for
  the IOS prompt; their output also streams to the Terminal page.
* The **Command Explorer** sends `<prefix> ?` and parses the two-column help IOS prints, then clears the line with
  Ctrl-U. For sub-modes it enters the mode (`configure terminal`, `interface X`, …), queries, and returns with `end`.
  Results are cached per node until you reload.
* Config changes go through `configure terminal … end`; anything that prints a `% …` error is reported as a failure.
* Profiles live in `%LOCALAPPDATA%\C3850GUI\settings.json`; passwords inside it are DPAPI-protected blobs bound to your
  Windows account. Config backups go to `%LOCALAPPDATA%\C3850GUI\backups\<profile>\`.

## Safety notes

* Config commands prompt for confirmation by default (toggle in Settings).
* Destructive actions (reload, renumber, switchover, delete VLAN, clear logging) always confirm, regardless of that setting.
* Host keys are currently trusted on first use without pinning.

## License

MIT
