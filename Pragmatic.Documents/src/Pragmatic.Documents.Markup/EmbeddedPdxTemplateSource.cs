using System.Reflection;
using System.Text;

namespace Pragmatic.Documents.Markup;

/// <summary>
///     Templates shipped inside an assembly as embedded resources — the way a module ships its own.
/// </summary>
/// <remarks>
///     <para>
///         In the module's <c>.csproj</c>:
///         <c>&lt;EmbeddedResource Include="templates\**\*.pdxdoc;templates\**\*.pdxemail" /&gt;</c>.
///         Embedded rather than copied beside the host: two modules that both copy
///         <c>templates/invoice.pdxdoc</c> land on the same path in the host's output and one silently
///         replaces the other; two assemblies cannot collide.
///     </para>
///     <para>
///         A name is the file name, optionally with the folders under the one the resources are rooted
///         at — <c>"reminder.pdxemail"</c> or <c>"mail/reminder.pdxemail"</c>. ⚠️ A bare name two folders
///         share is refused rather than answered with whichever the manifest lists first: the folder
///         settles it.
///     </para>
/// </remarks>
public sealed class EmbeddedPdxTemplateSource : IPdxTemplateSource
{
    private readonly Assembly _assembly;
    private readonly string[] _resources;

    /// <param name="assembly">The assembly the templates are embedded in.</param>
    public EmbeddedPdxTemplateSource(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        _assembly = assembly;
        _resources = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".pdxdoc", StringComparison.OrdinalIgnoreCase)
                           || name.EndsWith(".pdxemail", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<string?> FindAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var suffix = "." + ManifestNameOf(name);
        var matches = _resources
            .Where(resource => resource.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
            return null;

        if (matches.Length > 1)
            throw new InvalidOperationException(
                $"'{name}' names {matches.Length} templates in {_assembly.GetName().Name}: "
                + $"{string.Join(", ", matches)}. Name it with its folder.");

        await using var stream = _assembly.GetManifestResourceStream(matches[0])!;
        using var reader = new StreamReader(stream, Encoding.UTF8);

        return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override string ToString() => $"{nameof(EmbeddedPdxTemplateSource)}({_assembly.GetName().Name})";

    /// <summary>
    ///     The name as the compiler writes it into the manifest: folders joined by dots, with the
    ///     characters an identifier cannot hold turned into underscores; the file name as it is.
    /// </summary>
    private static string ManifestNameOf(string name)
    {
        var segments = name.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var folders = segments[..^1].Select(IdentifierOf);

        return string.Join('.', [.. folders, segments[^1]]);
    }

    private static string IdentifierOf(string folder)
        => string.Concat(folder.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));
}
