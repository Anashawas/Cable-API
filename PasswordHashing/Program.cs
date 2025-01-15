using Cable.Security.Encryption;
using Cable.Security.Encryption.Interfaces;


//TODO: accept options
IPasswordHasher _passwordHasher = new PasswordHasher();

Console.Write("Please write the text you wish to secure:");

string password = Console.ReadLine();

while (string.IsNullOrEmpty(password?.Trim()))
{
    Console.Write("Please write the text you wish to secure:");
    password = Console.ReadLine();
}

string encryptedText = ProcessHashingRequest(password);
Console.WriteLine(encryptedText);

Console.WriteLine("Press Any Key to close");

string ProcessHashingRequest(string password) => _passwordHasher.HashPassword(password);