# LiteSynapse installer

`LiteSynapse-Setup-<version>.exe` installs LiteSynapse for the current Windows user. It needs no
administrator rights, writes only to the user's own folders and `HKEY_CURRENT_USER`, and never installs
or removes drivers or services.

## Build

```
bash tools/build-release.sh
```

The script:

1. reads the version from `AssemblyInformationalVersion` in `src/LiteSynapse/Properties/AssemblyInfo.cs`,
2. runs the unit tests (`dotnet test tests/LiteSynapse.Tests`) and stops if any fail,
3. publishes the app to `build/publish` (win-x64, framework-dependent, single `LiteSynapse.exe`, plus
   `tools/remove-razer-filter-drivers.ps1`),
4. refreshes `installer/seed-data` from `LiteSynapse/data/*.json` and checks that each file is valid JSON,
5. writes `build/installer-files.nsh`, the exact list of published files (the uninstaller deletes only these),
6. compiles `installer/LiteSynapse.nsi` into `dist/LiteSynapse-Setup-<version>.exe` and prints its path and size.

It exits with a non-zero code if any step fails. Requirements: the .NET 10 SDK and NSIS 3 (`makensis`).
It works on Linux and in Git Bash on Windows. `dotnet` runs under `flock` when `flock` is available. Set
`LITESYNAPSE_BUILD_LOCK` to the lock file that other builds of the same tree use (the default is
`$TMPDIR/litesynapse-build.lock`).

To compile only the installer after a publish: `makensis installer/LiteSynapse.nsi`. Optional settings:
`-DVERSION=`, `-DVERSION4=`, `-DPUBLISH_DIR=` and `-DOUTFILE=`.

## What the installer does

Pages: Welcome, Install folder, Options, Install, Finish (with "Launch LiteSynapse").

- **Install folder**: `%LOCALAPPDATA%\Programs\LiteSynapse` by default. An upgrade reuses the folder of the
  previous install. If you pick a folder that already holds other files, Setup offers to use a
  `LiteSynapse` subfolder instead. Setup also checks that it can write to the folder without administrator
  rights.
- **Closes LiteSynapse** if it runs, from any folder (the new location or the old `Documents\LiteSynapse`):
  1. Setup sends the app the sign-out notice (`WM_ENDSESSION`). The app then moves the mouse back to its
     own on-board slot and removes its keyboard hook.
  2. Setup runs `taskkill /IM LiteSynapse.exe`.
  3. After about 2 s, Setup runs `taskkill /F`.

  No console window appears. If LiteSynapse still runs, for example because it was started as
  administrator, Setup asks you to exit it from the tray icon and then retry.
- **Files**: `LiteSynapse.exe` and `tools\remove-razer-filter-drivers.ps1`, plus `uninstall.exe`. When
  installing over the old Documents folder, the old build's `LiteSynapse.dll`, `.deps.json`,
  `.runtimeconfig.json` and `.pdb` are removed.
- **Profiles and settings** go in `data` next to the exe. The first match wins. A file that already exists
  in `<install folder>\data` is never overwritten:
  1. `<install folder>\data\profiles.json` exists (upgrade): the folder is kept as it is.
  2. The old copy's `Documents\LiteSynapse\data` exists: everything is copied except `log.txt` and
     `log.old.txt`, including the `backup-*` folders. Setup also looks in `%USERPROFILE%\Documents` when
     Documents is redirected, and in the older `%APPDATA%\LiteSynapse`. The old folder is not changed.
  3. Otherwise, Setup copies in the bundled profiles from `installer/seed-data`. This is a copy of
     `LiteSynapse/data/*.json`: mouse and keyboard profiles, macros, linked games, calibration and settings.
     The `.txt` files and `backup-*` folders are not included.

  Each step is appended to `<install folder>\data\install.log`.
- **Shortcuts**: `LiteSynapse` in the Start menu and on the Desktop. The Desktop shortcut replaces the old
  copy's shortcut.
- **Start LiteSynapse with Windows** (Options page): this box starts checked when a `LiteSynapse` value
  already exists in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
  - Checked: Setup writes `"<install folder>\LiteSynapse.exe" --tray`.
  - Unchecked: Setup deletes the value, but only when it starts a `LiteSynapse.exe` (this copy or an
    older one).
- **Requirements**: the Options page shows whether each runtime is installed.
  - The .NET 10 Desktop Runtime is required. Setup looks for
    `%ProgramFiles%\dotnet\shared\Microsoft.WindowsDesktop.App\10.*`.
  - The WebView2 Runtime is needed for the new window; without it, LiteSynapse uses the classic one. Setup
    looks for a `pv` value other than `0.0.0.0` under
    `HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}` or under
    the same key in HKCU.

  If either runtime is missing, a message box offers to open the official download page. Installation
  continues either way. If .NET is missing, "Launch LiteSynapse" starts unchecked.
- **Apps & features** entry: `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\LiteSynapse`.
- **Finish page**: points to LiteSynapse > Settings > Performance for removing Razer leftovers (the app runs
  the elevated clean-up from there). It also says that the old `Documents\LiteSynapse` folder is no longer
  used and can be deleted by hand. Setup never deletes it.

Silent install: `LiteSynapse-Setup-<version>.exe /S` (optionally `/D=<folder>`, which must be the last
argument). "Start with Windows" keeps its current state, and the runtime checks are only logged.

## Uninstall

Use Settings > Apps > LiteSynapse, or run `uninstall.exe` in the install folder (`/S` runs it silently and
keeps your data). The uninstaller:

- closes LiteSynapse the same way Setup does,
- asks **"Also delete your profiles and settings?"**. The default answer is No, which keeps
  `<install folder>\data` for a later reinstall.
- deletes the installed files (the exact published list), `uninstall.exe`, both shortcuts, the uninstall
  entry and the `Run` value. The `Run` value is removed only when it points to this copy.
- deletes the WebView2 cache `%LOCALAPPDATA%\LiteSynapse\WebView2`,
- removes the install folder and `%LOCALAPPDATA%\LiteSynapse`, but only if they are empty.

It deletes nothing else. Files it does not touch:

- the old `Documents\LiteSynapse` folder,
- Razer software,
- `%TEMP%\.net\LiteSynapse`, where .NET unpacks the bundled WebView2 loader. Windows cleans `%TEMP%` on
  its own.
