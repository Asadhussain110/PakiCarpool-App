using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PakiCarpool.Repository.EFCore.ValueConverters;

public sealed class EncryptedStringConverter : ValueConverter<string, string>
{
    private static readonly byte[] Magic = [0x50, 0x41, 0x4B, 0x49, 0x43, 0x41, 0x52, 0x50, 0x47, 0x43, 0x4D, 0x01];
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public EncryptedStringConverter(string base64Key)
        : this(new Dictionary<byte, string>
        {
            [0] = base64Key,
            [1] = base64Key
        }, 1, base64Key)
    {
    }

    public EncryptedStringConverter(
        IReadOnlyDictionary<byte, string> base64Keys,
        byte activeVersion,
        string legacyCbcBase64Key,
        bool tolerateDoubleEncryption = false)
        : this(
            CreateSettings(base64Keys, activeVersion, legacyCbcBase64Key),
            tolerateDoubleEncryption)
    {
    }

    private EncryptedStringConverter(EncryptionSettings settings, bool tolerateDoubleEncryption)
        : base(
            value => Encrypt(value, settings),
            value => Decrypt(value, settings, tolerateDoubleEncryption))
    {
    }

    private static EncryptionSettings CreateSettings(
        IReadOnlyDictionary<byte, string> base64Keys,
        byte activeVersion,
        string legacyCbcBase64Key)
    {
        var keys = base64Keys.ToDictionary(pair => pair.Key, pair => Convert.FromBase64String(pair.Value));
        if (!keys.TryGetValue(activeVersion, out var activeKey) || activeKey.Length != 32)
            throw new InvalidOperationException("The active AES encryption key must be a Base64-encoded 256-bit key.");

        if (keys.Values.Any(key => key.Length != 32))
            throw new InvalidOperationException("Every AES encryption key must be a Base64-encoded 256-bit key.");

        var legacyCbcBytes = Convert.FromBase64String(legacyCbcBase64Key);
        if (legacyCbcBytes.Length != 32)
            throw new InvalidOperationException("The legacy AES encryption key must be a Base64-encoded 256-bit key.");

        return new EncryptionSettings(keys, activeVersion, legacyCbcBytes);
    }

    /// <summary>
    /// Returns the existing inner ciphertext for a legacy double-encrypted plate.
    /// Single-layer values are returned unchanged with a false result.
    /// </summary>
    public static bool TryRemoveDoubleEncryption(
        string ciphertext,
        IReadOnlyDictionary<byte, string> base64Keys,
        byte activeVersion,
        string legacyCbcBase64Key,
        out string singleLayerCiphertext)
    {
        var settings = CreateSettings(base64Keys, activeVersion, legacyCbcBase64Key);
        var decryptedOnce = DecryptSingleLayer(ciphertext, settings);
        if (TryDecryptNested(decryptedOnce, settings, out _))
        {
            singleLayerCiphertext = decryptedOnce;
            return true;
        }

        singleLayerCiphertext = ciphertext;
        return false;
    }

    /// <summary>
    /// Re-applies the removed outer layer when rolling back the data migration.
    /// Existing double-encrypted values are returned unchanged with a false result.
    /// </summary>
    public static bool TryRestoreDoubleEncryption(
        string ciphertext,
        IReadOnlyDictionary<byte, string> base64Keys,
        byte activeVersion,
        string legacyCbcBase64Key,
        out string doubleLayerCiphertext)
    {
        var settings = CreateSettings(base64Keys, activeVersion, legacyCbcBase64Key);
        var decryptedOnce = DecryptSingleLayer(ciphertext, settings);
        if (TryDecryptNested(decryptedOnce, settings, out _))
        {
            doubleLayerCiphertext = ciphertext;
            return false;
        }

        doubleLayerCiphertext = Encrypt(ciphertext, settings);
        return true;
    }

