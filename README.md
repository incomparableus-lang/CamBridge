# CamBridge

Makes an ordinary DirectShow webcam visible to applications that use
Media Foundation — the Windows Camera app, Teams, Zoom, browsers, Discord.

Windows exposes such a camera only if it registers itself in the
`KSCATEGORY_VIDEO_CAMERA` category. Modern UWP/webcam drivers do; legacy WDM
drivers from the 2000s do not. Those cameras still work in DirectShow-only
programs, but every modern application sees an empty list.

CamBridge bridges the two. It keeps the physical camera untouched and publishes
an additional virtual camera next to it, fed by the real one:

```
physical camera (DirectShow only)          CamBridge
  USB\VID_0458&PID_7029  ──►  ffmpeg  ──►  named pipe  ──►  Media Foundation
                                    dshow        (user-mode       virtual camera
                                                  media source)   KSCATEGORY_VIDEO_CAMERA
```

The virtual camera uses the `MFCreateVirtualCamera` API (Windows 11, build
22000 and later), so it needs no kernel driver and no driver signing.

## Why the driver cannot just be fixed

The reference machine has a Genius/KYE *Look 320S*. Its USB descriptor reports
`Class_FF&SubClass_FF&Prot_FF` — vendor-specific, **not** UVC. Windows' built-in
USB Video Class driver can never bind to it, and its only working driver is a
2006-era Sonix WDM driver that registers under `KSCATEGORY_CAPTURE` only.
Registration happens inside the driver at device start; there is no registry key
that can override it. A virtual camera is the only route.

## Requirements

- Windows 11, build 22000 (21H2) or newer — `MFCreateVirtualCamera` does not exist earlier
- x64
- Administrator rights for installation
- The camera must appear in DirectShow (`ffmpeg -list_devices true -f dshow -i dummy`)

## Install

Download `CamBridge-Setup.exe` from the releases, run it, pick your camera and a
resolution. The installer is self-contained: it carries its own ffmpeg.

Mirrors: **GitHub** `incomparableus-lang/CamBridge` · **GitVerse** `MEdb/CamBridge`.

For unattended deployment:

```
CamBridge-Setup.exe /silent
```

Afterwards the camera is available under the name `<device> (CamBridge)`.
CamBridge itself runs from the tray and starts automatically at logon; the
physical camera and its driver are never modified. Uninstall from
*Settings → Apps → CamBridge*.

## Languages

Russian, Belarusian, Korean, Persian, Chinese, Hindi. The language follows the
system on first run and can be changed in the tray settings or in the installer.

Persian switches the interface to right-to-left. If Hindi renders as boxes, the
Devanagari font (Nirmala UI) is missing — install the *Basic Hindi* language pack.

## Build from source

No Visual Studio, no MSBuild, no NuGet — just `csc.exe` from the .NET Framework
and PowerShell.

```powershell
git clone https://github.com/incomparableus-lang/CamBridge.git
cd CamBridge
.\fetch-deps.ps1      # downloads ffmpeg + the LocosLab media source (~115 MB)
.\build.ps1           # -> dist\CamBridge-Setup.exe
```

`fetch-deps.ps1` pulls the two third-party pieces; neither is committed to this
repository. `build.ps1` reads the version from the `VERSION` file and embeds it
into the binaries.

Run `tools\lang-dump.cs` against `src\Strings.cs` to dump every UI string in all
six languages and check for missing keys.

## How the pieces fit

| File | Role |
|---|---|
| `src\BridgeCore.cs` | frame source, Media Foundation feed, config, ffmpeg and DirectShow discovery |
| `src\Strings.cs` | UI strings for all six languages |
| `src\CamBridge.cs` | tray application and settings dialog |
| `src\Setup.cs` | self-extracting installer and wizard |
| `src\Uninstaller.cs` | uninstaller |

The installer stub finds its appended zip payload by scanning for the ZIP
end-of-central-directory record near the end of the executable, so the stub
stays a valid PE and needs no bookkeeping of its own length.

## Known limitations

- The camera really delivers ~10 fps at 640x480; the bridge does not invent frames.
- 1280x960 from the reference camera is interpolated, not native.
- Only the first DirectShow camera is picked by `/silent`; use the wizard otherwise.
- The installer is unsigned, so SmartScreen on a fresh machine may warn.

## License

GPL-3.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE).

Bundled third-party components are redistributed under their own licenses —
ffmpeg (GPL) and the LocosLab Virtual Camera media source (MIT).
