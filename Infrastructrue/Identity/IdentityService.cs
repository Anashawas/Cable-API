using Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Infrastructrue.Identity;

public class IdentityService : IIdentityService
{
    private readonly IApplicationDbContext _applicationDbContext;

    public IdentityService(IApplicationDbContext applicationDbContext)
        => _applicationDbContext = applicationDbContext;

    public async Task<IReadOnlyCollection<string>> GetPrivileges(int userId,
        CancellationToken cancellationToken = default)
        => await (from u in _applicationDbContext.UserAccounts
                join role in _applicationDbContext.Roles
                    on u.RoleId equals role.Id
                join rolePrivilege in _applicationDbContext.RolePrivlages
                    on role.Id equals rolePrivilege.RoleId
                join privilege in _applicationDbContext.Privilages
                    on rolePrivilege.PrivilegeId equals privilege.Id
                where u.Id == userId && !role.IsDeleted && !rolePrivilege.IsDeleted
                select privilege.Code
            ).AsNoTracking().ToListAsync(cancellationToken);


    public async Task<string> GetUserName(int userId, CancellationToken cancellationToken = default)
        => (await _applicationDbContext.UserAccounts.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken))
            ?.UserName;

    public async Task<bool> HasPrivilege(int userId, string privilegeKey, CancellationToken cancellationToken = default)
        => await (from u in _applicationDbContext.UserAccounts
                join role in _applicationDbContext.Roles
                    on u.RoleId equals role.Id
                join rolePrivilege in _applicationDbContext.RolePrivlages
                    on role.Id equals rolePrivilege.RoleId
                join privilege in _applicationDbContext.Privilages
                    on rolePrivilege.PrivilegeId equals privilege.Id
                where u.Id == userId && u.IsActive && !u.IsDeleted && !role.IsDeleted && !rolePrivilege.IsDeleted
                      && privilege.Code == privilegeKey
                select privilege.Code
            ).AsNoTracking().AnyAsync(cancellationToken);

    public async Task<string> GetName(int userId, CancellationToken cancellationToken = default)
        => (await _applicationDbContext.UserAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken))
            ?.Name;
}