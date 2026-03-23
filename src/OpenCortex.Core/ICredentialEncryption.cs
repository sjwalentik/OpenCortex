namespace OpenCortex.Core;

/// <summary>
/// Service for encrypting/decrypting user credentials.
/// </summary>
public interface ICredentialEncryption
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}
