External programs used by WMC. Put them in this folder (or in a subfolder, e.g. tools\wimlib\).

  oscdimg.exe          Needed by "Build ISO". Copy it from a PC that has the Windows ADK:
                       C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\amd64\Oscdimg\oscdimg.exe
                       (or install the ADK with only "Deployment Tools" selected instead).

  wimlib-imagex.exe    Needed by the Metadata tab to change edition properties (name, description, ...).
  libwim-15.dll        Goes next to wimlib-imagex.exe. Windows x64 binaries: https://wimlib.net/

WMC looks here first, then (for oscdimg) in the installed ADK and on PATH.
