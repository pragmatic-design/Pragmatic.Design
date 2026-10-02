using Microsoft.Extensions.Configuration;

namespace Pragmatic.Composition.Validation;

/// <summary>
///     Validates that all required configuration keys are present at startup.
/// </summary>
internal static class ConfigurationValidator
{
    /// <summary>
    ///     Validates that all required configuration keys are present.
    /// </summary>
    public static void ValidateRequiredKeys(IConfiguration config, IEnumerable<string> requiredKeys)
    {
        var missing = requiredKeys
            .Where(k => string.IsNullOrEmpty(config[k]))
            .ToList();

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Missing required configuration keys: {string.Join(", ", missing)}. " +
                "Ensure the connection strings are configured in appsettings.json or environment variables.");
    }
}
