using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Infrastructrue.Persistence.Converters;

public class TimeOnlyValueConverter() : ValueConverter<TimeOnly, TimeSpan>(timeOnly => timeOnly.ToTimeSpan(),
    timeSpan => TimeOnly.FromTimeSpan(timeSpan));