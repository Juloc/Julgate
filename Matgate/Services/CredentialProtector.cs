using System.Security.Cryptography;
using System.Text;

namespace Matgate.Services;

public sealed class CredentialProtector : IDisposable
{
    private const string Prefix = "julgate-aesgcm:v1:";
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _primaryKey;
    private readonly IReadOnlyList<byte[]> _decryptionKeys;

    public CredentialProtector(IConfiguration configuration)
        : this(
            SecretValueReader.Read("JULGATE_CREDENTIAL_KEY")
            ?? configuration["Julgate:CredentialKey"]
            ?? "",
            SecretValueReader.Read("JULGATE_CREDENTIAL_KEY_PREVIOUS")
            ?? configuration["Julgate:CredentialKeyPrevious"])
    {
    }

    public CredentialProtector(string base64Key, string? previousBase64Keys = null)
    {
        _primaryKey = DecodeKey(base64Key, "JULGATE_CREDENTIAL_KEY");
        var keys = new List<byte[]> { _primaryKey };

        foreach (var previous in (previousBase64Keys ?? "")
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var key = DecodeKey(previous, "JULGATE_CREDENTIAL_KEY_PREVIOUS");
            if (!keys.Any(existing => CryptographicOperations.FixedTimeEquals(existing, key)))
            {
                keys.Add(key);
            }
            else
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        _decryptionKeys = keys;
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
            using var aes = new AesGcm(_primaryKey, TagSize);
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

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(value[Prefix.Length..]);
        }
        catch (FormatException exception)
        {
            throw DecryptionFailure(exception);
        }

        if (payload.Length < NonceSize + TagSize)
        {
            throw DecryptionFailure(new CryptographicException("Credential payload is too short."));
        }

        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var ciphertext = payload.AsSpan(NonceSize + TagSize);

        Exception? lastFailure = null;
        foreach (var key in _decryptionKeys)
        {
            var plaintext = new byte[ciphertext.Length];
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
                return Encoding.UTF8.GetString(plaintext);
            }
            catch (CryptographicException exception)
            {
                lastFailure = exception;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        throw DecryptionFailure(lastFailure ?? new CryptographicException("No credential key matched."));
    }

    public bool IsProtected(string? value)
    {
        return value?.StartsWith(Prefix, StringComparison.Ordinal) == true;
    }

    public void Dispose()
    {
        foreach (var key in _decryptionKeys)
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] DecodeKey(string base64Key, string settingName)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64Key);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                $"{settingName} or {settingName}_FILE must provide a Base64-encoded 32-byte random key.",
                exception);
        }

        if (key.Length != KeySize)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidOperationException(
                $"{settingName} must decode to exactly 32 bytes. Generate it with: openssl rand -base64 32");
        }

        return key;
    }

    private static InvalidOperationException DecryptionFailure(Exception exception)
    {
        return new InvalidOperationException(
            "A stored Julgate credential cannot be decrypted. Restore the matching primary or previous credential key.",
            exception);
    }
}
