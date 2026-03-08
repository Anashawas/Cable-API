namespace Cable.Core.Enums;

/// <summary>
/// Defines the types of Firebase applications supported in the Cable system
/// </summary>
public enum FirebaseAppType
{
    /// <summary>
    /// Main user application (Cable) - Default
    /// </summary>
    UserApp = 1,

    /// <summary>
    /// Station owner/manager application (CableStation)
    /// </summary>
    StationApp = 2
}