    private static string Encrypt(string plaintext, EncryptionSettings settings)
    {
        if (string.IsNullOrEmpty(plaintext))
            return plaintext;

        var header = new byte[Magic.Length + 1];
        Buffer.BlockCopy(Magic, 0, header, 0, Magic.Length);
        header[^1] = settings.ActiveVersion;

        var nonce = RandomNumberGenerator.GetBytes(12);
        var plainBytes = StrictUtf8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(settings.Keys[settings.ActiveVersion], tag.Length))
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag, header);

        var payload = new byte[header.Length + nonce.Length + cipherBytes.Length + tag.Length];
        Buffer.BlockCopy(header, 0, payload, 0, header.Length);
        Buffer.BlockCopy(nonce, 0, payload, header.Length, nonce.Length);
        Buffer.BlockCopy(cipherBytes, 0, payload, header.Length + nonce.Length, cipherBytes.Length);
        Buffer.BlockCopy(tag, 0, payload, payload.Length - tag.Length, tag.Length);
        return Convert.ToBase64String(payload);
    }

    private static string Decrypt(
        string ciphertext,
        EncryptionSettings settings,
        bool tolerateDoubleEncryption)
    {
        var plaintext = DecryptSingleLayer(ciphertext, settings);
        return tolerateDoubleEncryption && TryDecryptNested(plaintext, settings, out var nestedPlaintext)
            ? nestedPlaintext
            : plaintext;
    }

    private static string DecryptSingleLayer(string ciphertext, EncryptionSettings settings)
    {
        if (string.IsNullOrEmpty(ciphertext))
            return ciphertext;

        var payload = Convert.FromBase64String(ciphertext);
        if (HasMagic(payload))
            return DecryptCurrent(payload, settings);

        if (TryDecryptLegacyGcm(payload, settings, out var gcmPlaintext))
            return gcmPlaintext;

        return DecryptLegacyCbc(payload, settings.LegacyCbcKey);
    }

    private static string DecryptCurrent(byte[] payload, EncryptionSettings settings)
    {
        var headerLength = Magic.Length + 1;
        if (payload.Length < headerLength + 12 + 16)
            throw new CryptographicException("Encrypted value is invalid or truncated.");

        var version = payload[Magic.Length];
        if (!settings.Keys.TryGetValue(version, out var key))
            throw new CryptographicException($"AES key version {version} is not configured.");

        var nonce = payload.AsSpan(headerLength, 12).ToArray();
        var tag = payload.AsSpan(payload.Length - 16, 16).ToArray();
        var cipherLength = payload.Length - headerLength - nonce.Length - tag.Length;
        var cipherBytes = payload.AsSpan(headerLength + nonce.Length, cipherLength).ToArray();
        var plaintext = new byte[cipherLength];

        using (var aes = new AesGcm(key, tag.Length))
            aes.Decrypt(nonce, cipherBytes, tag, plaintext, payload.AsSpan(0, headerLength));

        return StrictUtf8.GetString(plaintext);
    }

    private static bool TryDecryptLegacyGcm(byte[] payload, EncryptionSettings settings, out string plaintext)
    {
        plaintext = string.Empty;
        if (payload.Length < 1 + 12 + 16 || !settings.Keys.TryGetValue(payload[0], out var key))
            return false;

        var nonce = payload.AsSpan(1, 12).ToArray();
        var tag = payload.AsSpan(payload.Length - 16, 16).ToArray();
        var cipherLength = payload.Length - 1 - nonce.Length - tag.Length;
        var cipherBytes = payload.AsSpan(1 + nonce.Length, cipherLength).ToArray();
        var plaintextBytes = new byte[cipherLength];

        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, cipherBytes, tag, plaintextBytes);
            plaintext = StrictUtf8.GetString(plaintextBytes);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool TryDecryptNested(
        string value,
        EncryptionSettings settings,
        out string plaintext)
    {
        plaintext = string.Empty;
        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return false;
        }

        if (HasMagic(payload))
        {
            plaintext = DecryptCurrent(payload, settings);
            return true;
        }

        if (TryDecryptLegacyGcm(payload, settings, out plaintext))
            return true;

        if (payload.Length < 32 || (payload.Length - 16) % 16 != 0)
            return false;

        try
        {
            plaintext = DecryptLegacyCbc(payload, settings.LegacyCbcKey);
            return true;
        }
        catch (CryptographicException)
        {
            plaintext = string.Empty;
            return false;
        }
    }

    private static string DecryptLegacyCbc(byte[] payload, byte[] key)
    {
        if (payload.Length < 32 || (payload.Length - 16) % 16 != 0)
            throw new CryptographicException("Encrypted value is invalid or truncated.");

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = payload.AsSpan(0, 16).ToArray();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        try
        {
            using var decryptor = aes.CreateDecryptor();
            var plaintextBytes = decryptor.TransformFinalBlock(payload, 16, payload.Length - 16);
            return StrictUtf8.GetString(plaintextBytes);
        }
        catch (Exception exception) when (exception is CryptographicException or DecoderFallbackException)
        {
            throw new CryptographicException("Encrypted value could not be authenticated or decoded.", exception);
        }
    }

    private static bool HasMagic(byte[] payload)
        => payload.Length >= Magic.Length && payload.AsSpan(0, Magic.Length).SequenceEqual(Magic);

    private sealed record EncryptionSettings(
        Dictionary<byte, byte[]> Keys,
        byte ActiveVersion,
        byte[] LegacyCbcKey);
}
