using Matgate.Services;
using Xunit;

namespace Matgate.Tests;

public sealed class SecretValueReaderTests
{
    [Fact]
    public void FileSecret_TakesPrecedenceOverEnvironmentValue()
    {
        var name = $"JULGATE_TEST_SECRET_{Guid.NewGuid():N}";
        var path = Path.GetTempFileName();

        try
        {
            File.WriteAllText(path, "file-secret\n");
            Environment.SetEnvironmentVariable(name, "environment-secret");
            Environment.SetEnvironmentVariable($"{name}_FILE", path);

            Assert.Equal("file-secret", SecretValueReader.Read(name));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
            Environment.SetEnvironmentVariable($"{name}_FILE", null);
            File.Delete(path);
        }
    }

    [Fact]
    public void EmptySecretFile_IsRejected()
    {
        var name = $"JULGATE_TEST_SECRET_{Guid.NewGuid():N}";
        var path = Path.GetTempFileName();

        try
        {
            Environment.SetEnvironmentVariable($"{name}_FILE", path);

            Assert.Throws<InvalidOperationException>(() => SecretValueReader.Read(name));
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{name}_FILE", null);
            File.Delete(path);
        }
    }
}
