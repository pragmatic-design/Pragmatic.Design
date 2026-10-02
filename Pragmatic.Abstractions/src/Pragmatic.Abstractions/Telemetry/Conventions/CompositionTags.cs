namespace Pragmatic.Telemetry.Conventions;

/// <summary>
///     Tag names for assembly scanning and module composition.
/// </summary>
public static class CompositionTags
{
    /// <summary>The glob the scanner matched assemblies against.</summary>
    public const string Pattern = "pragmatic.composition.pattern";

    /// <summary>Number of files the pattern matched.</summary>
    public const string MatchedFiles = "pragmatic.composition.matched_files";

    /// <summary>Number of assemblies actually loaded.</summary>
    public const string LoadedAssemblies = "pragmatic.composition.loaded_assemblies";

    /// <summary>Number of libraries considered.</summary>
    public const string LibraryCount = "pragmatic.composition.library_count";
}
