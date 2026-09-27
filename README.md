# diCAN

Free and open-source CAN analyser software for Windows and macOS, supporting DSD TECH
SH-C3x, standard CANable and compatible Elmue Multiboard 2.5 USB-CAN adapters.

One adapter, one bus: receive aggregated by id or as a frame stream, periodic and one-shot
transmit, recording to a database and export.

---

## What it does

- **Two views of the same traffic** — a table aggregated by CAN id (count, mean period, changed
  payload bytes highlighted), and a raw frame stream.
- **Transmit queue** — any number of rows, each with its own id, payload, period and repeat count.
  Classic, CAN FD, FD with bit-rate switch, and remote frames.
- **Host-side filtering** — `id:mask` pairs, applied at render time, so nothing already captured is
  thrown away when you change the filter.
- **Recording and export** — frames are written to a SQLite database while recording, then exported
  to **CSV** or **PEAK `.trc`**.
- **Session log** — every lifecycle event and adapter report, with repeats folded, so a support
  mail has something behind it.
- **Nine interface languages** — English, Simplified Chinese, Traditional Chinese, Japanese,
  French, German, Spanish, Italian and Portuguese, switched from the menu.

## Supported device list

1. **DSD TECH SH-C30x series** — SH-C30A, SH-C30G and SH-C30L.
2. **DSD TECH SH-C31x series** — SH-C31A and SH-C31G.
3. **Standard CANable 1.0 and 2.0 adapters.**
4. **Other adapters running Elmue Multiboard 2.5 firmware.**
5. **DSD TECH SH-C32x series** — the upgraded version of SH-C31x, including SH-C32A and SH-C32B.

**CAN FD depends on the firmware the adapter is running, not on its model or its MCU.** Some
SH-C31x units shipped with a factory `canable2` firmware that does not report CAN FD; diCAN does
not offer it on those. The adapter is asked what it can do, and only what it answers is offered —
so the dialog shows the truth for the adapter in front of you.

**The adapter has to be running a supported firmware.** diCAN speaks slcan (ASCII over a virtual
COM port) and gs_usb (WinUSB). An adapter that enumerates but is not usable is still listed, with
what it is and what to do about it — it is never silently absent.

## If an adapter is not recognised

Check the USB connection and the adapter's firmware. On Windows, check whether Device Manager
shows the device with a warning. If it is still unavailable, email dsd_tech@outlook.com with the
adapter model, firmware version, operating system and the message shown by diCAN.

## Platforms

- **Windows** — Windows 10 version 1809 or later; x86, x64 and ARM64.
- **macOS** — Intel and Apple silicon; source builds require Apple's command-line developer tools.
- Linux is not supported.

The Microsoft Store and Mac App Store submissions are under review as of 27 September 2026.
Submission does not mean the app is already available in either store. Download availability
and package details will be listed on the repository's Releases page when published.

## Privacy and local data

No account or sign-in is required. diCAN makes no network connections and does not automatically
upload your settings, adapter information, CAN traffic or diagnostic logs.

Settings, recordings and logs stay in the local application data directory. Download builds
use `%APPDATA%\diCAN` on Windows and `~/Library/Application Support/diCAN` on macOS; store builds
use their separate package or sandbox locations. See `PRIVACY.md` for the complete directory list.

The privacy policy used by both stores is available at
[Privacy Policy](https://github.com/dsdtech-official/.github/blob/main/docs/dican/PRIVACY.md).

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). On macOS, install Apple's
command-line developer tools as well; the Mac project builds its native USB library with
`xcrun clang`. Build on the platform you intend to run on.

```
dotnet build diCAN.sln -c Release
dotnet run --project src/diCAN.App -- --data-dir ./artifacts/dev-data
```

The run command uses a Debug build with a separate development data directory. These commands
build and run the application; they do not reproduce store signing, sandbox packaging or installers.

## Source references and maintenance

Internal documents, tests and development tools are not included in this public source release.
Source comments provide brief descriptions of classes and functions.

This repository publishes DSD TECH releases. External contributions and pull requests are not
accepted. Report ordinary problems through Issues; report security issues privately as described
in `SECURITY.md`.

## Not in this version

Recording playback, and any form of scripting or automation API.

## Licence

Apache-2.0. See `LICENSE`, and `NOTICE` plus `THIRD-PARTY-NOTICES` for the attributions.

"DSD TECH" is a registered trademark of DongGuan DESHIDE TECHNOLOGY CO.,LTD. The licence covers
the source code; it does not grant any right to use the trademark.

---

DongGuan DESHIDE TECHNOLOGY CO.,LTD · [www.deshide.com](https://www.deshide.com) ·
东莞市德士德科技有限公司
dsd_tech@outlook.com
