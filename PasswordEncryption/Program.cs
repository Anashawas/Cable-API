using Cable.Security.Encryption;
using Cable.Security.Encryption.Interfaces;
using Cable.Security.Encryption.Options;
using Microsoft.Extensions.Options;

var encryptionKey = RequestRequiredUserValue("Please write the encryption key:");


var selectedOption = RequestOperationType();

if (selectedOption == "1")
{
    var password = RequestRequiredUserValue("Please write the text you wish to secure:");
    var encryptedText = ProcessPasswordEncryption(encryptionKey, password);
    
    Console.WriteLine($"The encrypted value is:{encryptedText}");
}
else
{
    var encryptedPassword = RequestRequiredUserValue("Please write the text you wish to decrypt:");
    var decryptedText = ProcessPasswordDecryption(encryptionKey, encryptedPassword);

    Console.WriteLine($"The decrypted value is:{decryptedText}");

}



Console.WriteLine("Press Any Key to close");

string ProcessPasswordEncryption(string encryptionKey,string password)
{
    IDataEncryption dataEncryption = new TripleDESDataEncryption(Options.Create(new EncryptionOptions()
    {
        Key = encryptionKey
    }));

    return dataEncryption.Encrypt(password);
}

string ProcessPasswordDecryption(string encryptionKey, string encryptedPassword)
{
    IDataEncryption dataEncryption = new TripleDESDataEncryption(Options.Create(new EncryptionOptions()
    {
        Key = encryptionKey
    }));

    return dataEncryption.Decrypt(encryptedPassword);
}

string RequestRequiredUserValue(string message)
{
    Console.Write(message);
    var value = Console.ReadLine();

    while (string.IsNullOrEmpty(value?.Trim()))
    {
        Console.Write(message);
        value = Console.ReadLine();
    }

    return value;
}

string RequestOperationType()
{
    var message = "Please select [1] for encryption,[2] for decryption:";
    Console.Write(message);

    var value = Console.ReadLine();

    var validValues = new string[] { "1", "2" };

    while (string.IsNullOrEmpty(value?.Trim()) || !validValues.Contains(value) )
    {
        Console.Write(message);
        value = Console.ReadLine();
    }

    return value;
}