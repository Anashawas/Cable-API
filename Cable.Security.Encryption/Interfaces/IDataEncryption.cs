namespace Cable.Security.Encryption.Interfaces;

public interface IDataEncryption
{
    /// <summary>
    /// Encrypts the text.
    /// </summary>
    /// <param name="textToEncrypt">The text to encrypt</param>
    /// <returns>The encrypted text</returns>
    string Encrypt(string textToEncrypt);

    /// <summary>
    /// Decrypts the text
    /// </summary>
    /// <param name="textToDecrypt">The text to decrypt</param>
    /// <returns>The decrypted text</returns>
    string Decrypt(string textToDecrypt);
}
