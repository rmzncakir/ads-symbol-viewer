# ADS Symbol Viewer

A lightweight Windows desktop tool for browsing, reading, writing and live-watching
TwinCAT PLC variables over **ADS** (Automation Device Specification).

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![Framework](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4)
![UI](https://img.shields.io/badge/UI-WPF-0078D4)
![License](https://img.shields.io/badge/license-MIT-green)
![Built with Claude](https://img.shields.io/badge/built%20with-Claude-cc785c)

## About

This project exists to give PLC developers and commissioning engineers a
**simple, free tool** for inspecting and manipulating TwinCAT variables —
without opening the full TwinCAT XAE environment: connect, browse the symbol
tree, read or write a value, and keep an eye on critical signals with the
live watch list.

It was developed together with **[Claude](https://claude.com/claude-code)**
(Anthropic's AI coding assistant) and is **open to everyone** — free to use,
modify and share under the MIT license.

## Features

- **Connect** to any local or remote TwinCAT runtime by AMS NetId + ADS port
  (recent connections are remembered between sessions)
- **Symbol tree** — lazily loaded browser for all PLC symbols, including nested
  structs and array elements, with type badges and a live filter box
- **Read / write** values for all common IEC types:
  `BOOL`, `BYTE`, `(U)SINT`, `(U)INT`, `WORD`, `(U)DINT`, `DWORD`, `(U)LINT`,
  `REAL`, `LREAL`, `STRING`, `WSTRING`, `TIME`, `DATE`, `TOD`, `DT`
  (BOOL gets a TRUE/FALSE toggle button)
- **Watch list** — poll selected symbols at 200 ms – 5 s intervals; the
  timestamp column shows when each value last changed
- **Status badges** for the connection and the PLC ADS state (RUN / STOP / CONFIG / ERROR)
- **CSV export** of the full symbol table
- **Activity log** of every connect, read, write and error

## Requirements

| Component | Notes |
|---|---|
| Windows 10/11 | |
| .NET Framework 4.8 | preinstalled on current Windows versions |
| TwinCAT ADS router | any of: full TwinCAT XAE/XAR, TwinCAT ADS runtime, or TC1000 ADS setup — required so the app can reach a PLC |

The `TwinCAT.Ads` library itself is pulled automatically from NuGet
([`Beckhoff.TwinCAT.Ads` 4.4.46](https://www.nuget.org/packages/Beckhoff.TwinCAT.Ads/4.4.46)),
so no local TwinCAT installation is needed just to **build**.

## Building

Open `AdsSymbolViewer.slnx` in Visual Studio 2022 or later and build, or from a
*Developer Command Prompt*:

```cmd
msbuild AdsSymbolViewer\AdsSymbolViewer.csproj /restore /p:Configuration=Release
```

The executable is produced at `AdsSymbolViewer\bin\Release\AdsSymbolViewer.exe`.

## Usage

1. Start the app and enter the target **AMS NetId** (defaults to the local one)
   and the **ADS port** (`851` for the first TwinCAT 3 PLC runtime).
2. Click **Connect**, then **Load Symbols**.
3. Browse or filter the tree on the left; selecting a symbol shows its type,
   size and index group/offset, and loads its current value.
4. Use **Read** / **Write** for one-shot access, or **+ Watch** to add the
   symbol to the polling watch list.

> **Note for remote targets:** an ADS route to the remote device must exist
> (add it via *TwinCAT → Router → Edit Routes* or the target's IPC diagnostics
> page) before connecting to a non-local AMS NetId.

## Project layout

| File | Purpose |
|---|---|
| `AdsService.cs` | All ADS communication: connect/disconnect, symbol enumeration, typed raw read/write |
| `MainWindow.xaml(.cs)` | WPF UI: connection bar, symbol tree, detail panel, watch list, log |

## Authors

- **Ramazan ÇAKIR** — [github.com/rmzncakir](https://github.com/rmzncakir)
- **Claude** (Anthropic) — AI pair programmer, [claude.com/claude-code](https://claude.com/claude-code)

## License

Released under the [MIT License](LICENSE) — free for everyone to use,
modify and distribute.
