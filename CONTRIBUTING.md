# Contributing to OpenPen

Thanks for wanting to improve OpenPen. It is a small, deliberately local Windows app,
and the best contributions keep it that way.

## Ground rules

- **No telemetry, analytics, ads, accounts, or network calls.** PRs that add any of these
  will be rejected.
- Keep the app native: C#, WPF, and a small Win32 surface. No Electron, no embedded
  browser, no web server.
- Do not add NuGet dependencies unless they are genuinely necessary. The app currently
  has none beyond the Windows desktop framework.
- Match the surrounding code style. The repo uses file-scoped namespaces, nullable
  reference types, and 4-space indentation.

## Build and run

Requirements: Windows 10/11 and the **.NET 8 SDK**.

```powershell
dotnet build OpenPen.sln -c Release
dotnet run --project OpenPen.csproj
```

Portable build:

```powershell
.\build.ps1
```

## What to test before opening a PR

At minimum, verify the affected path on a clean run:

1. Drawing with a mouse (and a pen tablet if you have one).
2. The collapsed and expanded toolbar.
3. One sensitive area from the change, e.g. undo/redo, eraser, screenshots, whiteboard,
   text, or the tray menu.
4. A clean build with zero warnings.

## Good places to help

- Customisable global hotkeys.
- Touch and multi-touch gestures.
- Persisting ink across sessions (optional, opt-in).
- Better multi-monitor / mixed-DPI coverage.
- Small accessibility and keyboard-navigation improvements.
- Documentation, screenshots, and bug reports.

## Pull requests

Keep changes focused, describe the behaviour change, and include a screenshot or short
recording for visible UI changes. For bugs, include your Windows version, display scaling,
and exact reproduction steps.

## Security

Please do not open public issues for security problems. See [SECURITY.md](SECURITY.md).
