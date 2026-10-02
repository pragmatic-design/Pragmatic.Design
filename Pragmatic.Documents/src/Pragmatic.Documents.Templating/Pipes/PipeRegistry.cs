using System.Collections.Frozen;
using System.Globalization;

namespace Pragmatic.Documents.Templating.Pipes;

/// <summary>
/// Registry of available template pipes. Immutable after construction.
/// </summary>
public sealed class PipeRegistry
{
    private readonly FrozenDictionary<string, ITemplatePipe> _pipes;

    private PipeRegistry(IEnumerable<ITemplatePipe> pipes)
    {
        _pipes = pipes.ToFrozenDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Default registry with built-in pipes.</summary>
    public static PipeRegistry Default { get; } = new(BuiltInPipes.All);

    /// <summary>Create a new registry by adding pipes to this one.</summary>
    public PipeRegistry With(params ITemplatePipe[] additional)
    {
        var all = _pipes.Values.Concat(additional);
        return new PipeRegistry(all);
    }

    /// <summary>Execute a named pipe.</summary>
    /// <exception cref="InvalidOperationException">
    ///     No pipe of that name is registered. The message says what is registered, and — for a pipe
    ///     that exists in another package — which package and which call add it.
    /// </exception>
    public object? Execute(string name, object? input, IReadOnlyList<string> args, CultureInfo culture)
    {
        if (!_pipes.TryGetValue(name, out var pipe))
            throw new InvalidOperationException(NoPipeCalled(name));
        return pipe.Execute(input, args, culture);
    }

    /// <summary>Check if a pipe is registered.</summary>
    public bool Contains(string name) => _pipes.ContainsKey(name);

    /// <summary>
    ///     The pipes that exist and are <b>not</b> here: they live in
    ///     <c>Pragmatic.Documents.Templating.I18N</c>, which this package cannot reference because the
    ///     dependency runs the other way.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A list of names in one package about types declared in another ages in silence, so it is
    ///     pinned from the I18N suite, where both are visible
    ///     (<c>TheHintNamesEveryPipeThisPackageAddsTests</c>): that test compares this list, as the
    ///     message prints it, with what <c>WithI18N()</c> actually adds.
    /// </remarks>
    private static readonly string[] PipesFromTheI18NPackage = ["currency", "date", "percent"];

    /// <summary>
    ///     What to say when a template names a pipe nobody registered.
    /// </summary>
    /// <remarks>
    ///     "Unknown pipe: 'date'" was the whole message, and it names the pipe rather than the package
    ///     that has it — so an application read the five built-ins, concluded there was no date
    ///     formatting, and wrote its own. Twice, in the same example.
    /// </remarks>
    private string NoPipeCalled(string name)
    {
        var registered = string.Join(", ", _pipes.Keys.Order(StringComparer.OrdinalIgnoreCase));

        if (PipesFromTheI18NPackage.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return $"Unknown pipe: '{name}'. It is one of the pipes "
                   + $"Pragmatic.Documents.Templating.I18N adds ({string.Join(", ", PipesFromTheI18NPackage)}): "
                   + "reference that package and build the registry with PipeRegistry.Default.WithI18N(). "
                   + $"Registered here: {registered}.";
        }

        return $"Unknown pipe: '{name}'. Registered here: {registered}. "
               + "A pipe of your own is added with PipeRegistry.Default.With(new YourPipe()).";
    }
}
