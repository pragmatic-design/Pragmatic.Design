using Microsoft.Extensions.Configuration;

namespace Pragmatic.Migrations.Cli.Discovery;

/// <summary>
///     Resolves connection strings from appsettings.json, environment variables,
///     or CLI override. Convention: ConnectionStrings:{DatabaseName}.
/// </summary>
public sealed class ConnectionStringResolver
{
    private readonly IConfiguration? _configuration;
    private readonly string? _overrideConnectionString;

    public ConnectionStringResolver(string? configPath, string? overrideConnectionString)
    {
        _overrideConnectionString = overrideConnectionString;

        if (configPath is not null || overrideConnectionString is null)
        {
            var builder = new ConfigurationBuilder();

            // Try auto-discover appsettings.json
            var effectivePath = configPath ?? FindAppSettings();
            var configDir = effectivePath is not null ? Path.GetDirectoryName(effectivePath) : null;

            // Guard on the directory existing: SetBasePath builds a PhysicalFileProvider which THROWS
            // DirectoryNotFoundException on a missing root (unlike AddJsonFile's optional:true, which only
            // tolerates a missing file). A non-existent config path must degrade to "no file config", not throw.
            if (effectivePath is not null && !string.IsNullOrEmpty(configDir) && Directory.Exists(configDir))
            {
                builder.SetBasePath(configDir);
                builder.AddJsonFile(Path.GetFileName(effectivePath), optional: true);

                // Also load environment-specific overrides
                var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
                var envFile = Path.Combine(configDir, $"appsettings.{env}.json");
                if (File.Exists(envFile))
                    builder.AddJsonFile(Path.GetFileName(envFile), optional: true);
            }

            builder.AddEnvironmentVariables();
            _configuration = builder.Build();
        }
    }

    /// <summary>
    ///     Resolves the connection string for a database.
    ///     Priority: --connection override > ConfigKey from SchemaVersion > convention matching > Default
    /// </summary>
    public string? Resolve(string? databaseName, string? configKey = null)
    {
        if (_overrideConnectionString is not null)
            return _overrideConnectionString;

        if (_configuration is null)
            return null;

        // Try ConfigKey from SchemaVersion (set by SG from [PragmaticDatabase(ConfigKey = "...")])
        if (!string.IsNullOrEmpty(configKey))
        {
            var cs = _configuration[configKey!];
            if (!string.IsNullOrEmpty(cs))
                return cs;
        }

        if (string.IsNullOrEmpty(databaseName))
            return null;

        // Try exact match: ConnectionStrings:{DatabaseName}
        var byName = _configuration[$"ConnectionStrings:{databaseName}"];
        if (!string.IsNullOrEmpty(byName))
            return byName;

        // Try without "Database" suffix: ConnectionStrings:App for ShowcaseAppDatabase
        var shortName = databaseName.Replace("Database", "", StringComparison.OrdinalIgnoreCase);
        byName = _configuration[$"ConnectionStrings:{shortName}"];
        if (!string.IsNullOrEmpty(byName))
            return byName;

        // Fallback: ConnectionStrings:Default
        return _configuration["ConnectionStrings:Default"];
    }

    public static string? ConfigFilePath => FindAppSettings();

    private static string? FindAppSettings()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "appsettings.Development.json")
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
