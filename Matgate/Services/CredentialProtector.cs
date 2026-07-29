using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Matgate.Services;

public sealed class CredentialProtector
{
    private const string Prefix = "julgate-protected:v1:";
    private readonly IDataProtector _protector;

    public CredentialProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Julgate.StoredCredentials.v1");
    }

    public string Protect(string? value)
    {
        if (string.IsNullOrEmpty(value) || IsProtected(value))
        {
            return value ?? "";
        }

        return Prefix + _protector.Protect(value);
    }

    public string Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value) || !IsProtected(value))
        {
            return value ?? "";
        }

        try
        {
            return _protector.Unprotect(value[Prefix.Length..]);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException(
                "A stored Julgate credential cannot be decrypted. Restore the matching data-protection keys from backup.",
                exception);
        }
    }

    public bool IsProtected(string? value)
    {
        return value?.StartsWith(Prefix, StringComparison.Ordinal) == true;
    }
}
