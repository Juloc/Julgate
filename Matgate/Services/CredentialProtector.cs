using System.Security.Cryptography;
using System.Text;

namespace Matgate.Services;

public sealed class CredentialProtector : IDisposable
{
    private const string Prefix = "julgate-aesgcm:v1:";
    private const string LegacyMatgatePrefix = "enc:1:";
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _primaryKey;
    private readonly IReadOnlyList<byte[]> _decryptionKeys;
    private readonly byte[]? _legacyMatgateKey;

    public CredentialProtector(IConfiguration configuration)
        : this(
            SecretValueReader.Read("JULGATE_CREDENTIAL_KEY")
            ?? configuration["Julgate:CredentialKey"]
            ?? "",
            SecretValueReader.Read("JULGATE_CREDENTIAL_KEY_PREVIOUS")
            ?? configuration["Julgate:CredentialKeyPrevious"],
            SecretValueReader.Read("JULGATE_LEGACY_MATGATE_SECRET_KEY", "MATGATE_SECRET_KEY")
            ?? configuration["Julgate:LegacyMatgateSecretKey"]
            ?? configuration["Matgate:SecretKey"])
    {
    }

    public CredentialProtector(
        string base64Key,
        string? previousBase64Keys = null,
        string? legacyMatgateSecretKey = null)
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
        _legacyMatgateKey = string.IsNullOrWhiteSpace(legacyMatgateSecretKey)
            ? null
            : SHA256.HashData(Encoding.UTF8.GetBytes(legacyMatgateSecretKey.Trim()));
    }

    public string Protect(string? value)
    {
        if (string.IsNullOrEmpty(value) || IsProtected(value))
        {
            return value ?? "";
        }

        // Matgate 0.6.1 used a different self-describing AES-GCM format. Decrypt it
        // before writing so the next store update migrates it to Julgate's current format.
        if (IsLegacyMatgateProtected(value))
        {
            value = UnprotectLegacyMatgate(value);
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
        if (string.IsNullOrEmpty(value))
        {
            return value ?? "";
        }

        if (IsLegacyMatgateProtected(value))
        {
            return UnprotectLegacyMatgate(value);
        }

        if (!IsProtected(value))
        {
            return value;
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
                var unprotected = Encoding.UTF8.GetString(plaintext);

                // Julgate 0.6.x could have wrapped an already-encrypted Matgate enc:1 value
                // before legacy support existed. Never forward that nested ciphertext as the
                // remote password; unwrap both layers and migrate it on the next store write.
                return IsLegacyMatgateProtected(unprotected)
                    ? UnprotectLegacyMatgate(unprotected)
                    : unprotected;
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

    internal static bool IsLegacyMatgateProtected(string? value)
    {
        return value?.StartsWith(LegacyMatgatePrefix, StringComparison.Ordinal) == true;
    }

    public void Dispose()
    {
        foreach (var key in _decryptionKeys)
        {
            CryptographicOperations.ZeroMemory(key);
        }

        if (_legacyMatgateKey is not null)
        {
            CryptographicOperations.ZeroMemory(_legacyMatgateKey);
        }
    }

    private string UnprotectLegacyMatgate(string value)
    {
        if (_legacyMatgateKey is null)
        {
            throw new InvalidOperationException(
                "A stored Matgate credential uses the legacy enc:1 format. Set "
                + "JULGATE_LEGACY_MATGATE_SECRET_KEY (or MATGATE_SECRET_KEY) to the original "
                + "Matgate secret, then restart Julgate so the credential can be migrated.");
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(value[LegacyMatgatePrefix.Length..]);
        }
        catch (FormatException exception)
        {
            throw LegacyDecryptionFailure(exception);
        }

        if (payload.Length < NonceSize + TagSize)
        {
            throw LegacyDecryptionFailure(new CryptographicException("Legacy credential payload is too short."));
        }

        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var ciphertext = payload.AsSpan(NonceSize + TagSize);
        var plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(_legacyMatgateKey, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException exception)
        {
            throw LegacyDecryptionFailure(exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
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

    private static InvalidOperationException LegacyDecryptionFailure(Exception exception)
    {
        return new InvalidOperationException(
            "A stored Matgate enc:1 credential cannot be decrypted. Restore the original MATGATE_SECRET_KEY.",
            exception);
    }
}
