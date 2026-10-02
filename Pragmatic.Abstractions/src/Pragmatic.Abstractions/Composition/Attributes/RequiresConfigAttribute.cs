namespace Pragmatic.Composition.Attributes;

/// <summary>
///     Declares that a startup step requires a specific configuration section to be present at startup.
///     The host generator reads these declarations and emits a <c>ValidateConfiguration()</c> method
///     that performs fail-fast validation before the application starts.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RequiresConfigAttribute : Attribute
{
    /// <param name="sectionPath">
    ///     The configuration section path that must exist (e.g., "ConnectionStrings:MyDb").
    /// </param>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="sectionPath"/> is null, empty, or whitespace. An empty
    ///     section path would otherwise pass attribute construction and fail confusingly during
    ///     startup validation rather than at the declaration site.
    /// </exception>
    public RequiresConfigAttribute(string sectionPath)
    {
        if (string.IsNullOrWhiteSpace(sectionPath))
            throw new ArgumentException(
                "[RequiresConfig] section path must not be null, empty, or whitespace.",
                nameof(sectionPath));

        SectionPath = sectionPath;
    }

    /// <summary>Gets the configuration section path that must exist (e.g., "ConnectionStrings:MyDb").</summary>
    public string SectionPath { get; }

    /// <summary>Gets or sets an optional human-readable description of why this section is required.</summary>
    public string? Description { get; set; }
}
