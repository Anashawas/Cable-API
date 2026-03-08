namespace infrastructrue.Options;

public class FirebaseOption
{
    public const string ConfigName = "Firebase";

    /// <summary>
    /// Service account path for User App (Cable)
    /// Example: "Firebase\\ServiceAccount\\Cable\\{Environment}\\ServiceAccount.json"
    /// </summary>
    public string UserAppServiceAccountPath { get; set; } = null!;

    /// <summary>
    /// Service account path for Station App (CableStation)
    /// Example: "Firebase\\ServiceAccount\\CableStation\\{Environment}\\ServiceAccount.json"
    /// </summary>
    public string StationAppServiceAccountPath { get; set; } = null!;
}