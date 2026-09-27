# Privacy Policy

**diCAN** — DongGuan DESHIDE TECHNOLOGY CO.,LTD
Last updated: 2026-09-26

This policy covers diCAN for Windows, including the Microsoft Store version, and macOS,
including the Mac App Store version, published by DongGuan DESHIDE TECHNOLOGY CO.,LTD.

## The short version

diCAN does not automatically send your personal information, device information, CAN traffic,
settings, or diagnostic logs to us or to third parties. No account is required.

It makes no network connections at all. Everything it records stays on the computer it runs on,
in files you can open, copy, or delete yourself.

## What the app stores, and where

diCAN stores settings, recordings and diagnostic logs locally in its application data directory.
Default locations are:

| Platform | Directory |
|---|---|
| macOS (Mac App Store) | `~/Library/Containers/com.deshide.dican/Data/Library/Application Support/diCAN` |
| macOS (downloaded from our website) | `~/Library/Application Support/diCAN` |
| Windows (download version) | `%APPDATA%\diCAN` |
| Windows (Microsoft Store) | `%LOCALAPPDATA%\Packages\<diCAN package family>\LocalCache\Roaming\diCAN-Store` |

The Windows Store package family begins with `DSDTECH.diCAN_`; its full folder name includes
a publisher identifier. Within the packaged app, the logical path is `%APPDATA%\diCAN-Store`;
Windows redirects newly created data to the package location above.

What lives there:

| File | What it holds |
|---|---|
| `dican.db` | Your settings, including interface language, window layout and bit rates; saved adapter identifiers, names and notes; connection history and saved transmit-queue entries used to restore your workspace |
| Recording volumes | The CAN traffic you chose to record, and nothing else. Nothing is recorded until you press Record |
| `logs/dican-*.jsonl` | Local diagnostic information, such as application and operating system versions, device or port identifiers, file paths and errors, used to diagnose problems |

Files you export — CSV or PEAK `.trc` — go wherever you tell the export dialog to put them. The
app never moves them anywhere else.

## Network access: none

diCAN does not perform network requests, automatic uploads, telemetry, analytics, automatic
crash reporting, or online update checks. It does not display advertisements or use
advertising trackers.

On the Mac App Store version this is enforced by the operating system as well: the app is
sandboxed and is not granted the network entitlement, so it cannot open a network connection
even if it tried.

## CAN adapters

The app opens the USB-CAN adapters you select, for the sole purpose of exchanging CAN frames
with the bus you connect to. Adapter serial numbers, port names and firmware identifiers are
used on your machine to show you which adapter is which, and to offer only the features your
firmware actually supports. They are never transmitted.

The CAN traffic itself is shown to you and, if you ask for it, recorded locally. It is not
uploaded. When you explicitly send CAN frames, they are sent through the selected adapter
to your connected CAN bus. CAN traffic and user-entered notes may contain personal or
confidential information depending on the equipment and content you choose to work with.
diCAN does not analyse that content for advertising or profiling.

## Store versions keep separate data

The Mac App Store version uses Apple's App Sandbox container. The Microsoft Store version
uses its own `diCAN-Store` directory in the Windows package data location. These versions
do not automatically share or migrate settings or recordings with the download versions.
Before switching versions or uninstalling a Store version, export or back up recordings
you want to keep. Files you deliberately import or export remain under your control.

## Storage, protection and retention

Settings and recordings remain locally until you delete them or remove the directory that
contains them. Diagnostic logs rotate automatically, with at most 14 log files retained and
a 32 MiB size limit per file. Older logs may therefore be removed automatically.

diCAN relies on your operating system's user-account and file-access protections, including
the Mac App Store sandbox. It does not add application-level encryption to its database,
recordings, logs or exports. Other users or software with sufficient access to your computer
may be able to read these files. Use operating-system access controls and disk encryption
when your CAN data or notes are sensitive.

## Sharing, support and external services

diCAN has no automatic data-sharing function and does not sell your data or upload it to
cloud services. If you choose to email us for support, we receive your email address,
message and any files you attach. We use the information you provide to handle your support
request. Review logs and recordings and remove sensitive information before sending them;
you can request deletion of information you sent us using the contact address below.

This policy describes diCAN's handling of data. Microsoft Store, Mac App Store, GitHub,
websites you visit and the email service you use operate separately under their own privacy
policies. Visiting the published policy page or sending an email may disclose information
to those services through your browser or email application.

## Children

diCAN is a developer tool and is not directed at children. It does not automatically upload
information about users of any age.

## Deleting your data

You control which adapter to connect, which frames to send, and whether to start or stop
recording. You can view recorded traffic in diCAN and export recordings as CSV or PEAK `.trc`
files. Settings, adapter names and notes can be changed in the app.

Close diCAN before deleting its application data directory. Deleting that directory removes
its local settings, recordings and logs. Delete exports, backups and copies you made
separately; deleting application data does not delete those copies. Store uninstall or reset
may remove application data, so export or back up what you need first rather than relying
on uninstall to preserve or erase every copy. No online diCAN account or cloud copy needs
to be deleted.

## Changes to this policy

If this policy changes, the revised version will be published at this address with a new date
at the top.

## Contact

**dsd_tech@outlook.com** — DongGuan DESHIDE TECHNOLOGY CO.,LTD, www.deshide.com
