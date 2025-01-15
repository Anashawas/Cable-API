namespace Application.Common.Interfaces;

public interface IIdentityService
{
    Task<bool> HasPrivilege(int userId, string privilegeKey, CancellationToken cancellationToken = default);
    Task<string> GetUserName(int userId, CancellationToken cancellationToken = default);
    Task<string> GetName(int userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<string>> GetPrivileges(int userId, CancellationToken cancellationToken = default);
}