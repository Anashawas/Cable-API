using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// Generic admin-editable key/value setting (e.g. the global nearby-radius).
/// Keys are unique; values stored as strings and parsed by the reader.
/// </summary>
public class AppSetting : BaseAuditableEntity
{
    public string Key { get; set; } = null!;
    public string Value { get; set; } = null!;
}
