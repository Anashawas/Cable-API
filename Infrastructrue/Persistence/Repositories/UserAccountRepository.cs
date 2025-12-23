using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Models;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Infrastructrue.Persistence.Repositories;

public class UserAccountRepository : IUserAccountRepository
{
    private readonly ApplicationDbContext _context;

    public UserAccountRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<UserAccount> GetUserDetailsByIdAsync(int userId, CancellationToken cancellationToken = default)
        =>
            GetUserDetailsResult.ToUserAccount(await GetUserDetailsFromDatabase(userId, null, cancellationToken));


    public async Task<UserAccount> GetUserDetailsByEmailAsync(string email,
        CancellationToken cancellationToken = default)
        => GetUserDetailsResult.ToUserAccount(await GetUserDetailsFromDatabase(null, email, cancellationToken));


    private async Task<List<GetUserDetailsResult>> GetUserDetailsFromDatabase(int? userId, string? email,
        CancellationToken cancellationToken)
    {
        const string sql = @"
        SELECT 
            u.Id,
            u.Name,
            u.Phone,
            u.IsActive,
            u.Email,
            u.RegistrationProvider,
            u.FirebaseUId,
            u.Country,
            u.City,
            u.RoleId,
            u.Password,
            u.IsDeleted,
            r.Name as RoleName,
            uc.Id as UserCarId,
            cm.Id as CarModelId,
            cm.Name as CarModelName,
            ct.Id as CarTypeId,
            ct.Name as CarTypeName,
            pt.Id as PlugTypeId,
            pt.Name as PlugTypeName
        FROM UserAccount u
        LEFT JOIN Role r ON u.RoleId = r.Id
        LEFT JOIN UserCar uc ON u.Id = uc.UserId AND uc.IsDeleted = 0
        LEFT JOIN CarModel cm ON uc.CarModelId = cm.Id
        LEFT JOIN CarType ct ON cm.CarTypeId = ct.Id
        LEFT JOIN PlugType pt ON uc.PlugTypeId = pt.Id
        WHERE u.IsDeleted = 0 
        AND (@userId IS NULL OR u.Id = @userId)
        AND (@email IS NULL OR LOWER(u.Email) = LOWER(@email))";


        var parameters = new[]
        {
            new SqlParameter("@userId", (object?)userId ?? DBNull.Value),
            new SqlParameter("@email", (object?)email ?? DBNull.Value)
        };
        var result = await _context.Database
            .SqlQueryRaw<GetUserDetailsResult>(sql, parameters)
            .ToListAsync(cancellationToken);

        if (!result.Any())
        {
            if (userId.HasValue)
                throw new NotFoundException(nameof(UserAccount), userId.Value);
            if (!string.IsNullOrWhiteSpace(email))
                throw new NotFoundException(nameof(UserAccount), email);
        }

        return result;
    }
}