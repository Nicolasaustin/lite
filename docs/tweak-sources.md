# Where the Windows settings come from

Every entry on **Performance › Windows** and **Performance › Services** is taken from one of the sources
below, and each row in the app names its own. Nothing was invented, and nothing is in the catalog because it
sounded plausible.

## Sources

| Shown as | Source |
| --- | --- |
| FR33THY's Ultimate Windows Optimization Guide | <https://github.com/FR33THYFR33THY/Ultimate> |
| Chris Titus Tech's WinUtil | <https://github.com/ChrisTitusTech/winutil>, `config/tweaks.json` |
| documented by Microsoft | Microsoft's own documentation for MMCSS, HAGS and Delivery Optimization |
| LiteSynapse | this app's own, for the Naga and the BlackWidow Lite |

Both guides were read as data, not run. Each setting was taken as a registry path, a value and its opposite,
and re-implemented here; the apply/undo pattern (an `On` value and an `Off` value per registry entry, with
`<RemoveEntry>` meaning "delete it so Windows decides again") follows WinUtil's model, which is the right
shape for a catalog that has to work in both directions.

## What was deliberately not copied

- **The `Run-Trusted` / TrustedInstaller technique.** FR33THY's scripts reach settings that only SYSTEM may
  write by stopping the `TrustedInstaller` service and replacing its `binPath` with their own command, so the
  service manager runs it as SYSTEM. It works, but it is the same trick malware uses to get SYSTEM, it leaves
  Windows servicing pointed at the wrong binary if it fails halfway, and antivirus flags it. LiteSynapse uses
  ordinary elevated writes instead. A few of FR33THY's Defender settings cannot be written that way at all,
  because Tamper Protection blocks them - those are simply not offered, rather than forced.
- **Disabling Defender's own services** (`WinDefend`, `WdFilter`, `WdBoot`, `WdNisSvc`, `Sense`). `WinDefend`
  is on the services list because the guides include it and the row explains what you lose, but the boot-start
  driver entries are not: setting `WdBoot`/`WdFilter` to disabled through the registry is exactly what
  Tamper Protection exists to stop, and a half-applied change there is a PC that will not boot cleanly.
- **The firewall region blocker and the DNS benchmarker** from TinyBoost. Interesting, but neither belongs in
  a mouse driver, and the first one silently changes who you can connect to.
- **Anything under `CurrentControlSet\Enum`.** Not a choice so much as a fact: that key is readable by SYSTEM
  only. The device power settings go through WMI instead (see `DeviceTuning.cs`).

## Where this app disagrees with a guide

The catalog does **not** treat "different from the Windows default" as a problem. Earlier builds did, and
listed several of these as "harmful settings another tool left behind" with a button to undo them. That was
wrong twice over: it took the choice away, and on several of them the guide's value is the better one.

Three entries are still presented with the Windows value as the recommended one, with the reason on the row:

- **Mouse and keyboard buffer size** (`MouseDataQueueSize` / `KeyboardDataQueueSize`). The guides use 16;
  Windows uses 100. The row offers 16 and says what the risk is - below 16 Windows starts dropping input, and
  the Event Log reports a ring-buffer overflow when it happens. Both values are one click apart.
- **Windows Font Cache** and **Windows Event Log** appear on the services list, as the guides have them, but
  their rows say plainly that turning them off makes text slower and removes the log that would tell you a
  buffer is too small. They are offered; they are not recommended.
- **`WinDefend`**: the row suggests a game-folder exclusion instead, which gets the same loading-time gain
  without leaving the PC unprotected.

## Verification

Each registry path and value in `Tweaks.cs` was cross-checked against at least one of the two guides, and the
ones marked `ms` against Microsoft's documentation. `tests/LiteSynapse.Tests/SystemScanTests.cs` asserts that

- every entry has both an on-state and an off-state, so nothing is one-directional,
- every entry that gives something up has a written cost, and every entry in the security group has a long one,
- nothing in the catalog touches the kernel input stack (`mouclass`, `mouhid`, `kbdclass`, `kbdhid`,
  `mshidkmdf`, `HidUsb`) or deletes anything,
- the services list and the never-offer list do not overlap, and asking for a protected service by name
  produces an empty script.
