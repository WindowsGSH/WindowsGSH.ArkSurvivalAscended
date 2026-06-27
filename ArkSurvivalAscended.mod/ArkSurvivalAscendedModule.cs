using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using WindowsGSH.Core.Modules;
using WindowsGSH.Core.Rcon;
using WindowsGSH.Core.Servers;

namespace WindowsGSH.Modules.ArkSurvivalAscended;

public sealed class ArkSurvivalAscendedModule : IGameServerModule, IManifestBackedModule
{
    private const string ConfigRelativePath = @"ShooterGame\Saved\Config\WindowsServer";
    private const string GameUserSettingsFileName = "GameUserSettings.ini";
    private const string GameIniFileName = "Game.ini";

    private ModuleManifest? _manifest;
    private string _moduleDirectory = AppContext.BaseDirectory;

    private ModuleManifest Manifest => _manifest ??= ModuleManifest.Load(Path.Combine(_moduleDirectory, "module.json"));

    public string Id => Manifest.Id;
    public string Name => Manifest.Name;
    public string Version => Manifest.Version;
    public ModuleCapabilities Capabilities => Manifest.ToCapabilities(supportsQuery: true, supportsRcon: true);
    public SteamInstallDefinition? SteamInstall => Manifest.ToSteamInstall();
    public ModuleRuntimeDefinition Runtime => Manifest.ToRuntime();

    public void Configure(ModuleManifest manifest, string moduleDirectory)
    {
        _manifest = manifest;
        _moduleDirectory = moduleDirectory;
    }

    public IReadOnlyList<ConfigFieldDefinition> GetConfigFields() => Manifest.ToConfigFields();
    public IReadOnlyList<ServerAddonDefinition> GetAddonDefinitions() => Manifest.ToAddons();
    public IReadOnlyList<ServerBackupTargetDefinition> GetBackupTargets() => Manifest.ToBackupTargets();
    public string GetServerName(IReadOnlyDictionary<string, object?> settings) => GetSetting(settings, "server.name", Name);

    public ServerAddonStatus GetAddonStatus(ServerInstance instance, string addonId)
    {
        return new ServerAddonStatus(addonId, IsInstalled: false, IsEnabled: false, StatusText: "Manual mod");
    }

    public Task InstallAddonAsync(ServerInstance instance, string addonId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("ARK CurseForge mod installation is handled by the server from the configured mod IDs.");
    }

    public Task RemoveAddonAsync(ServerInstance instance, string addonId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Remove ARK mod IDs from the module configuration to stop loading them.");
    }

