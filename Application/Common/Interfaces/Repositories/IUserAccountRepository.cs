using Application.Common.Models;
using Domain.Enitites;

namespace Application.Common.Interfaces.Repositories;

public interface IUserAccountRepository
{
    /// <summary>
    /// Gets complete user account details by user ID using optimized raw SQL
    /// </summary>
    Task<UserAccount> GetUserDetailsByIdAsync(int userId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets complete user account details by email using optimized raw SQL
    /// </summary>
    Task<UserAccount> GetUserDetailsByEmailAsync(string email, CancellationToken cancellationToken = default);

}