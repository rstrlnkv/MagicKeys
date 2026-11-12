# MagicKeys

Apple keyboards on Windows 11. Media keys, Mac shortcuts, modifiers, Fn navigation and Apple
layouts — the keys do what is printed on them.

![Platform](https://img.shields.io/badge/platform-Windows%2011-lightgrey)
[![Release](https://img.shields.io/github/v/release/rstrlnkv/MagicKeys?include_prereleases)](https://github.com/rstrlnkv/MagicKeys/releases)
[![Downloads](https://img.shields.io/github/downloads/rstrlnkv/MagicKeys/total)](https://github.com/rstrlnkv/MagicKeys/releases)
[![Licence](https://img.shields.io/github/license/rstrlnkv/MagicKeys)](LICENSE)

**English** · [Русский](README.ru.md)

## Install

Grab `MagicKeys-<version>-x64.msi` from the [releases](https://github.com/rstrlnkv/MagicKeys/releases)
and run it. The package installs for the whole machine, so Windows asks for administrator rights
once. MagicKeys starts from the Start menu and stays in the notification area when its window is
closed; quit it from the tray icon's menu. `--tray` starts it without the window, `--log` keeps a
log in `%AppData%\MagicKeys\log.txt`.

From then on MagicKeys updates itself: **About → Check now** finds the new release on GitHub and
runs its installer. An installer that is not signed with the project's certificate is never run —
the check compares the certificate's thumbprint, not a chain of trust. The **Stable** channel takes
finished releases only, **Dev** takes the prereleases too; the switch is on the About page.

## Features

| Feature | What it does |
|---|---|
| **Function keys** | F1–F12 do what is printed on them for your keyboard's generation, F13–F19 too on full-size models. Any key can take another action from a closed list — nothing typed in is ever run. Media first, as on a Mac, or F-keys first. |
| **Fn substitute** | Fn never reaches Windows, so a key of your choice stands in for it: right ⌥, a Windows key, right Control or Caps Lock. With it come Mac navigation (`Fn+←/→/↑/↓` → Home, End, PgUp, PgDn; `Fn+Backspace` → Delete) and the F-key mode switch. |
| **Mac shortcuts** | ⌘C copies, ⌘← goes to the start of the line, ⌥← skips a word, ⌘+Tab switches windows. |
| **Modifiers** | Both Controls, ⌥, both ⌘ and Caps Lock go anywhere or nowhere, one key at a time or by one of three ready-made schemes. |
| **Apple layouts** | 33 built-in layouts with working dead keys, one per Windows input language. Only the keys that differ from Microsoft's layout of the same language are replaced; nothing is installed into the system. |
| **Brightness** | Brightness keys work: external monitors over DDC/CI, the built-in panel through WMI, with an on-screen indicator. |
| **Battery** | The keyboard's charge, which Windows does not show. |
| **Your keyboard** | A drawing of the model MagicKeys recognised. Keys that reach Windows are outlined and light up when pressed. |
| **Apple driver** | Finds Apple's Boot Camp package on Apple's servers, downloads and unpacks it and installs the keyboard driver; removes it again on request. |

The program speaks Russian.

## Requirements

Windows 11 on x64. The app runs on the .NET Framework 4.x that ships with Windows; the package is
x64 only. Apple keyboards work over Bluetooth and USB. Remapping acts on all keyboard input,
because Windows does not say which device a key press came from; "Only with Magic Keyboard" keeps
MagicKeys quiet while no Apple keyboard is connected.

Administrator rights are asked for twice, each only when it is needed: to install the package, and
to install or remove Apple's driver (`pnputil`). The app itself runs as you and watches the
keyboard through a low-level hook.

Five hosts MagicKeys reaches over the network, each only after you press a button:
`api.github.com` and `github.com` for the update check and the installer download;
`swscan.apple.com` and `swcdn.apple.com` for Apple's catalog and the Boot Camp package (about
700 MB); and `www.7-zip.org` for the official 7-Zip installer when 7-Zip is missing — Apple's
package cannot be unpacked without it, and its SHA-256 is checked before it runs.

Four things MagicKeys can change beyond its own folder. **Settings** live in
`%AppData%\MagicKeys\` (`settings.xml`, your own layouts in `layouts\`, the log). **Apple's
package** and the unpacked 7-Zip go to `%LocalAppData%\MagicKeys\`, and the Apple driver page
offers a button that deletes them. **Autostart** is a value under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, only if you turn it on. **Apple's driver**
(`Keymagic2.inf`) goes into the Windows driver store with its 24 "(Apple)" keyboard layouts, only
if you install it; changing its Fn behaviour writes `OSXFnBehavior` under
`HKLM\SYSTEM\CurrentControlSet\Services\KeyMagic2`, behind an administrator prompt.

## Build from source

Requires Windows with .NET Framework 4.x — no SDK, Visual Studio or NuGet for the app itself.

```bat
scripts\build.cmd          :: src\ → MagicKeys.exe in the repository root
tests\run.cmd              :: the test suite
installer\build-msi.cmd    :: MagicKeys-<version>-x64.msi
```

`scripts\build.cmd` compiles `src\` with the framework's own `csc.exe` and numbers the build by the
commit count. `installer\build-msi.cmd` fetches WiX 3.14 through NuGet, so it needs the network;
without the release certificate the package comes out unsigned — it installs, but MagicKeys refuses
to update onto it. [ARCHITECTURE.md](ARCHITECTURE.md) says how the code is laid out.

## Licence

GPL-3.0 or later — see [LICENSE](LICENSE). Third-party data (Apple layouts from Unicode CLDR) and
its terms are in [NOTICE.md](NOTICE.md); Apple's driver files are never bundled.
