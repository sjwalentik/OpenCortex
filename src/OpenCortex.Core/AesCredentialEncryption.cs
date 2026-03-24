using System.Security.Cryptography;
using System.Text;

namespace OpenCortex.Core;

/// <summary>
/// AES-256-GCM encryption for user credentials.
/// New values are encrypted with AES-256-GCM (authenticated encryption).
/// Values encrypted with the legacy AES-256-CBC scheme are still decryptable.
/// </summary>
public sealed class AesCredentialEncryption : ICredentialEncryption
{
    // Prefix written before new GCM ciphertexts to distinguish them from legacy CBC values.
    private const string GcmPrefix = "g:";
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    private readonly byte[] _key;

    public AesCredentialEncryption(string encryptionKey)
    {
        if (string.IsNullOrEmpty(encryptionKey))
        {
            throw new ArgumentException("Encryption key is required. Set OpenCortex:Security:EncryptionKey in user secrets.");
        }

        // Derive a 256-bit key from the provided key using SHA256.
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(encryptionKey));
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return string.Empty;
        }

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);

        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSizeBytes];

        using var aesGcm = new AesGcm(_key, TagSizeBytes);
        aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);

        // Layout: nonce(12) + ciphertext(n) + tag(16)
        var result = new byte[NonceSizeBytes + cipherBytes.Length + TagSizeBytes];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSizeBytes);
        Buffer.BlockCopy(cipherBytes, 0, result, NonceSizeBytes, cipherBytes.Length);
        Buffer.BlockCopy(tag, 0, result, NonceSizeBytes + cipherBytes.Length, TagSizeBytes);

        return GcmPrefix + Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return string.Empty;
        }

        return cipherText.StartsWith(GcmPrefix, StringComparison.Ordinal)
            ? DecryptGcm(cipherText[GcmPrefix.Length..])
            : DecryptLegacyCbc(cipherText);
    }

    private string DecryptGcm(string base64Payload)
    {
        var fullBytes = Convert.FromBase64String(base64Payload);

        var nonce = fullBytes[..NonceSizeBytes];
        var tag = fullBytes[^TagSizeBytes..];
        var cipherBytes = fullBytes[NonceSizeBytes..^TagSizeBytes];
        var plainBytes = new byte[cipherBytes.Length];

        using var aesGcm = new AesGcm(_key, TagSizeBytes);
        aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>
    /// Decrypts values encrypted with the legacy AES-256-CBC scheme (no prefix).
    /// Retained for backward compatibility with credentials stored before the GCM migration.
    /// </summary>
    private string DecryptLegacyCbc(string cipherText)
    {
        var fullCipher = Convert.FromBase64String(cipherText);

        using var aes = Aes.Create();
        aes.Key = _key;

        var iv = new byte[aes.BlockSize / 8];
        var cipher = new byte[fullCipher.Length - iv.Length];

        Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
        Buffer.BlockCopy(fullCipher, iv.Length, cipher, 0, cipher.Length);

        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
