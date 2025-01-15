using System.Security.Cryptography;

namespace Cable.Security.Encryption.Options;

public class PasswordHasherOptions
{
    private static readonly RandomNumberGenerator _defaultRng = RandomNumberGenerator.Create(); // secure PRNG

    /// <summary>
    /// Gets or sets the number of iterations used when hashing passwords using PBKDF2. Default is 100,000.
    /// </summary>
    /// <value>
    /// The number of iterations used when hashing passwords using PBKDF2.
    /// </value>
    /// <remarks>
    /// The value must be a positive integer.
    /// </remarks>
    public int IterationCount { get; set; } = 100_000;

    internal RandomNumberGenerator Rng { get; set; } = _defaultRng;
}
