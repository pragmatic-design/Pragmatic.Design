using Microsoft.Extensions.Configuration;

namespace Pragmatic.Configuration.Discovery;

/// <summary>
///     Checks an <see cref="IConfiguration" /> against what the application declares it needs, without
///     starting the application.
/// </summary>
/// <remarks>
///     <para>
///         Distinct from <c>ValidateOnStart</c>, which is the same question asked at the worst moment:
///         it answers during host start, in the environment being started, after whatever the process
///         had already begun to do. This answers it from a test, a build step or a command, against the
///         <c>appsettings</c> of an environment nobody has deployed to yet.
///     </para>
///     <para>
///         It reports what is <b>missing</b>, not what is wrong: a value's shape is the bound POCO's
///         business, and re-deciding it here would be a second validator disagreeing with the first.
///     </para>
/// </remarks>
public static class ConfigurationPreflight
{
    /// <summary>
    ///     Every required key the configuration does not supply, as full configuration keys.
    ///     An empty list means the configuration satisfies everything the catalogue declares.
    /// </summary>
    public static IReadOnlyList<string> MissingRequiredKeys(
        IConfiguration configuration, IConfigurationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(catalog);

        var missing = new List<string>();

        foreach (var section in catalog.Sections)
        {
            foreach (var property in section.Properties)
            {
                if (!property.IsRequired)
                    continue;

                var key = section.KeyOf(property);

                // GetSection never returns null, so presence is "has a value, or has children" — a
                // section supplied as an object counts as supplied. Checking for null would report
                // every key as present.
                var entry = configuration.GetSection(key);
                if (string.IsNullOrEmpty(entry.Value) && !entry.GetChildren().Any())
                    missing.Add(key);
            }
        }

        return missing;
    }

    /// <summary>
    ///     Throws when anything required is missing, naming every key at once.
    /// </summary>
    /// <remarks>
    ///     All of them, not the first: fixing a deployment one missing key per run is the failure mode
    ///     that makes people stop running the check.
    /// </remarks>
    public static void ThrowIfIncomplete(IConfiguration configuration, IConfigurationCatalog catalog)
    {
        var missing = MissingRequiredKeys(configuration, catalog);
        if (missing.Count == 0)
            return;

        throw new InvalidOperationException(
            "Configuration is missing required keys: " + string.Join(", ", missing) + ".");
    }
}
