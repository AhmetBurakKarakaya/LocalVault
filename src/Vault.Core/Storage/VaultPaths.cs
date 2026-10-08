namespace Vault.Core.Storage;

public static class VaultPaths
{
    public const string PathEnvironmentVariable = "LOCALVAULT_PATH";

    /// <summary>
    /// Varsayılan kasa yolu: LOCALVAULT_PATH ortam değişkeni, yoksa
    /// %APPDATA%\LocalVault\vault.json (Linux/macOS'ta ~/.config/LocalVault/vault.json).
    /// </summary>
    public static string DefaultVaultPath
    {
        get
        {
            var fromEnv = Environment.GetEnvironmentVariable(PathEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(fromEnv))
                return Path.GetFullPath(fromEnv);

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "LocalVault", "vault.json");
        }
    }
}
