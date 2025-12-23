using Application.Common.Extensions;

namespace Application.Common.Interfaces.Repositories;

public partial interface IApplicationDbContextProcedures
{
    Task<int> CleanupExpiredOtpRecordsAsync(OutputParameter<int> returnValue = null, CancellationToken cancellationToken = default);
}