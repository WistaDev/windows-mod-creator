# WMC - Windows Mod Creator, a open-source application to create your own modded Windows installation

WMC (Windows Mod Creator) is a application thats allows to create your custom Windows installation without prior knowledge. No Sysprep, No NTLite, no manually using DISM.

## Requirements

- Windows 10 x64 or later
- [.NET 8 SDK 9/.NET Desktop Runtime](https://dotnet.microsoft.com/pt-br/download/dotnet/thank-you/sdk-8.0.427-windows-x64-installer)
- [Windows ADK](https://learn.microsoft.com/windows-hardware/get-started/adk-install) with **Deployment Tools** (provides `oscdimg.exe`) (in the future you will don't need it)
- About 15 GB of free space in the working folder or in the SSD storage

## How to build
```
dotnet build WMC.sln -c Release
WMC.App\bin\Release\net8.0-windows\WMC.exe
```

## Workflow

Every tab except **Project** appears once an ISO is loaded.

1. **Project** - pick the ISO and a working folder, then *Load ISO*. WMC extracts it (`iso\`) and scans it for images.
2. **Edition** - the **WIM file** list shows every `.wim` / `.esd` found in the ISO (`install.wim`, `install.esd`, `boot.wim`, ...); pick the one to modify (default: `install.wim`). Then pick an edition and mount it (`DISM /Mount-Image` into `mount\`). An `.esd` is converted to a `.wim` next to it for the chosen edition. Metadata, Apps, Files and Build all work on the selected image.
   **Metadata** - edit each edition: *Name, Description, Display name, Display description, Flags* plus any extra property as `KEY=VALUE` lines (wimlib syntax, e.g. `WINDOWS/PRODUCTNAME=My Windows`). Use *Move up/down* to change the index order and *Remove* to drop editions. Property changes use `wimlib-imagex.exe` (download from https://wimlib.net/ into `tools\wimlib`); reordering/removing (or an ESD source) re-exports the editions with DISM, which is slow. Only available while the image is unmounted.
3. **Apps** - choose the app folder and main executable, fill in the entry, then **Publish**:
   - *Name, Publisher, Version, Icon, Uninstaller path, Modify path*. If the app ships an uninstaller (`unins*.exe`) it is detected automatically.
   - Paths can be relative to the app folder, or a file elsewhere on your PC (it gets copied into the app folder). Arguments are allowed, e.g. `unins000.exe /SILENT`.
   - Files are copied to `Program Files` (or `Program Files (x86)` for 32-bit apps) inside the image.
   - Registered in the offline registry: `Uninstall\<app>` (with `UninstallString` / `ModifyPath`, or `NoRemove` / `NoModify` when not provided) and `App Paths\<exe>`. 32-bit apps on x64 images use `WOW6432Node`.
   - Optional Start Menu / Public Desktop shortcuts.
   **Files** - add files or a folder anywhere in the mounted image. Pick the source (several files, or one folder) and a destination folder inside the image: a preset (`C:\Users\Public\Desktop`, `C:\Users\Default\Desktop`, `C:\Windows\System32`, ...) or any path you type. A README.md on the desktop for every user goes to `C:\Users\Public\Desktop`. Folders are copied as a subfolder with their own name. Copies use `robocopy /B`, so protected folders work without changing permissions in the image. Paths that go through a symlink/junction of the image (e.g. `Documents and Settings`) are refused, because they would write to your PC instead of the image.
4. **Build** - commits and unmounts the image, then builds the ISO with `oscdimg` (BIOS + UEFI).

Project state lives in `wmc-project.json` inside the working folder (*Open project...* resumes it).

## Layout

```
WMC.Core   engine: ProcessRunner, IsoService, WimService (DISM), OfflineHive (reg load/unload),
           AppInstaller, FileInjector, WimMetadata, IsoBuilder (oscdimg), PeInfo, WmcProject
WMC.App    WinForms front end, classic Win32 look (visual styles intentionally disabled)
```

## License
The WMC source-code are under the GPL-3 license.