using Microsoft.Extensions.Configuration;

namespace Pragmatic.Composition.Hosting;

/// <summary>
///     The check the generated entry runs, before any database is touched, for every connection string
///     a <c>[PragmaticDatabase]</c> declares.
/// </summary>
/// <remarks>
///     The key is known at compile time. Read as <c>configuration[key] ?? ""</c>, an absent value would
///     become an empty one and fail wherever it was first used — leader election, then migration
///     polling, then the schema check — each saying something other than "this key is missing".
///     Failing here, by name, is the one place the cause and the message are the same.
/// </remarks>
public static class DeclaredConnectionStrings
{
    /// <summary>Throws when <paramref name="key" /> has no value, naming it and where it was looked for.</summary>
    /// <param name="configuration">The host's final configuration.</param>
    /// <param name="key">The key declared on the database, e.g. <c>ConnectionStrings:App</c>.</param>
    /// <param name="database">The database type that declared it.</param>
    /// <param name="contentRoot">The content root the <c>appsettings*.json</c> files were read from.</param>
    /// <exception cref="InvalidOperationException">The key is absent, empty or whitespace.</exception>
    public static void Require(IConfiguration configuration, string key, string database, string contentRoot)
    {
        if (!string.IsNullOrWhiteSpace(configuration[key]))
            return;

        throw new InvalidOperationException(
            $"The connection string '{key}' declared by {database} is not configured. Configuration was read "
            + $"from the appsettings files in '{contentRoot}', the environment and the command line; set it in "
            + $"one of them (as an environment variable: '{key.Replace(":", "__", StringComparison.Ordinal)}').");
    }
}
