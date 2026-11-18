# How Zoomies works

Zoomies is a small Windows tray app (C#, .NET 10, WPF). This page is for contributors: where things live, how each
feature works, and the rules the code keeps.

## The two projects

```text
src/Zoomies  (the app: WPF, Windows-only)            src/Zoomies.Core  (no UI, unit-tested)
├─ Program.cs     Velopack install/update hooks, then  ├─ Indexing/    FileIndex, IndexService, TextMatch
│                 starts the WPF App                    ├─ Frecency/    FrecencyStore (folders you use most)
├─ App.xaml.cs    single instance, tray, global         ├─ Organize/    FileCategory, OrganizePlanner, FileMover
│                 shortcut, pages, settings             ├─ Cleanup/     FileWalker, Scanners, Recycler
├─ Interop/       Win32: hotkey, tray icon, IPC,        ├─ History/     OperationJournal (undo)
│                 File Explorer and file dialogs        ├─ Platform/    RecycleBin, Shell
├─ Services/      updater, Windows integration,         ├─ Insights/    SuggestionEngine (Home page)
│                 Explorer tracking, memory trimming    ├─ Workspaces/  WorkspaceStore
├─ UI/            Quick Jump window, the hub and pages  ├─ Settings/    AppSettings, SettingsStore
├─ Themes/        Sand (light) and Midnight (dark)      └─ Storage/     JsonFile + source-generated JSON
└─ Strings/       en.json — every piece of UI text
```

Anything that can be tested without a window goes in `Zoomies.Core` and gets a test in `tests/Zoomies.Core.Tests`.

## Features, end to end

**Quick Jump.** `IndexService` builds an in-memory index of file and folder names under the user's folders on a
low-priority thread, then keeps it current with file-system watchers. `FileIndex` stores entries as parallel arrays
(about 50 bytes per item, capped by `MaxIndexedItems`) instead of one object per file. Results are ranked by
`TextMatch` and by frecency: `FrecencyStore` remembers which folders you open and how recently, learned from File
Explorer windows (`ExplorerTracker`) and seeded from Windows' Recent list (`FolderSeeder`). Pressing Enter navigates
the File Explorer window or the Open/Save dialog you came from (`ExplorerBridge`), or opens a new window.

**Organize.** `OrganizePlanner` turns a folder into a plan — which file moves where, by type or by month, with the
user's own folder names per file type — and the Organize page shows it before anything happens. `FileMover` carries
out the plan, never overwriting: a name clash gets a unique name. Every move is recorded in the journal.

**Clean up.** `FileWalker` walks the chosen folders, skipping folder links and online-only cloud files. `Scanners`
find unused files (by last-opened time), duplicates, large files, old installers and empty folders. `Recycler` sends
the chosen items to the Recycle Bin (`SHFileOperation` with undo allowed), never deleting outright, and records them.

**Undo.** `OperationJournal` keeps the last 300 actions in `history.json`. Undoing a move moves the files back;
undoing a clean-up restores the items from the Recycle Bin, found by reading the bin's `$I` records (`RecycleBin`).

**Workspaces** save the set of open File Explorer folders and reopen them together.

## Process model

- **One copy at a time.** A second launch (from the Start menu, the Explorer right-click menu or the installer) finds
  the running copy's hidden message window (`MessageHost`) and forwards its arguments with `WM_COPYDATA`, then exits.
  Forwarded commands only open pages (`--organize <folder>`, `--cleanup`, `--duplicates`, `--settings`,
  `--launcher`) or quit (`--quit`); nothing destructive can be triggered from outside.
- **Global shortcut.** `HotkeyRegistration` uses `RegisterHotKey`. If the saved shortcut is taken, the app switches to
  the first free one in `Hotkey.Alternatives`, saves it and tells the user once.
- **Memory.** Workstation GC without the background GC thread, `GCConserveMemory`, the compact index, and
  `MemoryTrimmer`, which gives memory back to Windows while the app is idle in the tray.

## Where things are stored

| Location | What | Removed on uninstall? |
|---|---|---|
| `%LOCALAPPDATA%\Zoomies` | The installed program (`current\`), `Update.exe`, downloaded update packages | Yes |
| `%APPDATA%\Zoomies` | `settings.json`, `folders.json` (learned folders), `history.json` (undo), `workspaces.json`, `logs\errors.log` | No |
| `HKCU\…\CurrentVersion\Run` | Start with Windows (value `Zoomies`) | Yes |
| `HKCU\Software\Classes\Directory\…\shell\Zoomies` | Explorer right-click menu, if turned on | Yes |

Earlier builds were called FolderPilot. On first start, Zoomies copies `%LOCALAPPDATA%\FolderPilot\*.json` into
`%APPDATA%\Zoomies` and removes FolderPilot's registry entries.

## Updates and releases

Velopack builds the installer and handles updates. An installed copy checks this repository's GitHub Releases three
minutes after starting and then once a day (Settings can turn this off), downloads a small delta package when there
is one, and applies it the next time Zoomies starts. Nothing about the user's files is sent. See
[RELEASING.md](RELEASING.md) for publishing a version.

## Text and translations

All UI text is in `src/Zoomies/Strings/en.json`, read through `Loc.T` (text), `Loc.F` (with placeholders) and
`Loc.P` (counts: a `.One` key holds the singular form). A translation is another JSON file with the same keys.

## The rules the code keeps

1. Nothing moves without a preview, and nothing is deleted outright — the Recycle Bin, always.
2. Every action can be undone from History.
3. Nothing about the user's files leaves the PC.
4. Memory stays bounded: no cache grows without a limit.
5. Folder links and online-only cloud files are never walked or downloaded.

## Developer tools

- **Demo mode and screenshots** (Debug builds only): `ZOOMIES_DEMO_ROOT=<folder>` points every personal folder at a
  sample tree with its own app data; `Zoomies.exe --snapshot <folder> [--dark]` renders every screen to PNG.
- **`tools/DemoRecorder`** builds a sample PC and records the demo videos and clips without showing the real desktop.
- **`tools/reel`** cuts those clips into captioned promo and tutorial videos.
