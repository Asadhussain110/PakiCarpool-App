using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace PakiCarpool.Security.Helpers;

/// <summary>
/// AES-GCM helper for sensitive fields, with versioned keys for future rotation.
/// Payload format: [keyVersion][12-byte nonce][ciphertext][16-byte tag].
/// </summary>
public class AesEncryptionHelper
{
    private static readonly byte[] Magic = [0x50, 0x41, 0x4B, 0x49, 0x43, 0x41, 0x52, 0x50, 0x47, 0x43, 0x4D, 0x01];
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly Dictionary<byte, byte[]> _keys;
    private readonly byte _activeVersion;
    private readonly byte[] _legacyCbcKey;

    public AesEncryptionHelper(IConfiguration configuration)
    {
        var configuredKeys = new Dictionary<byte, byte[]>();

        var legacyKeyValue = configuration["AES_ENCRYPTION_KEY"];
        if (string.IsNullOrWhiteSpace(legacyKeyValue))
            legacyKeyValue = null;
        foreach (var keyConfig in new[]
                 {
                     (Version: (byte)0, KeyName: "AES_ENCRYPTION_KEY"),
                     (Version: (byte)1, KeyName: "AES_KEY_V1"),
                     (Version: (byte)2, KeyName: "AES_KEY_V2")
                 })
        {
            var value = configuration[keyConfig.KeyName];
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var bytes = Convert.FromBase64String(value);
            if (bytes.Length != 32)
            {
                throw new InvalidOperationException($"{keyConfig.KeyName} must be a Base64-encoded 256-bit key.");
            }

            configuredKeys[keyConfig.Version] = bytes;
        }

        if (!string.IsNullOrWhiteSpace(legacyKeyValue))
        {
            configuredKeys[0] = Convert.FromBase64String(legacyKeyValue);
            configuredKeys[1] = configuredKeys[0];
        }

        if (configuredKeys.Count == 0)
        {
            throw new InvalidOperationException("No AES encryption keys are configured. Set AES_KEY_V1 or AES_ENCRYPTION_KEY in environment variables.");
        }

        _keys = configuredKeys;
        _activeVersion = _keys.Keys.Max();
        var legacyCbcValue = new[]
                {
                    legacyKeyValue,
                    configuration["AES_KEY_V1"],
                    configuration["AES_KEY_V2"]
                }
            .First(value => !string.IsNullOrWhiteSpace(value))!;
        _legacyCbcKey = Convert.FromBase64String(legacyCbcValue);
    }

    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return plaintext;
        }

        var header = new byte[Magic.Length + 1];
        Buffer.BlockCopy(Magic, 0, header, 0, Magic.Length);
        header[^1] = _activeVersion;
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);
        var plainBytes = StrictUtf8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(GetKey(_activeVersion), tag.Length);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag, header);

        var payload = new byte[header.Length + nonce.Length + cipherBytes.Length + tag.Length];
        Buffer.BlockCopy(header, 0, payload, 0, header.Length);
        Buffer.BlockCopy(nonce, 0, payload, header.Length, nonce.Length);
        Buffer.BlockCopy(cipherBytes, 0, payload, header.Length + nonce.Length, cipherBytes.Length);
        Buffer.BlockCopy(tag, 0, payload, payload.Length - tag.Length, tag.Length);

        return Convert.ToBase64String(payload);
    }

    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
        {
            return ciphertext;
        }

        var payload = Convert.FromBase64String(ciphertext);
        if (HasMagic(payload))
            return DecryptCurrent(payload);

        if (TryDecryptLegacyGcm(payload, out var plaintext))
            return plaintext;

        return DecryptLegacyCbc(payload);
    }

    private string DecryptCurrent(byte[] payload)
    {
        var headerLength = Magic.Length + 1;
        if (payload.Length < headerLength + 12 + 16)
            throw new CryptographicException("Ciphertext is invalid or corrupted.");

        var version = payload[Magic.Length];
        var key = GetKey(version);
        var nonce = payload.AsSpan(headerLength, 12).ToArray();
        var tag = payload.AsSpan(payload.Length - 16, 16).ToArray();
        var cipherLength = payload.Length - headerLength - nonce.Length - tag.Length;
        var cipherBytes = payload.AsSpan(headerLength + nonce.Length, cipherLength).ToArray();
        var plainBytes = new byte[cipherLength];

        using var aes = new AesGcm(key, tag.Length);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes, payload.AsSpan(0, headerLength));
        return StrictUtf8.GetString(plainBytes);
    }

    private bool TryDecryptLegacyGcm(byte[] payload, out string plaintext)
    {
        plaintext = string.Empty;
        if (payload.Length < 1 + 12 + 16 || !_keys.TryGetValue(payload[0], out var key))
            return false;

        var nonce = payload.AsSpan(1, 12).ToArray();
        var tag = payload.AsSpan(payload.Length - 16, 16).ToArray();
        var cipherLength = payload.Length - 1 - nonce.Length - tag.Length;
        var cipherBytes = payload.AsSpan(1 + nonce.Length, cipherLength).ToArray();
        var plainBytes = new byte[cipherLength];

        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
            plaintext = StrictUtf8.GetString(plainBytes);
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

    private string DecryptLegacyCbc(byte[] payload)
    {
        if (payload.Length < 32 || (payload.Length - 16) % 16 != 0)
            throw new CryptographicException("Ciphertext is invalid or corrupted.");

        using var aes = Aes.Create();
        aes.Key = _legacyCbcKey;
        aes.IV = payload.AsSpan(0, 16).ToArray();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        try
        {
            using var decryptor = aes.CreateDecryptor();
            return StrictUtf8.GetString(decryptor.TransformFinalBlock(payload, 16, payload.Length - 16));
        }
        catch (Exception exception) when (exception is CryptographicException or DecoderFallbackException)
        {
            throw new CryptographicException("Ciphertext could not be authenticated or decoded.", exception);
        }
    }

    private static bool HasMagic(byte[] payload)
        => payload.Length >= Magic.Length && payload.AsSpan(0, Magic.Length).SequenceEqual(Magic);

    private byte[] GetKey(byte version)
    {
        if (_keys.TryGetValue(version, out var key))
        {
            return key;
        }

        throw new InvalidOperationException($"No AES key is configured for version {version}.");
    }
}
