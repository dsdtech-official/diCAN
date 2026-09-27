# Security policy

## Reporting a vulnerability

**Email `dsd_tech@outlook.com`** with `diCAN security` in the subject.

Please do **not** open a public issue for a suspected vulnerability. A CAN analyser is often
plugged into equipment that people are working on; a public report can be acted on before there is
anything to update to.

Useful in the report, as far as you have it:

- what version of diCAN (the **About diCAN** dialog shows it), and which platform,
- which adapter and firmware (the model and firmware columns in the New Session dialog),
- what an attacker would be able to do, and what access they would need first,
- the smallest reproduction you have — a recording, a `.trc`, or a sequence of frames.

## What to expect

We will acknowledge the report and tell you whether we can reproduce it. If a fix ships, the
release notes will credit you unless you ask us not to.

There is no bug bounty.

## Scope

diCAN is a desktop application that talks to a USB adapter and writes files on the machine it runs
on. Reports about the following are in scope:

- code execution or file access outside what the user asked for, triggered by adapter data, a
  recording file, or an imported transmit list,
- anything that lets a crafted device or file reach the host beyond the CAN payload itself.

Out of scope, because they are known and documented rather than defects:

- **the published binaries are not code-signed**, so Windows SmartScreen warns on first run,
- **frames are sent as configured** — diCAN is an analyser and will put whatever you type onto the
  bus, including on a live vehicle or machine. That is the tool working, not a flaw.

## Firmware

Vulnerabilities in the **adapter firmware** are not handled here. Report those to
`dsd_tech@outlook.com` as well, but say clearly that the report is about firmware, and name the
model and firmware version.
