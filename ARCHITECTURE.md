# Architecture

A bird's-eye view for people who want to read or change the code. Build commands are in the
[README](README.md#build-from-source).

## Shape

MagicKeys is one Windows tray app, `MagicKeys.exe`: C# 5 and WPF on .NET Framework 4, compiled from
`src/*.cs` by the framework's own `csc.exe` (`scripts\build.cmd`). There is no project file and no
compiled XAML: markup is kept as strings and parsed at runtime with `XamlReader.Parse`.

| Where | What |
|---|---|
| `src/Engine.cs` | The low-level keyboard hook and all remapping logic |
| `src/Devices.cs`, `src/KeyWatch.cs` | Raw input: recognises Apple keyboards, watches which keys arrive |
| `src/Models.cs`, `src/MacKeys.cs`, `src/Actions.cs` | Catalogue of Apple keyboards, Mac shortcuts, the closed list of actions |
| `src/AppleLayout.cs` | Reads the layout files, handles dead keys |
| `src/AppleDriver.cs`, `src/AppleDriverSetup.cs` | Detects Apple's Boot Camp driver; finds, downloads, unpacks and installs it |
| `src/Brightness.cs`, `src/Osd.cs`, `src/KeyboardBattery.cs` | Brightness (DDC/CI, WMI), its on-screen indicator, battery level |
| `src/Settings.cs` | `%AppData%\MagicKeys\settings.xml`, autostart |
| `src/Updater.cs`, `src/Changelog.cs` | Update check against GitHub releases; the embedded "What's new" |
| `src/MainWindow.cs`, `src/Theme.cs`, `src/Fluent.cs`, `src/KeyboardArt.cs`, `src/Iconography.cs` | The window, theme, keyboard drawing and icons, all drawn in code |
| `layouts/` | Apple layouts as XML data |
| `tests/` | Console test runners compiled with the same sources (`tests\run.cmd`) |
| `installer/` | WiX 3.14 MSI and signing |

## Invariants

- The hook handler stays well under Windows' `LowLevelHooksTimeout` (300 ms): slow work goes to
  background threads. Keystrokes the app sends itself are tagged in `dwExtraInfo` and ignored by the hook.
- Raw input checks the hook: a press seen by raw input that never reached the hook means Windows
  dropped the hook silently; the app reinstalls it and releases every held key.
- A layout replaces only the levels that differ from Microsoft's layout for the same language
  (the `diff` attribute); everything else passes through untouched.
- The action list is closed: nothing typed by the user is ever run.
- An update installs only an MSI signed by the one pinned certificate thumbprint.
- The build number is the commit count; the version lives in `src/BuildInfo.cs`.

## Boundaries

- Remaps act on all keyboards: the low-level hook cannot tell which device a key came from.
- Fn and Eject never reach Windows; a substitute key stands in for Fn.
- Windows running elevated are out of reach of a non-elevated hook (UIPI).
- Apple's driver files are never in the tree or the installer; the app downloads them from
  `*.apple.com` on the user's machine.
