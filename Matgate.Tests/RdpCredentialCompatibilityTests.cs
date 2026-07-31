using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Matgate.Models;
using Matgate.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Matgate.Tests;

public sealed class RdpCredentialCompatibilityTests
{
    [Fact]
    public void LegacyMatgateCredential_IsDecryptedAndMigratedToCurrentFormat()
    {
        const string legacySecret = "legacy-matgate-secret-from-env";
        const string password = "Rdp-Passwort! 42 äöü &?=";
        var legacyValue = ProtectLegacyMatgate(password, legacySecret);

        using var protector = new CredentialProtector(
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            legacyMatgateSecretKey: legacySecret);

        Assert.True(CredentialProtector.IsLegacyMatgateProtected(legacyValue));
        Assert.Equal(password, protector.Unprotect(legacyValue));

        var migrated = protector.Protect(legacyValue);
        Assert.StartsWith("julgate-aesgcm:v1:", migrated, StringComparison.Ordinal);
        Assert.Equal(password, protector.Unprotect(migrated));
    }

    [Fact]
    public void LegacyMatgateCredential_WrappedByOlderJulgate_IsFullyUnwrapped()
    {
        const string legacySecret = "legacy-matgate-secret-from-env";
        const string password = "nested-RDP-password!";
        var currentKey = RandomNumberGenerator.GetBytes(32);
        var legacyValue = ProtectLegacyMatgate(password, legacySecret);
        var nestedValue = ProtectCurrentEnvelope(legacyValue, currentKey);

        using var protector = new CredentialProtector(
            Convert.ToBase64String(currentKey),
            legacyMatgateSecretKey: legacySecret);

        Assert.Equal(password, protector.Unprotect(nestedValue));
    }

    [Fact]
    public void LegacyMatgateCredential_WithoutOriginalKeyFailsClosed()
    {
        var legacyValue = ProtectLegacyMatgate("secret", "original-matgate-key");
        using var protector = new CredentialProtector(
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

        var error = Assert.Throws<InvalidOperationException>(() => protector.Unprotect(legacyValue));

        Assert.Contains("JULGATE_LEGACY_MATGATE_SECRET_KEY", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoredRdpPassword_ReachesGuacamoleLaunchPayloadUnchanged()
    {
        var root = Path.Combine(Path.GetTempPath(), $"julgate-rdp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            const string jsonSecret = "00112233445566778899aabbccddeeff";
            const string expectedPassword = "P@ss wörd?&=\\'\" 42";
            var configuration = new ConfigurationManager();
            configuration["Matgate:DataDirectory"] = root;
            configuration["Matgate:WorkspaceRoot"] = Path.Combine(root, "workspaces");
            configuration["Guacamole:JsonSecretKey"] = jsonSecret;
            configuration["Guacamole:PublicBasePath"] = "/guacamole";
            configuration["Guacamole:DirectLaunch"] = "true";

            using var protector = new CredentialProtector(
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            var store = new JsonDataStore(
                configuration,
                new TestHostEnvironment(root),
                NullLogger<JsonDataStore>.Instance,
                protector);

            var server = new ServerEndpoint
            {
                Name = "RDP regression",
                Protocol = ServerProtocol.Rdp,
                Host = "192.168.1.218",
                Port = 3389,
                UserName = "WIN11-VM\\Julian",
                Password = expectedPassword,
                IgnoreCertificate = true,
                IsEnabled = true
            };
            await store.UpdateServersAsync(servers => servers.Add(server));

            var storedRaw = await File.ReadAllTextAsync(Path.Combine(root, "servers.json"));
            Assert.DoesNotContain(expectedPassword, storedRaw, StringComparison.Ordinal);

            var reloaded = Assert.Single(await store.GetServersAsync());
            Assert.Equal(expectedPassword, reloaded.Password);

            var launcher = new GuacamoleLauncher(configuration);
            var result = await launcher.CreateLaunchAsync(
                new MatgateUser { UserName = "admin", IsAdmin = true, IsEnabled = true },
                reloaded);

            Assert.True(result.Success, result.Error);
            Assert.NotNull(result.EncryptedData);
            var parameters = DecryptLaunchParameters(result.EncryptedData!, jsonSecret);
            Assert.Equal(expectedPassword, parameters.GetProperty("password").GetString());
            Assert.Equal("WIN11-VM\\Julian", parameters.GetProperty("username").GetString());
            Assert.Equal("any", parameters.GetProperty("security").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string ProtectLegacyMatgate(string plaintext, string legacySecret)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(legacySecret.Trim()));
        try
        {
            return ProtectAesGcmEnvelope("enc:1:", plaintext, key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static string ProtectCurrentEnvelope(string plaintext, byte[] key)
    {
        return ProtectAesGcmEnvelope("julgate-aesgcm:v1:", plaintext, key);
    }

    private static string ProtectAesGcmEnvelope(string prefix, string plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var data = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[data.Length];

        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, data, ciphertext, tag);
            var payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
            nonce.CopyTo(payload, 0);
            tag.CopyTo(payload, nonce.Length);
            ciphertext.CopyTo(payload, nonce.Length + tag.Length);
            return prefix + Convert.ToBase64String(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(data);
        }
    }

    private static JsonElement DecryptLaunchParameters(string encryptedData, string jsonSecret)
    {
        var encrypted = Convert.FromBase64String(encryptedData);
        var key = Convert.FromHexString(jsonSecret);
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = new byte[16];
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var decryptor = aes.CreateDecryptor();
        var signedPayload = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
        var json = Encoding.UTF8.GetString(signedPayload, 32, signedPayload.Length - 32);
        using var document = JsonDocument.Parse(json);
        var connection = document.RootElement
            .GetProperty("connections")
            .EnumerateObject()
            .Single()
            .Value;
        return connection.GetProperty("parameters").Clone();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            ContentRootFileProvider = new PhysicalFileProvider(contentRootPath);
        }

        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "Matgate.Tests";

        public string ContentRootPath { get; set; }

        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
