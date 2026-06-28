# Ark Survival Ascended Dedicated Server

WindowsGSH module for ARK: Survival Ascended dedicated servers.

## Support

If this module helps you host your servers, you can support development here:

- [Ko-fi](https://ko-fi.com/shenniko)
- [PayPal](https://paypal.me/shenniko)

## Module Layout

```text
WindowsGSH.ArkSurvivalAscended/
  README.md
  LICENSE.md
  ArkSurvivalAscended.mod/
    module.json
    ArkSurvivalAscendedModule.cs
    author.png
```

Import `ArkSurvivalAscended.mod` directly, or import the repository root and let WindowsGSH discover the nested module folder.

## Current Status

- Installs through SteamCMD app `2430930`.
- Declares WindowsGSH module API `1.0` and minimum WindowsGSH `0.1.0`.
- Starts `ShooterGame/Binaries/Win64/ArkAscendedServer.exe`.
- Writes `ShooterGame/Saved/Config/WindowsServer/GameUserSettings.ini`.
- Writes `ShooterGame/Saved/Config/WindowsServer/Game.ini`.
- Launches with the selected map, `?listen`, session name, documented dash-style bind/port switches, max players, optional CurseForge mods, and extra arguments.
- Reports online/offline status from the ARK server process because ASA does not reliably answer A2S queries.
- Supports ARK/Source RCON when RCON is enabled and a password is configured.
- Uses log-tail console mode and tails `ShooterGame/Saved/Logs/ShooterGame.log` into the WindowsGSH console window.

## Quick Start

1. Import the module in WindowsGSH Module Management.
2. Create a new ARK: Survival Ascended server.
3. Set the server name, map, game port, passwords, max players, and optional mod IDs.
4. Install the server through WindowsGSH.
5. Start the server.

## Important Settings

- `server.map`: ASA map package, for example `TheIsland_WP`, `TheCenter_WP`, `ScorchedEarth_WP`, or `Aberration_WP`.
- `network.port`: UDP game port, default `7777`.
- `network.queryPort`: Steam/server-browser query port, default `27015`. WindowsGSH does not use this for ASA online/offline status.
- `rcon.enabled`: writes `RCONEnabled=True` to `GameUserSettings.ini` and includes `?RCONEnabled=True?RCONPort=<port>` in the launch args preview and launch command.
- `rcon.port`: TCP RCON port, default `27020`.
- `rcon.password`: optional RCON password. If blank, WindowsGSH uses `server.adminPassword` for RCON.
- `server.mods`: comma-separated CurseForge mod IDs. WindowsGSH passes these as `-mods="id,id"`.
- `server.password`: join password, written to `GameUserSettings.ini`.
- `server.adminPassword`: admin password, written to `GameUserSettings.ini`.
- `server.itemStackSizeMultiplier`, `server.harvestHealthMultiplier`, and player/dino multipliers: common rate controls written to `GameUserSettings.ini`.
- Breeding settings such as `server.matingIntervalMultiplier`, `server.eggHatchMultiplier`, `server.babyMatureMultiplier`, and imprint/cuddle controls are written to `Game.ini`.
- Transfer toggles such as `server.preventDownloadSurvivors`, `server.preventUploadItems`, and related item/dino controls are written to `GameUserSettings.ini`.
- World/rule settings such as crop growth, loot quality, max tamed dinos, corpse locator, unlimited respecs, and structure collision are exposed in the module UI and written to the ARK INI files.
- `server.autoPveTimer`, `server.autoPveUseSystemTime`, `server.autoPveStartTimeSeconds`, and `server.autoPveStopTimeSeconds`: ARK's scheduled PvE/PvP timer controls. Times are seconds after midnight, for example `57600` is 16:00 and `79200` is 22:00.
- PvE/PvP controls such as offline PvP protection, PvP respawn scaling, tribe wars, friendly fire, PvP decay, and foreign/tribute downloads are exposed in the module UI.
- Split XP controls such as kill, harvest, craft, boss, alpha, wild, cave, tamed, and explorer note XP multipliers are exposed in the module UI.
- General, structures, breeding, cryopod, and ASA content controls such as idle kick time, structure pickup, floating damage text, cryopod nerf settings, King Kaiju spawn time, Armadoggo cooldown, and Cosmo ammo limits are exposed in the module UI.
- `battleye.enabled`: keeps BattlEye enabled by default. Disable it to add `-NoBattlEye` at launch.
- `server.crossplay`: enables crossplay. When enabled, WindowsGSH adds `-ServerPlatform=ALL` at launch.
- `cluster.enabled`: enables ARK cluster launch arguments for character/item/dino transfer between servers.
- `cluster.id`: shared cluster identifier. Use the same value on every server in the cluster.
- `cluster.directory`: shared cluster data folder. Every server in the cluster must be able to read and write this folder.
- `cluster.altSaveDirectoryName`: unique save directory name for this server/map. Use a different value on each clustered server.
- `server.additionalArguments`: extra command-line arguments.

### Launch Port Arguments

ASA can ignore URL-style game port settings such as `?Port=8000` and then bind its default port instead. WindowsGSH passes the game port with the documented command-line switch and keeps the query/RCON ports in the existing URL-style server options:

```text
?QueryPort=<query port>?RCONPort=<rcon port> -MULTIHOME=<bind ip> -port=<game port>
```

The ARK log and WindowsGSH app log should show these switches in the actual launch command line.

## Configuration Files

The module writes:

```text
ShooterGame/Saved/Config/WindowsServer/GameUserSettings.ini
ShooterGame/Saved/Config/WindowsServer/Game.ini
```

`GameUserSettings.ini` receives the common server settings, passwords, RCON settings, rates, transfer toggles, and rule toggles.

`Game.ini` receives gameplay-mode settings such as:

- `OverrideOfficialDifficulty`
- `MatingIntervalMultiplier`
- `EggHatchSpeedMultiplier`
- `BabyMatureSpeedMultiplier`
- `BabyImprintingStatScaleMultiplier`
- `SupplyCrateLootQualityMultiplier`
- `MaxNumberOfPlayersInTribe`
- `bAutoPvETimer`
- `AutoPvEStartTimeSeconds`
- `AutoPvEStopTimeSeconds`
- `bIncreasePvPRespawnInterval`
- `bDisableFriendlyFire`
- split XP multipliers such as `KillXPMultiplier`, `BossKillXPMultiplier`, and `WildKillXPMultiplier`
- cryopod settings such as `EnableCryopodNerf`, `DisableCryopodEnemyCheck`, and `CryopodFridgeCooldowntime`
- ASA content settings such as `WorldBossKingKaijuSpawnTime`, `ArmadoggoDeathCooldown`, and `MaxCosmoWeaponAmmo`

## Notes

- Passwords are written to INI files rather than the launch command.
- ASA does not reliably answer A2S status queries. WindowsGSH uses process detection for online/offline status.
- ARK clusters do not use a master/slave server model. Each map server runs independently, but clustered servers must share the same Cluster ID and cluster directory.
- There is no strict cluster start order. For player transfers, the source and destination map servers should both be running.
- When hosting multiple clustered maps on one machine, give each server unique game, query, and RCON ports, and set a unique `cluster.altSaveDirectoryName` for each map.
- Existing WindowsGSM `serverqueryport` imports are mapped to `network.queryPort`.
- The manifest uses the current `entryPoints.processNames` format for process detection.
- The console view tails `ShooterGame/Saved/Logs/ShooterGame.log` after the server starts.

## Trust Note

WindowsGSH does not create, own, review, sign, or guarantee third-party modules. If you download and run one, responsibility for that module is yours.
