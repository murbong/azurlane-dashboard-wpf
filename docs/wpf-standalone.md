# WPF publish

The WPF app can be published without Python.

Default publish:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish_wpf.ps1 -Clean -Zip
```

Output:

```text
dist\wpf-publish\win-x64\
```

This is a framework-dependent publish. The target PC must have the x64 .NET
Desktop Runtime for `net10.0-windows`.

Standalone publish:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build_wpf_standalone.ps1 -Clean -Zip
```

Output:

```text
dist\wpf-standalone\win-x64\
```

This is self-contained and carries the .NET runtime, so it is much larger.

Both published folders include:

- `AzurLaneDashboard.App.exe`
- `defaults\maps\*.json` seed metadata files
- native C# AssetBundle texture decoding dependencies

The published folder does not include extracted game PNG assets from
`static\game-assets`. Icons and paintings are optional cache data. Missing images
must not stop the app from loading.

On first launch outside the source tree, the app uses the published folder as
its content root and creates:

```text
dist\wpf-publish\win-x64\maps\*.json
dist\wpf-publish\win-x64\azurlane_tcp_export\inventory.sqlite
dist\wpf-publish\win-x64\settings.json
```

`defaults\maps` is read-only seed data for new installs. Runtime updates write
to `maps`, and later app publishes must not overwrite files under `maps`.

The published folder stays self-contained even when it is run from inside this
repository's `dist` directory. It must not walk upward and use the source
project root.

Runtime requirements:

- No Python runtime is required.
- `dumpcap` from Wireshark/Npcap or `adb tcpdump` is still required for live capture.
- `git` is required only when running the native update flow.
- First-time game resource sync needs `adb.exe` or `MuMuManager.exe`. If the
  Android tool exists but MuMu is not running or the serial is offline, the app
  prompts before update so the user can start MuMu and retry, skip game resource
  sync for that run, or cancel.
- C# extracts `Texture2D` PNG assets from local AssetBundles during the native
  update flow.
