using System.Security.Cryptography;
using Matgate.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Matgate.Tests;

public sealed class GuacamoleSecurityTests
{
    [Fact]
    public async Task Synchronize_RemovesLegacyPlaintextFileAuthentication()
    {
        var root = Path.Combine(Path.GetTempPath(), $"julgate-guacamole-{Guid.NewGuid():N}");
        var legacyDirectory = Path.Combine(root, "guacamole");
        Directory.CreateDirectory(legacyDirectory);

        try
        {
            var legacyFiles = new[]
            {
                Path.Combine(root, "user-mapping.xml"),
                Path.Combine(root, "guacamole.properties"),
                Path.Combine(legacyDirectory, "user-mapping.xml"),
                Path.Combine(legacyDirectory, "guacamole.properties")
            };

            foreach (var path in legacyFiles)
            {
                await File.WriteAllTextAsync(path, "legacy-plaintext-secret");
            }

            var configuration = new ConfigurationManager();
            configuration["Matgate:DataDirectory"] = root;
            configuration["Matgate:WorkspaceRoot"] = Path.Combine(root, "workspaces");
            var environment = new TestHostEnvironment(root);
            using var protector = new CredentialProtector(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            var store = new JsonDataStore(
                configuration,
                environment,
                NullLogger<JsonDataStore>.Instance,
                protector);
            var writer = new GuacamoleConfigWriter(
                store,
                configuration,
                NullLogger<GuacamoleConfigWriter>.Instance);

            await writer.SynchronizeAsync();

            Assert.All(legacyFiles, path => Assert.False(File.Exists(path)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
