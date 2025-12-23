using Application.Common.Extensions;
using Application.Common.Interfaces.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Infrastructrue.Persistence;

public partial class ApplicationDbContext
{
    private IApplicationDbContextProcedures _procedures;

    public virtual IApplicationDbContextProcedures Procedures
    {
        get
        {
            if (_procedures is null) _procedures = new ApplicationDbContextProcedures(this);
            return _procedures;
        }
        set
        {
            _procedures = value;
        }
    }

    public IApplicationDbContextProcedures GetProcedures()
    {
        return Procedures;
    }
}

public partial class ApplicationDbContextProcedures : IApplicationDbContextProcedures
{
    private readonly ApplicationDbContext _context;

    public ApplicationDbContextProcedures(ApplicationDbContext context)
    {
        _context = context;
    }

    public virtual async Task<int> CleanupExpiredOtpRecordsAsync(OutputParameter<int> returnValue = null, CancellationToken cancellationToken = default)
    {
        var parameterreturnValue = new SqlParameter
        {
            ParameterName = "returnValue",
            Direction = System.Data.ParameterDirection.Output,
            SqlDbType = System.Data.SqlDbType.Int,
        };

        var sqlParameters = new []
        {
            parameterreturnValue,
        };
        var _ = await _context.Database.ExecuteSqlRawAsync("EXEC @returnValue = [dbo].[CleanupExpiredOtpRecords]", sqlParameters, cancellationToken);

        returnValue?.SetValue(parameterreturnValue.Value);

        return _;
    }
}