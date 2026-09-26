using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace PakiCarpool.Security.Helpers;

/// <summary>
/// AES-256 encryption helper for sensitive fields (DriverCar.LicensePlate, TotpSecret.Secret).
/// Key is read from environment variable AES_ENCRYPTION_KEY via IConfiguration.
/// Uses AES-CBC with PKCS7 padding and a random IV prepended to the ciphertext.
/// </summary>
public class AesEncryptionHelper
{
    private readonly byte[] _key;

    public AesEncryptionHelper(IConfiguration configuration)
    {
        var keyString = configuration["AES_ENCRYPTION_KEY"]
            ?? throw new InvalidOperationException("AES encryption key is not configured.");

        _key = Convert.FromBase64String(keyString);

        if (_key.Length != 32)
        {
            throw new InvalidOperationException("AES encryption key must be 256 bits (32 bytes) encoded as Base64.");
        }
    }

    /// <summary>
    /// Encrypts plaintext using AES-256-CBC. Returns Base64 string with IV prepended.
    /// </summary>
    public string Encrypt(string plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertextBytes = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

        // Prepend IV to ciphertext for storage
        var result = new byte[aes.IV.Length + ciphertextBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(ciphertextBytes, 0, result, aes.IV.Length, ciphertextBytes.Length);

        return Convert.ToBase64String(result);
    }

    /// <summary>
    /// Decrypts a Base64 AES-256-CBC ciphertext (with prepended IV) back to plaintext.
    /// </summary>
    public string Decrypt(string ciphertext)
    {
        Console.Error.WriteLine($"AES decrypt raw input length: {ciphertext.Length}");
        var fullCipher = Convert.FromBase64String(ciphertext);
        Console.Error.WriteLine($"AES decrypt decoded byte length: {fullCipher.Length}");

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        // Extract IV from the beginning of the ciphertext
        var iv = new byte[aes.BlockSize / 8];
        var cipher = new byte[fullCipher.Length - iv.Length];
        Console.Error.WriteLine($"AES decrypt IV length: {iv.Length}");
        Console.Error.WriteLine($"AES decrypt ciphertext length: {cipher.Length}");

        Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
        Buffer.BlockCopy(fullCipher, iv.Length, cipher, 0, cipher.Length);

        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var plaintextBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);

        return Encoding.UTF8.GetString(plaintextBytes);
    }
}
