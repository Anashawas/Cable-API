using System.Security.Cryptography;
using System.Text;
using Cable.Core.Helpers;
using Cable.Security.Encryption.Interfaces;
using Cable.Security.Encryption.Options;
using Microsoft.Extensions.Options;

namespace Cable.Security.Encryption;

public class TripleDESDataEncryption : IDataEncryption
{
    private string _key;

    private byte[] _keyBytes { get { return UTF8Encoding.UTF8.GetBytes(_key); } }

    public TripleDESDataEncryption(IOptions<EncryptionOptions> encryptionOptions)
    {
        ArgumentNullException.ThrowIfNull(encryptionOptions);
        ArgumentNullException.ThrowIfNull(encryptionOptions.Value);
        ExceptionHelper.ThrowIfNullOrEmpty(encryptionOptions.Value.Key);

        _key = encryptionOptions.Value.Key;
    }

    public string Encrypt(string textToEncrypt)
    {
        byte[] toEncryptArray = UTF8Encoding.UTF8.GetBytes(textToEncrypt);

        var tripleDes = GetTripleDES();

        ICryptoTransform cTransform = tripleDes.CreateEncryptor();
        byte[] resultArray = cTransform.TransformFinalBlock(toEncryptArray, 0, toEncryptArray.Length);
        tripleDes.Clear();

        return Convert.ToBase64String(resultArray, 0, resultArray.Length);
    }

    public string Decrypt(string textToDecrypt)
    {
        byte[] toEncryptArray = Convert.FromBase64String(textToDecrypt);

        ICryptoTransform cTransform = GetTripleDES().CreateDecryptor();

        byte[] resultArray = cTransform.TransformFinalBlock(toEncryptArray, 0, toEncryptArray.Length);

        return UTF8Encoding.UTF8.GetString(resultArray);
    }

    private TripleDES GetTripleDES()
    {
        TripleDES tdes = TripleDES.Create();
        tdes.Key = _keyBytes;
        tdes.Mode = CipherMode.ECB;
        tdes.Padding = PaddingMode.PKCS7;

        return tdes;
    }
}