using System.Text;
using Matgate.Models;
using Matgate.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Matgate.Tests;

public sealed class FileProtocolIntegrationTests
{
    [Theory]
    [InlineData(ServerProtocol.Sftp, 2222, "/upload")]
    [InlineData(ServerProtocol.Ftp, 2121, "/")]
    [InlineData(ServerProtocol.Smb, 445, "public")]
    public async Task FileProtocol_RoundTripsAFile(ServerProtocol protocol, int port, string root)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("JULGATE_RUN_FILE_INTEGRATION"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var configuration = new ConfigurationManager();
        configuration["Julgate:FILE_OPERATION_TIMEOUT_SECONDS"] = "45";
        configuration["Julgate:MAX_UPLOAD_BYTES"] = "1048576";
        configuration["Julgate:MAX_DOWNLOAD_BYTES"] = "1048576";
        configuration["Julgate:MAX_DIRECTORY_ENTRIES"] = "100";
        var files = new FileGatewaySecurityDecorator(new FileGatewayService(), configuration);
        var server = new ServerEndpoint
        {
            Id = Guid.NewGuid(),
            Name = $"Integration {protocol}",
            Protocol = protocol,
            Host = "127.0.0.1",
            Port = port,
            UserName = "test",
            Password = "password",
            FileRootPath = root,
            IsEnabled = true
        };
        var fileName = $"julgate-{protocol.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}.txt";
        const string content = "Julgate file gateway integration test";

        try
        {
            await using var upload = new MemoryStream(Encoding.UTF8.GetBytes(content));
            await files.UploadAsync(server, "/", upload, fileName);

            var listing = await files.ListAsync(server, "/");
            Assert.Contains(listing.Entries, entry =>
                !entry.IsDirectory && string.Equals(entry.Name, fileName, StringComparison.OrdinalIgnoreCase));

            var download = await files.DownloadAsync(server, "/" + fileName);
            await using (download.Content)
            using (var reader = new StreamReader(download.Content, Encoding.UTF8))
            {
                Assert.Equal(content, await reader.ReadToEndAsync());
            }
        }
        finally
        {
            try
            {
                await files.DeleteAsync(server, "/" + fileName);
            }
            catch
            {
                // The assertion above provides the primary failure. Cleanup is best effort.
            }
        }
    }
}
