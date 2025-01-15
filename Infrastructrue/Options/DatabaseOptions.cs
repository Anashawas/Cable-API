namespace Infrastructrue.Options;

public class DatabaseOptions
{
    public const string ConfigName = "Database";
    /// <summary>
    /// The timeout of the database commands in seconds
    /// </summary>
    public int CommandTimeOutInSeconds { get; set; }

    /// <summary>
    /// The default database schema
    /// </summary>
    public string DefaultSchema { get; set; }

    /// <summary>
    /// Enables Entity Framework sensitive data logging
    /// </summary>
    public bool EnableSensitiveDataLogging { get; set; }

    /// <summary>
    /// Enables Entity Framework detailed error logging
    /// </summary>
    public bool EnableDetailedErrors { get; set; }

}
