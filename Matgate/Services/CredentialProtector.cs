using System.Security.Cryptography;
using System.Text;

namespace Matgate.Services;

public sealed class CredentialProtector : IDisposable
{
    private const string Prefix = "julgate-aesgcm:v1:";
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public CredentialProtector(IConfiguration configuration)
        : this(
            SecretValueReader.Read("JULGATE_CREDENTIAL_KEY")
            ?? configuration["Julgate:CredentialKey"]
            ?? "")
    {
    }

    public CredentialProtector(string base64Key)
    {
        try
        {
            _key = Convert.FromBase64String(base64Key);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "JULGATE_CREDENTIAL_KEY or JULGATE_CREDENTIAL_KEY_FILE must provide a Base64-encoded 32-byte random key.",
                exception);
        }

        if (_key.Length != KeySize)
        {
            CryptographicOperations.ZeroMemory(_key);
            throw new InvalidOperationException(
                "The Julgate credential key must decode to exactly 32 bytes. Generate it with: openssl rand -base64 32");
        }
    }

    public string Protect(string? value)
    {
        if (string.IsNullOrEmpty(value) || IsProtected(value))
        {
            return value ?? "";
        }

        var plaintext = Encoding.UTF8.GetBytes(value);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

            var payload = new byte[NonceSize + TagSize + ciphertext.Length];
            nonce.CopyTo(payload, 0);
            tag.CopyTo(payload, NonceSize);
            ciphertext.CopyTo(payload, NonceSize + TagSize);
            return Prefix + Convert.ToBase64String(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public string Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value) || !IsProtected(value))
        {
            return value ?? "";
        }

        try
        {
            var payload = Convert.FromBase64String(value[Prefix.Length..]);
            if (payload.Length < NonceSize + TagSize)
            {
                throw new CryptographicException("Credential payload is too short.");
            }

            var nonce = payload.AsSpan(0, NonceSize);
            var tag = payload.AsSpan(NonceSize, TagSize);
            var ciphertext = payload.AsSpan(NonceSize + TagSize);
            var plaintext = new byte[ciphertext.Length];

            try
            {
                using var aes = new AesGcm(_key, TagSize);
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
                return Encoding.UTF8.GetString(plaintext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch (FormatException exception)
        {
            throw DecryptionFailure(exception);
        }
        catch (CryptographicException exception)
        {
            throw DecryptionFailure(exception);
        }
    }

    public bool IsProtected(string? value)
    {
        return value?.StartsWith(Prefix, StringComparison.Ordinal) == true;
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_key);
    }

    private static InvalidOperationException DecryptionFailure(Exception exception)
    {
        return new InvalidOperationException(
            "A stored Julgate credential cannot be decrypted. Restore the matching credential key.",
            exception);
    }
}
