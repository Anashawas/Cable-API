using Cable.Security.Encryption.Models;

namespace Cable.Security.Encryption.Interfaces;

public interface IPasswordHasher
{
    /// <summary>
    /// Hashes the password
    /// </summary>
    /// <param name="password">The password to has</param>
    /// <returns>hashed password</returns>
    string HashPassword(string password);

    /// <summary>
    /// Verifies that the password and the hashed password are equal
    /// </summary>
    /// <param name="password">The password to verify</param>
    /// <param name="hashedPassword">The hashed password</param>
    /// <returns></returns>
    PasswordVerificationResult VerifyHashedPassword(string password,string hashedPassword);
}