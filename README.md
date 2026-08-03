# ARK: Survival Ascended Dedicated Server

[![WindowsGSH](.github/assets/windowsgsh-badge.svg)](https://windowsgsh.com)
[![Status](https://img.shields.io/badge/status-beta_candidate-22C55E)](#status)
[![Module version](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fraw.githubusercontent.com%2FWindowsGSH%2FWindowsGSH.ArkSurvivalAscended%2Fmain%2FArkSurvivalAscended.mod%2Fmodule.json&query=%24.version&prefix=v&label=module&color=0F766E)](ArkSurvivalAscended.mod/module.json)
[![Requires WindowsGSH](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fraw.githubusercontent.com%2FWindowsGSH%2FWindowsGSH.ArkSurvivalAscended%2Fmain%2FArkSurvivalAscended.mod%2Fmodule.json%3Fbadge%3Dminimum&query=%24.minimumWindowsGshVersion&prefix=v&label=requires%20WindowsGSH&color=2563EB)](ArkSurvivalAscended.mod/module.json)
[![Licence](https://img.shields.io/badge/licence-MIT-64748B)](LICENSE.md)

This WindowsGSH module installs, configures, starts, monitors, administers, and backs up an ARK: Survival Ascended dedicated server.

## Status

**BETA CANDIDATE.** Version `1.29` models ASA's game/peer, query, and optional RCON ports; preserves unmanaged INI content; uses RCON for a real save-and-exit attempt; and checks the executable, configuration directory, and enabled-RCON credentials through Readiness Check. The live checks below remain required before it is marked beta tested.

## Installation

WindowsGSH installs SteamCMD app `2430930` using anonymous login and validation. The executable is `ShooterGame/Binaries/Win64/ArkAscendedServer.exe`.

Import `ArkSurvivalAscended.mod`, create a server, select the map and unique ports, set strong passwords, review the large configuration surface, and install it. CurseForge mod IDs are passed through ASA's `-mods` argument; WindowsGSH does not download those mods itself.

### Import an existing server

WindowsGSH can import either a normal server installation folder or a WindowsGSM server folder containing `serverfiles`. The preview verifies the server executable and lets you copy the installation into WindowsGSH or adopt it in place. Review the defaulted launch/configuration values against the existing INI files before completing the import; the source installation is not modified during preview.

## Configuration

WindowsGSH manages settings in:

- `ShooterGame/Saved/Config/WindowsServer/GameUserSettings.ini`
- `ShooterGame/Saved/Config/WindowsServer/Game.ini`

Managed values include identity/passwords, player limit, rates, PvE/PvP rules, transfers, breeding, XP, structures, cryopods, clusters, BattlEye, crossplay, mods, and ASA-specific content options. Version `1.27` replaces only keys WindowsGSH owns: comments, unknown keys, and unrelated INI sections survive later saves. Managed keys intentionally take the values selected in WindowsGSH.

ASA requires the game port as `-port=<value>`; the module keeps the query/RCON settings in the server URL and places the admin password in the INI rather than exposing it on the command line. Each clustered map needs its own ports and `cluster.altSaveDirectoryName`, while cluster members share the cluster ID and directory.

## Networking

| Purpose | Default | Protocol | Exposure |
| --- | ---: | --- | --- |
| Game and peer | `7777–7778` | UDP | Public; a two-port range starting at the configured game port |
| Steam/server-browser query | `27015` | UDP | Public |
| Source RCON | `27020` | TCP | Private administration; never selected for automatic external forwarding |

ASA always consumes the peer UDP port immediately above the game port. The game range and query port are eligible for firewall guidance and automatic UPnP when the server policy enables it. RCON is deliberately private; use it locally or through a secure private network rather than exposing it directly to the internet.

The bind-IP setting controls `-MULTIHOME`; it is not a public-IP discovery service. When hosting multiple instances, avoid overlap between every game/peer range, query port, and RCON port.

## Query, console, and administration

WindowsGSH reports process-based status because ASA's A2S behavior has not been reliable enough to use as the card's availability source. The declared query port remains necessary for the server browser but does not promise player counts in WindowsGSH.

The console view tails `ShooterGame/Saved/Logs/ShooterGame.log`; the headless process does not expose interactive standard input. When RCON is enabled with an admin or RCON password, WindowsGSH uses Source RCON for commands such as `ListPlayers`, `Broadcast`, `SaveWorld`, and `DoExit`.

Normal Stop first sends `SaveWorld` and `DoExit`, waits up to 20 seconds, and then retains the existing forced-stop fallback. Windows session-ending uses only the graceful RCON path and never force-kills. Therefore, meaningful shutdown protection requires RCON to be enabled and reachable.

## Files and backups

- Executable: `ShooterGame/Binaries/Win64/ArkAscendedServer.exe`
- Configuration: `ShooterGame/Saved/Config/WindowsServer`
- Saves: `ShooterGame/Saved/SavedArks`
- Primary log: `ShooterGame/Saved/Logs/ShooterGame.log`
- Backup targets: the WindowsServer configuration directory and SavedArks worlds

Stop the server gracefully before a restore. Keep the pre-restore archive until the map, tribes, characters, cluster transfers, and mod state have all been checked.

## Known limitations

- Process supervision does not provide ASA player counts or prove external reachability.
- RCON-disabled servers cannot receive WindowsGSH's graceful headless shutdown command.
- The large settings surface still needs a live round-trip audit against a current server build.
- WindowsGSH preserves unmanaged INI content but does not import arbitrary existing INI values back into `ServerConfig.json`.
- CurseForge mod compatibility, download progress, and update failures are controlled by ASA rather than a WindowsGSH addon installer.

## Beta verification checklist

- [ ] Fresh-install/update Steam app `2430930`, confirm the executable, and verify the process attaches to the correct card.
- [ ] Round-trip representative values from every settings group and prove comments, unknown keys, and unrelated INI sections survive.
- [ ] Test RCON-enabled `SaveWorld`/`DoExit`, RCON-disabled forced Stop, crash detection, and app-restart reattachment.
- [ ] Confirm remote joining, game and peer UDP listeners, query UDP, private RCON TCP, firewall guidance, and UPnP mappings.
- [ ] Test mods, clusters/transfers, update/validation, complete configuration/world backup, and a disposable restore.

## Support

Report module problems through the [WindowsGSH.ArkSurvivalAscended issue tracker](https://github.com/WindowsGSH/WindowsGSH.ArkSurvivalAscended/issues). Include module/WindowsGSH versions, sanitized configuration and launch arguments, map/mod/cluster context, relevant logs, and the operation performed. Never post admin, join, RCON, platform, or cluster credentials.

## Support development

If you like the work I do and would like to support continued WindowsGSH and module development, you can contribute here:

- [Ko-fi](https://ko-fi.com/shenniko)
- [PayPal](https://paypal.me/shenniko)

## Trust and source

Modules execute with the same Windows permissions as WindowsGSH. Download releases from a source you trust and review [`ArkSurvivalAscendedModule.cs`](ArkSurvivalAscended.mod/ArkSurvivalAscendedModule.cs) and [`module.json`](ArkSurvivalAscended.mod/module.json) before running them. See [SECURITY.md](SECURITY.md) for safe-download, credential-handling, and private vulnerability-reporting guidance. Network and configuration behavior was checked against the ARK community's [dedicated-server setup](https://ark.wiki.gg/wiki/Dedicated_server_setup) and [server configuration reference](https://ark.wiki.gg/wiki/Server_configuration).
