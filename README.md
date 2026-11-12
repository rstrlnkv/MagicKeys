# MagicKeys

Apple keyboards on Windows 11: media keys, Mac shortcuts, modifiers, Fn navigation and Apple layouts.

![platform](https://img.shields.io/badge/platform-Windows%2011-lightgrey) ![release](https://img.shields.io/github/v/release/rstrlnkv/MagicKeys?include_prereleases) ![downloads](https://img.shields.io/github/downloads/rstrlnkv/MagicKeys/total) ![licence](https://img.shields.io/github/license/rstrlnkv/MagicKeys)

**English** · [Русский](README.ru.md)

MagicKeys makes an Apple Magic Keyboard (or another Apple keyboard) behave on Windows the way its
keys are printed. It needs no driver of its own and installs nothing into the system: Apple layouts
are applied on the fly, not registered with Windows. On request it can fetch Apple's own Boot Camp
keyboard driver from Apple and install it. The program's interface is in Russian.

## Install

1. Download `MagicKeys-<version>-x64.msi` from [Releases](https://github.com/rstrlnkv/MagicKeys/releases).
2. Run it. The installer is per-machine, so Windows asks for administrator rights.
3. Start MagicKeys from the Start menu. It keeps running in the notification area when the window
   is closed; quit it from the tray icon's menu.

Command-line keys: `--tray` starts in the tray without the window; `--log` writes a log to
`%AppData%\MagicKeys\log.txt`.

Later updates install from the app itself: About → Check now. Uninstall in Settings → Apps.

## Features

| Feature | What it does |
|---|---|
| Function keys | F1–F12 do what is printed on them for your keyboard's generation (F13–F19 on full-size models); any key can be given another action from a fixed list; media-first or F-keys-first mode |
| Fn substitute | Fn never reaches Windows, so a key of your choice (right ⌥, a Windows key, right Control or Caps Lock) stands in for it, with Mac navigation: `Fn+←/→/↑/↓` → Home, End, PgUp, PgDn; `Fn+Backspace` → Delete |
| Mac shortcuts | ⌘C copies, ⌘← goes to the start of the line, ⌥← skips a word; ⌘+Tab as Alt+Tab |
| Modifiers | Both Controls, ⌥, both ⌘ and Caps Lock can be sent anywhere or switched off; three ready-made schemes |
| Apple layouts | 33 built-in layouts with dead keys; only the keys that differ from the Microsoft layout of the same language are replaced; one Apple layout per Windows input language |
| Screen brightness | Brightness keys work: external monitors over DDC/CI, the built-in panel through WMI, with an on-screen indicator |
| Keyboard battery | Shows the charge that Windows does not show |
| Your keyboard | A drawing of the keyboard model the app recognised; keys that reach Windows are marked and light up when pressed |
| Apple driver | Finds Apple's Boot Camp package on Apple's servers, downloads it, unpacks it and installs the keyboard driver; can remove it again |
| Updates | Checks GitHub on request; Stable or Dev channel; installs only an installer signed with the project's certificate |

## Requirements

- **OS:** Windows 11, x64. The app runs on the .NET Framework 4.x that ships with Windows.
- **Keyboards:** Apple keyboards over Bluetooth or USB. Remapping acts on all keyboard input, because
  Windows does not say which device a key press came from; the option "Only with Magic Keyboard"
  keeps the app quiet while no Apple keyboard is connected.
- **Permissions:** administrator rights to install the MSI and to install or remove the Apple
  driver (`pnputil`). The app itself runs as the user and uses a low-level keyboard hook.
- **Network:** nothing without a button press. Then it reaches:
  - `api.github.com` and `github.com` (and GitHub's download host): the update check and the
    installer download;
  - `swscan.apple.com` and `swcdn.apple.com`: Apple's update catalog and the Boot Camp package
    (about 700 MB);
  - `www.7-zip.org`: the official 7-Zip MSI, needed to unpack Apple's package, when 7-Zip is not
    installed (its SHA-256 is checked before use).
- **Changes outside its own folder:**
  - `%AppData%\MagicKeys\`: `settings.xml`, your own layouts in `layouts\`, and `log.txt` with `--log`;
  - `%LocalAppData%\MagicKeys\`: the downloaded Boot Camp package (`bootcamp\`) and the unpacked
    7-Zip (`tools\`); the app offers a button to delete them;
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`: the autostart entry, only if you turn it on;
  - Apple's keyboard driver (`Keymagic2.inf`) in the Windows driver store, with its 24 "(Apple)" keyboard
    layouts, only if you install it; changing its Fn behaviour writes `OSXFnBehavior` under
    `HKLM\SYSTEM\CurrentControlSet\Services\KeyMagic2` (administrator prompt).

## Build from source

You need Windows with .NET Framework 4.x. No SDK, Visual Studio or NuGet is needed for the app.

```
scripts\build.cmd
```

It compiles `src\` with the framework's own `csc.exe` and writes `MagicKeys.exe` to the repository
root, next to `layouts\`. The build number is the commit count (`git rev-list --count HEAD`).

```
tests\run.cmd
```

runs the tests.

```
installer\build-msi.cmd
```

builds `MagicKeys-<version>-x64.msi`. It fetches WiX 3.14 through NuGet, so it needs the network.
Without the release certificate the package is unsigned: it installs normally, but the app refuses
to update onto it.

See [ARCHITECTURE.md](ARCHITECTURE.md) for how the code is laid out.

## Licence

GNU General Public License v3 or later — see [LICENSE](LICENSE). Third-party material in the tree
(Apple layout data from Unicode CLDR) is listed in [NOTICE.md](NOTICE.md). Apple's driver files are
never bundled: the app downloads them from Apple on your machine.
