using System.Runtime.CompilerServices;

namespace Matgate.Services;

public static class SecretFileBootstrap
{
    [ModuleInitializer]
    public static void Initialize()
    {
        Promote("JULGATE_ADMIN_PASSWORD");
        Promote("JULGATE_CREDENTIAL_KEY");
        Promote("JULGATE_GUACAMOLE_JSON_SECRET_KEY");
        Promote("MATGATE_ADMIN_PASSWORD");
        Promote("MATGATE_GUACAMOLE_JSON_SECRET_KEY");
    }

    private static void Promote(string name)
    {
        var filePath = Environment.GetEnvironmentVariable($"{name}_FILE");
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var value = SecretValueReader.Read(name);
        Environment.SetEnvironmentVariable(name, value);
    }
}
