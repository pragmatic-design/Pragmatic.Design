using Pragmatic.Composition;

namespace Pragmatic.Composition.Hosting;

/// <summary>
///     Fast startup entry point for Pragmatic applications.
///     Methods are source-generated based on discovered modules.
/// </summary>
/// <remarks>
///     <para>
///         This class provides ultra-minimal Program.cs patterns:
///     </para>
///     <code>
/// await PragmaticApp.RunAsync(args);
///
/// // With module configuration:
/// await PragmaticApp.RunAsync(args, app =>
/// {
///     app.UseMultiTenancy(mt => mt.UseHeader());
///     app.UseAuthentication(auth => auth.UseNoOp());
/// });
///     </code>
///     <para>
///         The source generator creates the implementation based on discovered
///         [PragmaticMetadata] attributes in referenced assemblies.
///     </para>
/// </remarks>
// This class is internal - the public implementation is source-generated.
// This exists only for design-time hints before the generator runs.
internal static class PragmaticApp
{
    /// <summary>
    ///     Runs a web application with auto-discovered Pragmatic modules.
    /// </summary>
    public static Task RunAsync(string[] args, Action<IPragmaticBuilder>? configure = null)
    {
        throw new InvalidOperationException(
            "PragmaticApp.RunAsync requires the Pragmatic source generator. " +
            "Ensure your project is an Exe and references Pragmatic.SourceGenerator as an analyzer.");
    }

    /// <summary>
    ///     Runs a background worker host with auto-discovered Pragmatic modules.
    /// </summary>
    public static Task RunWorkerAsync(string[] args, Action<IPragmaticBuilder>? configure = null)
    {
        throw new InvalidOperationException(
            "PragmaticApp.RunWorkerAsync requires the Pragmatic source generator. " +
            "Ensure your project is an Exe and references Pragmatic.SourceGenerator as an analyzer.");
    }
}
