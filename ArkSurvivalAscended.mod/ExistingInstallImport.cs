using WindowsGSH.Core.Modules;

namespace WindowsGSH.Modules.ArkSurvivalAscended;

internal static class ExistingInstallImport
{
    public static bool CanImport(IGameServerModule module, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        var installPath = ResolveInstallPath(module, path);
        return File.Exists(Path.Combine(installPath, module.Runtime.StartPath));
    }

    public static async Task<ModuleExistingServerImportProbe> PreviewAsync(
        IGameServerModule module,
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourcePath = Path.GetFullPath(path);
        var installPath = ResolveInstallPath(module, sourcePath);
        var probe = new ServerInstance(
            Path.GetFileName(sourcePath), module.Name, module.Id, installPath, installPath,
            Path.Combine(sourcePath, "ServerConfig.json"), new Dictionary<string, object?>());
        var settings = new Dictionary<string, object?>(
            await module.ReadConfigFileSettingsAsync(probe, cancellationToken),
            StringComparer.OrdinalIgnoreCase);
        var warnings = settings.Count == 0
            ? new[] { "No supported settings were detected; review the module defaults before importing." }
            : Array.Empty<string>();
        return new ModuleExistingServerImportProbe(module.GetServerName(settings), installPath, settings, warnings);
    }

    private static string ResolveInstallPath(IGameServerModule module, string path)
    {
        var sourcePath = Path.GetFullPath(path);
        if (File.Exists(Path.Combine(sourcePath, module.Runtime.StartPath)))
        {
            return sourcePath;
        }

        var serverFilesPath = Path.Combine(sourcePath, "serverfiles");
        return File.Exists(Path.Combine(serverFilesPath, module.Runtime.StartPath)) ? serverFilesPath : sourcePath;
    }
}
