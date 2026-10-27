# WMC - Windows Mod Creator

Think Cubic, but for Windows: take an official ISO, mount the image, apply offline modifications and build a new bootable ISO.

## Requirements

- Windows 10/11 x64, run as **Administrator** (the manifest requests elevation)
- .NET 8 SDK
- [Windows ADK](https://learn.microsoft.com/windows-hardware/get-started/adk-install) with **Deployment Tools** (provides `oscdimg.exe`)
- About 15 GB of free space in the working folder

## Build and run

```
dotnet build WMC.sln -c Release
WMC.App\bin\Release\net8.0-windows\WMC.exe
```

## Workflow

The **Edition**, **Apps** and **Build** tabs appear once an ISO is loaded.

1. **Project** - pick the ISO and a working folder, then *Load ISO*. WMC extracts it (`iso\`) and reads the editions from `install.wim` / `install.esd`.
2. **Edition** - pick an edition and mount it (`DISM /Mount-Image` into `mount\`). An `install.esd` is converted to `install.wim` for the chosen edition.
3. **Apps** - choose the app folder and main executable, fill in the entry, then **Publish**:
   - *Name, Publisher, Version, Icon, Uninstaller path, Modify path*. If the app ships an uninstaller (`unins*.exe`) it is detected automatically.
   - Paths can be relative to the app folder, or a file elsewhere on your PC (it gets copied into the app folder). Arguments are allowed, e.g. `unins000.exe /SILENT`.
   - Files are copied to `Program Files` (or `Program Files (x86)` for 32-bit apps) inside the image.
   - Registered in the offline registry: `Uninstall\<app>` (with `UninstallString` / `ModifyPath`, or `NoRemove` / `NoModify` when not provided) and `App Paths\<exe>`. 32-bit apps on x64 images use `WOW6432Node`.
   - Optional Start Menu / Public Desktop shortcuts.
4. **Build** - commits and unmounts the image, then builds the ISO with `oscdimg` (BIOS + UEFI).

Project state lives in `wmc-project.json` inside the working folder (*Open project...* resumes it).

## Layout

```
WMC.Core   engine: ProcessRunner, IsoService, WimService (DISM), OfflineHive (reg load/unload),
           AppInstaller, IsoBuilder (oscdimg), PeInfo, WmcProject
WMC.App    WinForms front end, classic Win32 look (visual styles intentionally disabled)
```
