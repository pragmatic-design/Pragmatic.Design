using System.Runtime.InteropServices;

namespace Pragmatic.Logging.Context.Providers;

/// <summary>
/// Provides machine-level context information.
/// </summary>
public sealed class MachineContextProvider() : ContextProviderBase("Machine", priority: 1000)
{
    private static readonly Lazy<IReadOnlyDictionary<string, object?>> _cachedProperties =
        new(CreateMachineProperties);

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object?> GetContextProperties()
    {
        return _cachedProperties.Value;
    }

    private static IReadOnlyDictionary<string, object?> CreateMachineProperties()
    {
        return CreatePropertiesDictionary(
            ("MachineName", Environment.MachineName),
            ("UserName", Environment.UserName),
            ("OSVersion", Environment.OSVersion.ToString()),
            ("ProcessorCount", Environment.ProcessorCount),
            ("Is64BitOperatingSystem", Environment.Is64BitOperatingSystem),
            ("Is64BitProcess", Environment.Is64BitProcess),
            ("CLRVersion", Environment.Version.ToString()),
            ("RuntimeIdentifier", RuntimeInformation.RuntimeIdentifier),
            ("OSDescription", RuntimeInformation.OSDescription),
            ("FrameworkDescription", RuntimeInformation.FrameworkDescription)
        );
    }
}