    public Task<IReadOnlyList<Process>> StartAddonProcessesAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<Process>>([]);
    }

    public ServerDisplayInfo GetDisplayInfo(ServerInstance instance)
    {
        return new ServerDisplayInfo(
            GetSetting(instance, "network.ip", "0.0.0.0"),
            GetSetting(instance, "network.port", "7777"),
            GetSetting(instance, "server.maxPlayers", "40"));
    }

    public Task<IReadOnlyDictionary<string, object?>> ReadConfigFileSettingsAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
    }

    public Task WriteConfigFileSettingsAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        WriteIniFiles(instance);
        return Task.CompletedTask;
    }

    public Task<InstallPlan> CreateInstallPlanAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        if (SteamInstall == null)
        {
            throw new NotSupportedException("ARK: Survival Ascended module does not define a SteamCMD install.");
        }

        return Task.FromResult(new InstallPlan(
            "steamcmd",
            $"+force_install_dir \"{instance.InstallPath}\" +login anonymous +app_update {SteamInstall.AppId} validate +quit",
            instance.InstallPath,
            ["Installs ARK: Survival Ascended Dedicated Server through SteamCMD app 2430930."]));
    }

    public Task<ProcessStartInfo> CreateStartInfoAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        WriteIniFiles(instance);

        var arguments = ModuleLaunchArgumentBuilder.Build(Manifest.GetDefaultArguments(), BuildLaunchSettings(instance));

        var modArguments = BuildModArguments(instance);
        if (!string.IsNullOrWhiteSpace(modArguments))
        {
            arguments = string.IsNullOrWhiteSpace(arguments)
                ? modArguments
                : $"{arguments} {modArguments}";
        }

        var battleEyeArguments = BuildBattlEyeArguments(instance);
        if (!string.IsNullOrWhiteSpace(battleEyeArguments))
        {
            arguments = string.IsNullOrWhiteSpace(arguments)
                ? battleEyeArguments
                : $"{arguments} {battleEyeArguments}";
        }

        return Task.FromResult(new ProcessStartInfo
        {
            FileName = Path.Combine(instance.InstallPath, Runtime.StartPath),
            WorkingDirectory = instance.InstallPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = Runtime.AllowsEmbeddedConsole,
            RedirectStandardError = Runtime.AllowsEmbeddedConsole,
            RedirectStandardInput = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    public async Task<Process?> StartAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        if (!IsInstallValid(instance))
        {
            throw new FileNotFoundException("ARK: Survival Ascended server executable was not found.", Path.Combine(instance.InstallPath, Runtime.StartPath));
        }

        var process = new Process
        {
            StartInfo = await CreateStartInfoAsync(instance, cancellationToken),
            EnableRaisingEvents = true
        };
        process.Start();
        return process;
    }

    public Task StopAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        return ModuleStopStrategyRunner.StopAsync(this, Manifest, instance, cancellationToken);
    }

    public bool IsInstallValid(ServerInstance instance)
    {
        return File.Exists(Path.Combine(instance.InstallPath, Runtime.StartPath));
    }

    public string? GetConsoleLogPath(ServerInstance instance)
    {
        return Path.Combine(instance.InstallPath, "ShooterGame", "Saved", "Logs", "ShooterGame.log");
    }

    public async Task<string> ExecuteRconCommandAsync(ServerInstance instance, string command, CancellationToken cancellationToken)
    {
        if (!GetBool(instance, "rcon.enabled"))
        {
            throw new InvalidOperationException("Enable RCON in this server's ARK settings before sending RCON commands.");
        }

        var password = GetAdminOrRconPassword(instance);
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Set an Admin Password or RCON Password before sending RCON commands.");
        }

        var portText = GetSetting(instance, "rcon.port", "27020");
        if (!int.TryParse(portText, out var port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException("RCON Port must be between 1 and 65535.");
        }

        var host = GetConnectableHost(GetSetting(instance, "network.ip", "127.0.0.1"));
        var response = await new SourceRconClient().ExecuteAsync(host, port, password, NormalizeRconCommand(command), cancellationToken);
        return IsEmptyRconResponse(response)
            ? "RCON command sent. The ARK server returned no text. Try ARK commands such as ListPlayers, SaveWorld, Broadcast <message>, or DoExit."
            : response;
    }

    public Task<QueryResult> QueryAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = ServerProcessLocator.IsRunning(this, instance.InstallPath)
            ? ModuleServerStatus.Online
            : ModuleServerStatus.Offline;
        return Task.FromResult(new QueryResult(status, Message: "Process status only. ARK: Survival Ascended does not answer A2S queries reliably."));
    }

    private static void WriteIniFiles(ServerInstance instance)
    {
        var configDirectory = Path.Combine(instance.InstallPath, ConfigRelativePath);
        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(Path.Combine(configDirectory, GameUserSettingsFileName), BuildGameUserSettings(instance));
        File.WriteAllText(Path.Combine(configDirectory, GameIniFileName), BuildGameIni(instance));
    }

    private static string BuildGameUserSettings(ServerInstance instance)
    {
        var lines = new StringBuilder();
        lines.AppendLine("[ServerSettings]");
        lines.AppendLine($"ServerPassword={GetSetting(instance, "server.password", "")}");
        lines.AppendLine($"ServerAdminPassword={GetAdminOrRconPassword(instance)}");
        lines.AppendLine($"SessionName={GetSetting(instance, "server.name", "Ark Survival Ascended Dedicated Server")}");
        lines.AppendLine($"MaxPlayers={GetSetting(instance, "server.maxPlayers", "40")}");
        lines.AppendLine($"Port={GetSetting(instance, "network.port", "7777")}");
        lines.AppendLine($"RCONEnabled={ToIniBool(GetBool(instance, "rcon.enabled"))}");
        lines.AppendLine($"RCONPort={GetSetting(instance, "rcon.port", "27020")}");
        lines.AppendLine($"ServerPVE={ToIniBool(GetBool(instance, "server.pve"))}");
        lines.AppendLine($"AllowThirdPersonPlayer={ToIniBool(GetBool(instance, "server.allowThirdPerson", true))}");
        lines.AppendLine($"ShowMapPlayerLocation={ToIniBool(GetBool(instance, "server.showMapPlayerLocation"))}");
        lines.AppendLine($"DisableWeatherFog={ToIniBool(GetBool(instance, "server.disableWeatherFog"))}");
        lines.AppendLine($"XPMultiplier={GetDecimal(instance, "server.xpMultiplier", "1.0")}");
        lines.AppendLine($"HarvestAmountMultiplier={GetDecimal(instance, "server.harvestMultiplier", "1.0")}");
        lines.AppendLine($"HarvestHealthMultiplier={GetDecimal(instance, "server.harvestHealthMultiplier", "1.0")}");
        lines.AppendLine($"TamingSpeedMultiplier={GetDecimal(instance, "server.tamingMultiplier", "1.0")}");
        lines.AppendLine($"ItemStackSizeMultiplier={GetDecimal(instance, "server.itemStackSizeMultiplier", "1.0")}");
        lines.AppendLine($"DayCycleSpeedScale={GetDecimal(instance, "server.dayCycleSpeed", "1.0")}");
        lines.AppendLine($"NightTimeSpeedScale={GetDecimal(instance, "server.nightTimeSpeed", "1.0")}");
        lines.AppendLine($"DifficultyOffset={GetDecimal(instance, "server.difficultyOffset", "1.0")}");
        lines.AppendLine($"PlayerCharacterWaterDrainMultiplier={GetDecimal(instance, "server.playerWaterDrainMultiplier", "1.0")}");
        lines.AppendLine($"PlayerCharacterFoodDrainMultiplier={GetDecimal(instance, "server.playerFoodDrainMultiplier", "1.0")}");
        lines.AppendLine($"PlayerCharacterStaminaDrainMultiplier={GetDecimal(instance, "server.playerStaminaDrainMultiplier", "1.0")}");
        lines.AppendLine($"PlayerCharacterHealthRecoveryMultiplier={GetDecimal(instance, "server.playerHealthRecoveryMultiplier", "1.0")}");
        lines.AppendLine($"PlayerDamageMultiplier={GetDecimal(instance, "server.playerDamageMultiplier", "1.0")}");
        lines.AppendLine($"PlayerResistanceMultiplier={GetDecimal(instance, "server.playerResistanceMultiplier", "1.0")}");
        lines.AppendLine($"DinoCharacterFoodDrainMultiplier={GetDecimal(instance, "server.dinoFoodDrainMultiplier", "1.0")}");
        lines.AppendLine($"DinoDamageMultiplier={GetDecimal(instance, "server.dinoDamageMultiplier", "1.0")}");
        lines.AppendLine($"DinoResistanceMultiplier={GetDecimal(instance, "server.dinoResistanceMultiplier", "1.0")}");
        lines.AppendLine($"DinoCharacterStaminaDrainMultiplier={GetDecimal(instance, "server.dinoStaminaDrainMultiplier", "1.0")}");
        lines.AppendLine($"DinoCharacterHealthRecoveryMultiplier={GetDecimal(instance, "server.dinoHealthRecoveryMultiplier", "1.0")}");
        lines.AppendLine($"ResourcesRespawnPeriodMultiplier={GetDecimal(instance, "server.resourceRespawnMultiplier", "1.0")}");
        lines.AppendLine($"StructureResistanceMultiplier={GetDecimal(instance, "server.structureResistanceMultiplier", "1.0")}");
        lines.AppendLine($"AutoSavePeriodMinutes={GetDecimal(instance, "server.autoSavePeriodMinutes", "15")}");
        lines.AppendLine($"MaxTamedDinos={GetSetting(instance, "server.maxTamedDinos", "5000")}");
        lines.AppendLine($"RCONServerGameLogBuffer={GetSetting(instance, "server.rconServerGameLogBuffer", "600")}");
        lines.AppendLine($"KickIdlePlayersPeriod={GetDecimal(instance, "server.kickIdlePlayersPeriod", "3600")}");
        lines.AppendLine($"ServerCrosshair={ToIniBool(GetBool(instance, "server.crosshair"))}");
        lines.AppendLine($"AlwaysAllowStructurePickup={ToIniBool(GetBool(instance, "server.alwaysAllowStructurePickup"))}");
        lines.AppendLine($"StructurePickupTimeAfterPlacement={GetDecimal(instance, "server.structurePickupTimeAfterPlacement", "30")}");
        lines.AppendLine($"StructurePickupHoldDuration={GetDecimal(instance, "server.structurePickupHoldDuration", "0.5")}");
        lines.AppendLine($"TheMaxStructuresInRange={GetSetting(instance, "server.maxStructuresInRange", "10500")}");
        lines.AppendLine($"PerPlatformMaxStructuresMultiplier={GetDecimal(instance, "server.perPlatformMaxStructuresMultiplier", "1.0")}");
        lines.AppendLine($"AllowFlyerCarryPvE={ToIniBool(GetBool(instance, "server.allowFlyerCarryPve", true))}");
        lines.AppendLine($"AllowAnyoneBabyImprintCuddle={ToIniBool(GetBool(instance, "server.allowAnyoneBabyImprintCuddle"))}");
        lines.AppendLine($"DisableImprintDinoBuff={ToIniBool(GetBool(instance, "server.disableImprintDinoBuff"))}");
        lines.AppendLine($"AutoDestroyDecayedDinos={ToIniBool(GetBool(instance, "server.autoDestroyDecayedDinos"))}");
        lines.AppendLine($"AutoDestroyOldStructuresMultiplier={GetDecimal(instance, "server.autoDestroyOldStructuresMultiplier", "1.0")}");
        lines.AppendLine($"ShowFloatingDamageText={ToIniBool(GetBool(instance, "server.showFloatingDamageText"))}");
        lines.AppendLine($"AllowHitMarkers={ToIniBool(GetBool(instance, "server.allowHitMarkers"))}");
        lines.AppendLine($"AllowRaidDinoFeeding={ToIniBool(GetBool(instance, "server.allowRaidDinoFeeding"))}");
        lines.AppendLine($"EnableCryopodNerf={ToIniBool(GetBool(instance, "server.enableCryopodNerf"))}");
        lines.AppendLine($"CryopodNerfDuration={GetDecimal(instance, "server.cryopodNerfDuration", "10")}");
        lines.AppendLine($"CryopodNerfDamageMult={GetDecimal(instance, "server.cryopodNerfDamageMultiplier", "0.01")}");
        lines.AppendLine($"CryopodNerfIncomingDamageMultPercent={GetDecimal(instance, "server.cryopodNerfIncomingDamageMultiplierPercent", "0")}");
        lines.AppendLine($"AllowCryoFridgeOnSaddle={ToIniBool(GetBool(instance, "server.allowCryoFridgeOnSaddle"))}");
        lines.AppendLine($"DisableCryopodEnemyCheck={ToIniBool(GetBool(instance, "server.disableCryopodEnemyCheck"))}");
        lines.AppendLine($"DisableCryopodFridgeRequirement={ToIniBool(GetBool(instance, "server.disableCryopodFridgeRequirement"))}");
        lines.AppendLine($"EnableCryoSicknessPVE={ToIniBool(GetBool(instance, "server.enableCryoSicknessPve"))}");
        lines.AppendLine($"CryopodFridgeCooldowntime={GetDecimal(instance, "server.cryopodFridgeCooldownTime", "90")}");
        lines.AppendLine($"ForceGachaUnhappyInCaves={ToIniBool(GetBool(instance, "server.forceGachaUnhappyInCaves"))}");
        lines.AppendLine($"WorldBossKingKaijuSpawnTime={GetSetting(instance, "server.worldBossKingKaijuSpawnTime", "15:00:00")}");
        lines.AppendLine($"ArmadoggoDeathCooldown={GetDecimal(instance, "server.armadoggoDeathCooldown", "3600")}");
        lines.AppendLine($"MaxCosmoWeaponAmmo={GetSetting(instance, "server.maxCosmoWeaponAmmo", "-1")}");
        lines.AppendLine($"CosmoWeaponAmmoReloadAmount={GetSetting(instance, "server.cosmoWeaponAmmoReloadAmount", "-1")}");
        lines.AppendLine($"PreventOfflinePvP={ToIniBool(GetBool(instance, "server.preventOfflinePvp"))}");
        lines.AppendLine($"PreventOfflinePvPInterval={GetDecimal(instance, "server.preventOfflinePvpInterval", "0")}");
        lines.AppendLine($"PreventTribeAlliances={ToIniBool(GetBool(instance, "server.preventTribeAlliances"))}");
        lines.AppendLine($"PvPDinoDecay={ToIniBool(GetBool(instance, "server.pvpDinoDecay"))}");
        lines.AppendLine($"PvPStructureDecay={ToIniBool(GetBool(instance, "server.pvpStructureDecay"))}");
        lines.AppendLine($"PvEDinoDecayPeriodMultiplier={GetDecimal(instance, "server.pveDinoDecayPeriodMultiplier", "1.0")}");
        lines.AppendLine($"PvEAllowStructuresAtSupplyDrops={ToIniBool(GetBool(instance, "server.pveAllowStructuresAtSupplyDrops"))}");
        lines.AppendLine($"CrossARKAllowForeignDinoDownloads={ToIniBool(GetBool(instance, "server.crossArkAllowForeignDinoDownloads"))}");
        lines.AppendLine($"NoTributeDownloads={ToIniBool(GetBool(instance, "server.noTributeDownloads"))}");
        lines.AppendLine($"IgnorePVPMountedWeaponryRestriction={ToIniBool(GetBool(instance, "server.ignorePvpMountedWeaponryRestriction"))}");
        lines.AppendLine($"PreventDownloadSurvivors={ToIniBool(GetBool(instance, "server.preventDownloadSurvivors"))}");
        lines.AppendLine($"PreventDownloadItems={ToIniBool(GetBool(instance, "server.preventDownloadItems"))}");
        lines.AppendLine($"PreventDownloadDinos={ToIniBool(GetBool(instance, "server.preventDownloadDinos"))}");
        lines.AppendLine($"PreventUploadSurvivors={ToIniBool(GetBool(instance, "server.preventUploadSurvivors"))}");
        lines.AppendLine($"PreventUploadItems={ToIniBool(GetBool(instance, "server.preventUploadItems"))}");
        lines.AppendLine($"PreventUploadDinos={ToIniBool(GetBool(instance, "server.preventUploadDinos"))}");
        lines.AppendLine();
        lines.AppendLine("[SessionSettings]");
        lines.AppendLine($"SessionName={GetSetting(instance, "server.name", "Ark Survival Ascended Dedicated Server")}");
        lines.AppendLine();
        lines.AppendLine("[MessageOfTheDay]");
        lines.AppendLine("Message=");
        lines.AppendLine("Duration=20");
        return lines.ToString();
    }

    private static string BuildGameIni(ServerInstance instance)
    {
        var lines = new StringBuilder();
        lines.AppendLine("[/script/shootergame.shootergamemode]");
        lines.AppendLine($"OverrideOfficialDifficulty={GetDecimal(instance, "server.officialDifficulty", "5.0")}");
        lines.AppendLine($"MatingIntervalMultiplier={GetDecimal(instance, "server.matingIntervalMultiplier", "1.0")}");
        lines.AppendLine($"EggHatchSpeedMultiplier={GetDecimal(instance, "server.eggHatchMultiplier", "1.0")}");
        lines.AppendLine($"BabyMatureSpeedMultiplier={GetDecimal(instance, "server.babyMatureMultiplier", "1.0")}");
        lines.AppendLine($"BabyImprintingStatScaleMultiplier={GetDecimal(instance, "server.babyImprintingStatScaleMultiplier", "1.0")}");
        lines.AppendLine($"BabyCuddleIntervalMultiplier={GetDecimal(instance, "server.babyCuddleIntervalMultiplier", "1.0")}");
        lines.AppendLine($"BabyCuddleGracePeriodMultiplier={GetDecimal(instance, "server.babyCuddleGracePeriodMultiplier", "1.0")}");
        lines.AppendLine($"BabyCuddleLoseImprintQualitySpeedMultiplier={GetDecimal(instance, "server.babyCuddleLoseImprintQualitySpeedMultiplier", "1.0")}");
        lines.AppendLine($"BabyFoodConsumptionSpeedMultiplier={GetDecimal(instance, "server.babyFoodConsumptionSpeedMultiplier", "1.0")}");
        lines.AppendLine($"BabyImprintAmountMultiplier={GetDecimal(instance, "server.babyImprintAmountMultiplier", "1.0")}");
        lines.AppendLine($"GlobalSpoilingTimeMultiplier={GetDecimal(instance, "server.globalSpoilingTimeMultiplier", "1.0")}");
        lines.AppendLine($"GlobalItemDecompositionTimeMultiplier={GetDecimal(instance, "server.globalItemDecompositionTimeMultiplier", "1.0")}");
        lines.AppendLine($"GlobalCorpseDecompositionTimeMultiplier={GetDecimal(instance, "server.globalCorpseDecompositionTimeMultiplier", "1.0")}");
        lines.AppendLine($"CropGrowthSpeedMultiplier={GetDecimal(instance, "server.cropGrowthSpeedMultiplier", "1.0")}");
        lines.AppendLine($"CropDecaySpeedMultiplier={GetDecimal(instance, "server.cropDecaySpeedMultiplier", "1.0")}");
        lines.AppendLine($"DinoHarvestingDamageMultiplier={GetDecimal(instance, "server.dinoHarvestingDamageMultiplier", "1.0")}");
        lines.AppendLine($"PlayerHarvestingDamageMultiplier={GetDecimal(instance, "server.playerHarvestingDamageMultiplier", "1.0")}");
        lines.AppendLine($"CustomRecipeEffectivenessMultiplier={GetDecimal(instance, "server.customRecipeEffectivenessMultiplier", "1.0")}");
        lines.AppendLine($"CustomRecipeSkillMultiplier={GetDecimal(instance, "server.customRecipeSkillMultiplier", "1.0")}");
        lines.AppendLine($"KillXPMultiplier={GetDecimal(instance, "server.killXpMultiplier", "1.0")}");
        lines.AppendLine($"HarvestXPMultiplier={GetDecimal(instance, "server.harvestXpMultiplier", "1.0")}");
        lines.AppendLine($"CraftXPMultiplier={GetDecimal(instance, "server.craftXpMultiplier", "1.0")}");
        lines.AppendLine($"GenericXPMultiplier={GetDecimal(instance, "server.genericXpMultiplier", "1.0")}");
        lines.AppendLine($"SpecialXPMultiplier={GetDecimal(instance, "server.specialXpMultiplier", "1.0")}");
        lines.AppendLine($"ExplorerNoteXPMultiplier={GetDecimal(instance, "server.explorerNoteXpMultiplier", "1.0")}");
        lines.AppendLine($"BossKillXPMultiplier={GetDecimal(instance, "server.bossKillXpMultiplier", "1.0")}");
        lines.AppendLine($"AlphaKillXPMultiplier={GetDecimal(instance, "server.alphaKillXpMultiplier", "1.0")}");
        lines.AppendLine($"WildKillXPMultiplier={GetDecimal(instance, "server.wildKillXpMultiplier", "1.0")}");
        lines.AppendLine($"CaveKillXPMultiplier={GetDecimal(instance, "server.caveKillXpMultiplier", "1.0")}");
        lines.AppendLine($"TamedKillXPMultiplier={GetDecimal(instance, "server.tamedKillXpMultiplier", "1.0")}");
        lines.AppendLine($"UnclaimedKillXPMultiplier={GetDecimal(instance, "server.unclaimedKillXpMultiplier", "1.0")}");
        lines.AppendLine($"FuelConsumptionIntervalMultiplier={GetDecimal(instance, "server.fuelConsumptionIntervalMultiplier", "1.0")}");
        lines.AppendLine($"SupplyCrateLootQualityMultiplier={GetDecimal(instance, "server.supplyCrateLootQualityMultiplier", "1.0")}");
        lines.AppendLine($"FishingLootQualityMultiplier={GetDecimal(instance, "server.fishingLootQualityMultiplier", "1.0")}");
        lines.AppendLine($"CraftingSkillBonusMultiplier={GetDecimal(instance, "server.craftingSkillBonusMultiplier", "1.0")}");
        lines.AppendLine($"MaxNumberOfPlayersInTribe={GetSetting(instance, "server.maxPlayersInTribe", "0")}");
        lines.AppendLine($"AutoPvEStartTimeSeconds={GetSetting(instance, "server.autoPveStartTimeSeconds", "0")}");
        lines.AppendLine($"AutoPvEStopTimeSeconds={GetSetting(instance, "server.autoPveStopTimeSeconds", "0")}");
        lines.AppendLine($"PvPZoneStructureDamageMultiplier={GetDecimal(instance, "server.pvpZoneStructureDamageMultiplier", "6.0")}");
        lines.AppendLine($"IncreasePvPRespawnIntervalCheckPeriod={GetDecimal(instance, "server.increasePvpRespawnIntervalCheckPeriod", "300")}");
        lines.AppendLine($"IncreasePvPRespawnIntervalMultiplier={GetDecimal(instance, "server.increasePvpRespawnIntervalMultiplier", "1.0")}");
        lines.AppendLine($"IncreasePvPRespawnIntervalBaseAmount={GetDecimal(instance, "server.increasePvpRespawnIntervalBaseAmount", "60")}");
        lines.AppendLine($"MaxDifficulty={ToIniBool(GetBool(instance, "server.maxDifficulty"))}");
        lines.AppendLine($"bAutoPvETimer={ToIniBool(GetBool(instance, "server.autoPveTimer"))}");
        lines.AppendLine($"bAutoPvEUseSystemTime={ToIniBool(GetBool(instance, "server.autoPveUseSystemTime", true))}");
        lines.AppendLine($"bIncreasePvPRespawnInterval={ToIniBool(GetBool(instance, "server.increasePvpRespawnInterval"))}");
        lines.AppendLine($"bDisableFriendlyFire={ToIniBool(GetBool(instance, "server.disableFriendlyFire"))}");
        lines.AppendLine($"bPvEAllowTribeWar={ToIniBool(GetBool(instance, "server.pveAllowTribeWar"))}");
        lines.AppendLine($"bPvEAllowTribeWarCancel={ToIniBool(GetBool(instance, "server.pveAllowTribeWarCancel"))}");
        lines.AppendLine($"bPassiveDefensesDamageRiderlessDinos={ToIniBool(GetBool(instance, "server.passiveDefensesDamageRiderlessDinos"))}");
        lines.AppendLine($"bAllowCustomRecipes={ToIniBool(GetBool(instance, "server.allowCustomRecipes"))}");
        lines.AppendLine($"bUseCorpseLocator={ToIniBool(GetBool(instance, "server.useCorpseLocator", true))}");
        lines.AppendLine($"bAllowUnlimitedRespecs={ToIniBool(GetBool(instance, "server.allowUnlimitedRespecs"))}");
        lines.AppendLine($"bDisableStructurePlacementCollision={ToIniBool(GetBool(instance, "server.disableStructurePlacementCollision"))}");
        lines.AppendLine($"bAllowSpeedLeveling={ToIniBool(GetBool(instance, "server.allowSpeedLeveling"))}");
        lines.AppendLine($"bAllowFlyerSpeedLeveling={ToIniBool(GetBool(instance, "server.allowFlyerSpeedLeveling"))}");
        return lines.ToString();
    }

    private static string BuildModArguments(ServerInstance instance)
    {
        var mods = GetSetting(instance, "server.mods", "");
        var normalized = string.Join(
            ',',
            mods.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return string.IsNullOrWhiteSpace(normalized)
            ? string.Empty
            : "-mods=" + WindowsCommandLineEscaper.Quote(normalized);
    }

    private static string BuildBattlEyeArguments(ServerInstance instance)
    {
        return GetBool(instance, "battleye.enabled", true)
            ? string.Empty
            : "-NoBattlEye";
    }

    private static IReadOnlyDictionary<string, object?> BuildLaunchSettings(ServerInstance instance)
    {
        var settings = new Dictionary<string, object?>(instance.Settings, StringComparer.OrdinalIgnoreCase);
        AddDefault(settings, "network.queryPort", "27015");
        AddDefault(settings, "server.additionalArguments", "");
        return settings;
    }

    private static void AddDefault(IDictionary<string, object?> settings, string key, object value)
    {
        if (!settings.TryGetValue(key, out var existing) ||
            string.IsNullOrWhiteSpace(existing?.ToString()))
        {
            settings[key] = value;
        }
    }

    private static string NormalizeRconCommand(string command)
    {
        var trimmed = command.Trim();
        return string.Equals(trimmed, "status", StringComparison.OrdinalIgnoreCase)
            ? "ListPlayers"
            : trimmed;
    }

    private static bool IsEmptyRconResponse(string response)
    {
        return string.IsNullOrWhiteSpace(response) ||
            string.Equals(response.Trim(), "(no response)", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDecimal(ServerInstance instance, string key, string fallback)
    {
        var value = GetSetting(instance, key, fallback);
        return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : fallback;
    }

    private static bool GetBool(ServerInstance instance, string key, bool fallback = false)
    {
        var value = GetSetting(instance, key, fallback ? "true" : "false");
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static string ToIniBool(bool value) => value ? "True" : "False";

    private static string GetAdminOrRconPassword(ServerInstance instance)
    {
        var adminPassword = GetSetting(instance, "server.adminPassword", "");
        return string.IsNullOrWhiteSpace(adminPassword)
            ? GetSetting(instance, "rcon.password", "")
            : adminPassword;
    }

    private static string GetConnectableHost(string host)
    {
        return string.Equals(host, "0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "::", StringComparison.OrdinalIgnoreCase)
            ? "127.0.0.1"
            : host;
    }

    private static string GetSetting(ServerInstance instance, string key, string fallback)
    {
        return GetSetting(instance.Settings, key, fallback);
    }

    private static string GetSetting(IReadOnlyDictionary<string, object?> settings, string key, string fallback)
    {
        return settings.TryGetValue(key, out var value) ? value?.ToString() ?? fallback : fallback;
    }
}
