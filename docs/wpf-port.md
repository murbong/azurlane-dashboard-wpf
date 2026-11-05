# WPF MVVM port

The WPF port starts as a native read-only viewer for the existing
`azurlane_tcp_export/inventory.sqlite` database.

## Current slice

Project:

```text
src/AzurLaneDashboard.App/
```

Implemented:

- .NET 8 WPF application
- MVVM structure without WebView
- WPF UI (`WPF-UI` NuGet package) Fluent theme resources
- dark Fluent shell with left navigation, top toolbar, KPI cards, and dense grids
- SQLite read service using `Microsoft.Data.Sqlite`
- Dock table from `ships`
- Equipment table from `equipment`
- Summary cards from `meta.summary_json`
- Search filter across dock and equipment
- Database picker

Not implemented yet:

- live capture start/stop
- dumpcap interface selector
- packet decoding in C#
- ship detail recommendations
- battle/system queues
- local game resource extraction
- Mesh + UV painting reconstruction in C#
- installer/code signing

## Build

```powershell
dotnet build .\AzurLaneDashboard.slnx
```

The app targets `net10.0-windows`.

Default publish:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish_wpf.ps1 -Clean
```

See `docs/wpf-standalone.md` for the Python-free publish layout.

## Design Direction

The WPF port uses a native Fluent-style desktop shell instead of WebView:

- left navigation rail
- top search/action toolbar
- compact KPI cards
- virtualized dock/equipment `DataGrid`
- right-side ship detail panel on the dock view
- dark mode first

The design should stay dense and operational. Avoid marketing-style hero sections
or oversized cards; this app is used repeatedly while the game is running.

## Roadmap

1. Add settings persistence.
2. Add dumpcap interface list and capture process control.
3. Port packet frame parsing and SQLite write path.
4. Port decoded packet handling by protocol area.
5. Port map/resource update flow.
6. Add native installer and code signing.
