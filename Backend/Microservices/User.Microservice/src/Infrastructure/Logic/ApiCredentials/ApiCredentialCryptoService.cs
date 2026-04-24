using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Logic.ApiCredentials;

public sealed class ApiCredentialCryptoService
{
    private readonly byte[] _keyBytes;

    public ApiCredentialCryptoService(IConfiguration configuration)
    {
        var keyMaterial = configuration["ApiCredentials:EncryptionKey"]
                          ?? configuration["ApiCredentials__EncryptionKey"]
                          ?? configuration["CredentialEncryption:Key"]
                          ?? configuration["CredentialEncryption__Key"]
                          ?? configuration["Jwt:SecretKey"]
                          ?? configuration["Jwt__SecretKey"]
                          ?? $"{ApiCredentialCatalog.ServiceName}.ApiCredentials.FallbackKey";

        _keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial));
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return string.Empty;
        }

        using var aes = Aes.Create();
        aes.Key = _keyBytes;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        var payload = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, payload, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, payload, aes.IV.Length, cipherBytes.Length);
        return Convert.ToBase64String(payload);
    }

    public string Decrypt(string encryptedValue)
    {
        if (string.IsNullOrEmpty(encryptedValue))
        {
            return string.Empty;
        }

        var payload = Convert.FromBase64String(encryptedValue);
        using var aes = Aes.Create();
        aes.Key = _keyBytes;

        var iv = new byte[aes.BlockSize / 8];
        var cipherBytes = new byte[payload.Length - iv.Length];
        Buffer.BlockCopy(payload, 0, iv, 0, iv.Length);
        Buffer.BlockCopy(payload, iv.Length, cipherBytes, 0, cipherBytes.Length);

        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
