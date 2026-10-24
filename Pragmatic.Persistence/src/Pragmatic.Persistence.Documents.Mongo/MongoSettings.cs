using System.Globalization;

namespace Pragmatic.Persistence.Documents.Mongo;

/// <summary>
///     Where the server is and how to speak to it, from a connection string
///     <c>mongodb://[user:password@]host[:port][/database][?authSource=…&amp;tls=true&amp;tlsAllowInvalidCertificates=true&amp;maxPoolSize=…&amp;connectTimeoutMS=…&amp;appName=…]</c>.
///     One host: the driver speaks to a single server (standalone, or one member addressed directly); replica-set discovery and failover are
///     not implemented, and a list of hosts or <c>mongodb+srv://</c> is refused rather than half served.
/// </summary>
public sealed record MongoSettings
{
    public required string Host { get; init; }

    public int Port { get; init; } = 27017;

    public string? User { get; init; }

    public string? Password { get; init; }

    /// <summary>The database the credentials are defined in (<c>authSource</c>); <c>admin</c> when not named.</summary>
    public string AuthSource { get; init; } = "admin";

    /// <summary>The database named in the connection string's path, when there is one.</summary>
    public string? Database { get; init; }

    public bool Tls { get; init; }

    /// <summary>Accepts the server's certificate without validating it: for a test server's self-signed certificate, never in production.</summary>
    public bool AllowInvalidCertificates { get; init; }

    public int MaxPoolSize { get; init; } = 16;

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public string AppName { get; init; } = "Pragmatic.Persistence.Documents";

    public static MongoSettings Parse(string connectionString)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        const string scheme = "mongodb://";
        if (connectionString.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("mongodb+srv:// needs DNS SRV discovery and a replica set: this driver speaks to one host (mongodb://host:port).");
        if (!connectionString.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            throw new FormatException($"A MongoDB connection string starts with {scheme}.");
        var rest = connectionString[scheme.Length..];
        var query = "";
        var questionMark = rest.IndexOf('?', StringComparison.Ordinal);
        if (questionMark >= 0)
        {
            query = rest[(questionMark + 1)..];
            rest = rest[..questionMark];
        }

        string? user = null, password = null;
        var at = rest.LastIndexOf('@');
        if (at >= 0)
        {
            var credentials = rest[..at];
            rest = rest[(at + 1)..];
            var colon = credentials.IndexOf(':', StringComparison.Ordinal);
            user = Uri.UnescapeDataString(colon >= 0 ? credentials[..colon] : credentials);
            password = colon >= 0 ? Uri.UnescapeDataString(credentials[(colon + 1)..]) : null;
        }

        string? database = null;
        var slash = rest.IndexOf('/', StringComparison.Ordinal);
        if (slash >= 0)
        {
            database = rest[(slash + 1)..] is { Length: > 0 } named ? Uri.UnescapeDataString(named) : null;
            rest = rest[..slash];
        }

        if (rest.Contains(',', StringComparison.Ordinal))
            throw new NotSupportedException("A list of hosts is a replica set: this driver speaks to one host (no discovery, no failover).");
        var host = rest;
        var port = 27017;
        var portColon = rest.LastIndexOf(':');
        if (portColon >= 0 && !rest.EndsWith(']'))
        {
            host = rest[..portColon];
            port = int.Parse(rest[(portColon + 1)..], CultureInfo.InvariantCulture);
        }

        var settings = new MongoSettings { Host = host.Trim('[', ']'), Port = port, User = user, Password = password, Database = database };
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            var key = equals >= 0 ? pair[..equals] : pair;
            var value = equals >= 0 ? Uri.UnescapeDataString(pair[(equals + 1)..]) : "";
            settings = key.ToLowerInvariant() switch
            {
                "authsource" => settings with { AuthSource = value },
                "tls" or "ssl" => settings with { Tls = bool.Parse(value) },
                "tlsallowinvalidcertificates" or "tlsinsecure" => settings with { AllowInvalidCertificates = bool.Parse(value) },
                "maxpoolsize" => settings with { MaxPoolSize = int.Parse(value, CultureInfo.InvariantCulture) },
                "connecttimeoutms" => settings with { ConnectTimeout = TimeSpan.FromMilliseconds(int.Parse(value, CultureInfo.InvariantCulture)) },
                "appname" => settings with { AppName = value },
                "directconnection" or "retrywrites" or "w" => settings, // one host, spoken to directly; the defaults hold
                _ => throw new NotSupportedException($"The connection string option '{key}' is not supported by this driver."),
            };
        }

        if (settings.User is not null && settings.Password is null)
            throw new FormatException("The connection string names a user without a password (SCRAM needs both).");
        return settings;
    }
}
