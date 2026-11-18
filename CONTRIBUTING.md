# Contributing to Zoomies

Thanks for helping! Zoomies moves and recycles people's files, so the bar is: **safe, private, light.**

## Reporting a bug

Open an [issue](https://github.com/lakshmang007/zoomies/issues/new/choose) and pick **Bug report**: the form asks for
what happened, the steps, your Zoomies and Windows versions, and optionally the end of
`%APPDATA%\Zoomies\logs\errors.log` (remove any file names you'd rather not share).

Security problems go through [private reporting](SECURITY.md), not public issues.

## Making a change

Start with [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): it shows where each feature lives and the rules below.

1. Fork the repo and create a branch.
2. `dotnet build Zoomies.slnx` and `dotnet test tests/Zoomies.Core.Tests` should both pass with no warnings.
3. Keep the rules the app already follows:
   - Nothing moves without a preview, nothing is deleted outright (Recycle Bin only), and every action can be undone.
   - Nothing about the user's files leaves the PC.
   - Memory matters: Zoomies sits in the tray all day. Avoid caches that grow without a limit.
   - All text the user sees goes in `src/Zoomies/Strings/en.json`, not in code.
4. Add user-visible changes to [CHANGELOG.md](CHANGELOG.md) under the next version.
5. Open a pull request; the template has a short checklist.

## Translating

Copy `src/Zoomies/Strings/en.json` to your language's code (for example `es.json` or `hi.json`), translate the values
(not the keys), keep placeholders like `{0}` as they are, and open a pull request.
