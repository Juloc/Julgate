namespace Matgate.Services;

public static class SecretValueReader
{
    public static string? Read(params string[] names)
    {
        foreach (var name in names)
        {
            var filePath = Environment.GetEnvironmentVariable($"{name}_FILE")?.Trim();
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                return ReadFile(name, filePath);
            }

            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string ReadFile(string name, string filePath)
    {
        try
        {
            var value = File.ReadAllText(filePath).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"The secret file configured by {name}_FILE is empty.");
            }

            return value;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"The secret file configured by {name}_FILE could not be read.",
                exception);
        }
    }
}
