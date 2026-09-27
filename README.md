# diCAN

> **Free and open-source CAN analyser for Windows and macOS.** Connect a USB-CAN adapter to
> inspect CAN traffic, send frames, record a session and export the results.
>
> Developed by DSD TECH for its SH-C3x adapters, with support for standard CANable and
> compatible adapters running Elmue Multiboard 2.5 firmware.

| | |
|---|---|
| **Traffic views** | A table grouped by CAN ID and a stream of individual frames |
| **Protocols** | Classic CAN; CAN FD and bit-rate switching when supported by the adapter firmware |
| **Interface languages** | English, Simplified Chinese, Traditional Chinese, Japanese, French, German, Spanish, Italian and Portuguese |
| **Platforms** | Windows 10 version 1809 or later; macOS on Intel and Apple silicon. Linux is not supported |
| **Stack** | .NET 10 / Avalonia 12.1.0 |
| **Licence** | Apache-2.0 — see [LICENSE](LICENSE), [NOTICE](NOTICE) and [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES) |

## Downloads

| Platform | Download |
|---|---|
| **Windows — Microsoft Store** | **[Get diCAN from the Microsoft Store](https://apps.microsoft.com/detail/9N16CGHG2L72)** |
| **Windows — direct download** | [x64](https://github.com/dsdtech-official/diCAN/releases/download/v1.0.0/diCAN-1.0.0-win-x64.zip) · [x86](https://github.com/dsdtech-official/diCAN/releases/download/v1.0.0/diCAN-1.0.0-win-x86.zip) · [ARM64](https://github.com/dsdtech-official/diCAN/releases/download/v1.0.0/diCAN-1.0.0-win-arm64.zip) — Windows 10 version 1809 or later |
| **macOS — direct download** | [Apple silicon (M-series)](https://github.com/dsdtech-official/diCAN/releases/download/v1.0.0/diCAN-1.0.0-osx-arm64.zip) — macOS 12 or later; Developer ID signed and notarized. An Intel download is not included |
| **macOS — Mac App Store** | The submission is under review. The store link will be added when available |

The direct downloads are self-contained and include the .NET runtime. On Windows, extract
the ZIP and run `diCAN.App.exe`; these download builds are not code-signed. On macOS, extract
the ZIP and move `diCAN.app` to Applications.

See [diCAN v1.0.0](https://github.com/dsdtech-official/diCAN/releases/tag/v1.0.0) for release
details and [SHA256SUMS.txt](https://github.com/dsdtech-official/diCAN/releases/download/v1.0.0/SHA256SUMS.txt)
to verify your download.

## What it does

- **See the traffic in two ways.** Group frames by CAN ID to see counts, mean periods and
  changing payload bytes, or inspect each frame in a stream.
- **Send frames.** Build a transmit queue with an ID, payload, period and repeat count for
  each entry. Send once or periodically; Classic CAN, CAN FD, bit-rate switching and remote
  frames are offered according to the adapter's capabilities.
- **Filter the display.** Apply CAN ID and mask filters on the host. Changing the display
  filter does not discard frames already captured.
- **Record and export.** Save traffic to a local SQLite database, then export recordings as
  CSV or PEAK `.trc` files.
- **Keep diagnostic information.** Local logs record connection events and adapter reports
  to help investigate problems.
- **Use your preferred language.** Choose from nine interface languages in the Language menu.

Recording playback and scripting or automation APIs are not included in this version.

## Supported adapters

| Adapter family | Models or firmware |
|---|---|
| **DSD TECH SH-C30x** | SH-C30A, SH-C30G and SH-C30L |
| **DSD TECH SH-C31x** | SH-C31A and SH-C31G |
| **CANable** | Standard CANable 1.0 and 2.0 adapters |
| **Elmue Multiboard** | Compatible adapters running Elmue Multiboard 2.5 firmware |
| **DSD TECH SH-C32x** | SH-C32A and SH-C32B |

**Firmware determines the available features.** A model name or MCU alone does not guarantee
CAN FD support. diCAN reads the adapter's capabilities and offers the modes it reports. Some
SH-C31x units shipped with factory `canable2` firmware that does not report CAN FD; diCAN does
not offer CAN FD on those units.

The adapter must run a supported firmware. diCAN communicates using slcan over a virtual COM
port or gs_usb over USB. On Windows, gs_usb adapters use WinUSB.

An adapter that is detected but cannot be used is still listed with an explanation. If yours
is unavailable, check its USB connection, firmware and, on Windows, its Device Manager status.

## Getting started

1. Install diCAN and connect a supported USB-CAN adapter.
2. Open **Session → New session**, choose the adapter and configure the CAN mode and bit rates
   for your bus.
3. Inspect incoming frames in the grouped table or frame stream. Use the transmit queue when
   you want to send frames.
4. Start recording when needed. Open **Tools → Manage recordings** to export or delete a
   saved recording.

If you need help, email **dsd_tech@outlook.com** with the adapter model, firmware version,
operating system and the message shown by diCAN.

## Privacy and local data

No account or sign-in is required. diCAN makes no network connections and does not automatically
upload settings, adapter information, CAN traffic or diagnostic logs.

Settings, recordings and logs stay on your computer. Download builds use `%APPDATA%\diCAN`
on Windows and `~/Library/Application Support/diCAN` on macOS. Store builds use separate
package or sandbox locations; see [PRIVACY.md](PRIVACY.md) for the directory list and deletion
instructions.

The privacy policy used by the stores is also available at this
[public address](https://github.com/dsdtech-official/.github/blob/main/docs/dican/PRIVACY.md).
Review logs and recordings for sensitive information before sending them for support.

## Building from source

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download). On macOS, install Apple's
command-line developer tools as well; the Mac project builds its native USB library with
`xcrun clang`. Build on the platform you intend to run on.

```sh
dotnet build diCAN.sln -c Release
dotnet run --project src/diCAN.App -- --data-dir ./artifacts/dev-data
```

The run command uses a Debug build and a separate development data directory. These commands
build and run the application; they do not reproduce store signing, sandbox packaging or
installers. Windows source builds support x86, x64 and ARM64.

## About this repository

This repository contains diCAN's product source. Internal documents, tests and development
tools are not included. The build instructions above cover the application source; store
packaging tools are not included.

DSD TECH publishes and maintains the software. External contributions and pull requests are
not accepted. Report ordinary problems through
[Issues](https://github.com/dsdtech-official/diCAN/issues); report security issues privately
as described in [SECURITY.md](SECURITY.md).

## Licence

diCAN is released under the Apache License 2.0. See [LICENSE](LICENSE), [NOTICE](NOTICE) and
[THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES) for the licence and attributions.

"DSD TECH" is a registered trademark of DongGuan DESHIDE TECHNOLOGY CO.,LTD. The licence covers
the source code; it does not grant any right to use the trademark.

---

DongGuan DESHIDE TECHNOLOGY CO.,LTD · [www.deshide.com](https://www.deshide.com) ·
东莞市德士德科技有限公司 · **dsd_tech@outlook.com**
