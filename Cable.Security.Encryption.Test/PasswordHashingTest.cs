using Cable.Security.Encryption.Interfaces;
using Cable.Security.Encryption.Models;
using Cable.Security.Encryption.Options;
using Xunit.Abstractions;

namespace Cable.Security.Encryption.Test;

public class PasswordHashingTest
{
    private readonly ITestOutputHelper _output;

    public PasswordHashingTest(ITestOutputHelper output)
    {
        _output = output;
    }



    [Fact]
    public void Can_Hash_Password()
    {
        IPasswordHasher passwordHasher = new PasswordHasher();

        string hashedPassword = passwordHasher.HashPassword("This_Is_My_Password");

        _output.WriteLine(hashedPassword);

        Assert.NotNull(hashedPassword);
    }

    [Theory]
    [InlineData("This_Is_My_Password")]
    [InlineData("SolidSnake1987@")]
    [InlineData("Metal_Gear_Solid")]
    public void IS_Password_Verifiaction_Success(string password)
    {
        IPasswordHasher passwordHasher = new PasswordHasher();

        string hashedPassword = passwordHasher.HashPassword(password);

        var result = passwordHasher.VerifyHashedPassword(password, hashedPassword);

        Assert.True(result == PasswordVerificationResult.Success);
    }

    [Theory]
    [InlineData("This_Is_My_Password")]
    [InlineData("SolidSnake1987@")]
    [InlineData("Metal_Gear_Solid")]
    public void IS_Password_Verifiaction_SuccessToRehash(string password)
    {
        IPasswordHasher passwordHashe = new PasswordHasher();

        string hashedPassword = passwordHashe.HashPassword(password);

        passwordHashe = new PasswordHasher(Options.Create<PasswordHasherOptions>(new PasswordHasherOptions()
        {
            IterationCount = 200000
        }));

        var result = passwordHashe.VerifyHashedPassword(password, hashedPassword);

        Assert.True(result == PasswordVerificationResult.SuccessRehashNeeded);
    }

}