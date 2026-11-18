# Security policy

Zoomies moves and recycles files on people's PCs, so security reports are taken seriously.

## Supported versions

Only the [latest release](https://github.com/lakshmang007/zoomies/releases/latest) gets fixes. Installed copies update
themselves, so a fix reaches users within a day of release.

## Reporting a vulnerability

Please **don't open a public issue.** Use GitHub's private reporting instead:
**[Security → Report a vulnerability](https://github.com/lakshmang007/zoomies/security/advisories/new)**.

Include what an attacker could do, the steps to reproduce it, and the Zoomies and Windows versions. You'll get a reply
within 7 days. Once a fix is released, the advisory is published and you're credited, unless you'd rather not be.

## What counts

- Zoomies moving, recycling or changing files without the user's confirmation, or outside the folders they chose.
- Information about the user's files leaving the PC.
- Tampering with updates or the installer.
- Another program or user gaining rights through Zoomies.

Out of scope: problems that need an attacker who already controls the user's Windows account, and SmartScreen's
"unknown publisher" warning (the installer isn't code-signed yet).
