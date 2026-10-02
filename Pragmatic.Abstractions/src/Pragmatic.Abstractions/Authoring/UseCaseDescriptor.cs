namespace Pragmatic.Authoring;

/// <summary>
///     One entry of a module's generated use-case catalog: a member annotated with
///     <see cref="UseCaseAttribute" />, the <see cref="RuleAttribute" />s declared beside it, and
///     where in the source it lives.
/// </summary>
/// <remarks>
///     <para>
///         The catalog is the consumer those two attributes name in their own summaries ("inert at
///         runtime, consumed by tooling and the generated use-case catalog"). The generator emits
///         <c>{Assembly}.Generated.PragmaticUseCases</c>, whose <c>All</c> is a list of these.
///     </para>
///     <para>
///         It is a traceability artifact that cannot drift from the code, because the compilation that
///         builds the code writes it.
///     </para>
/// </remarks>
public sealed class UseCaseDescriptor
{
    /// <summary>
    ///     The use-case identifier, or <c>null</c> for an entry of
    ///     <c>PragmaticUseCases.RulesWithoutAUseCase</c> — rules declared on a member that carries no
    ///     <see cref="UseCaseAttribute" />.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>The use case's human-readable title, when the author gave one.</summary>
    public string? Title { get; init; }

    /// <summary>
    ///     The annotated member, fully qualified: a type, or a type and method name. Namespace
    ///     included, because a simple name is not something a reader of the catalog can go and find.
    /// </summary>
    public required string Target { get; init; }

    /// <summary>
    ///     The source file, relative to the project directory when the build makes it visible
    ///     (<c>ProjectDir</c>), and the bare file name otherwise. Never an absolute path: a build
    ///     machine's directory layout has no business in a shipped assembly.
    /// </summary>
    public required string File { get; init; }

    /// <summary>The 1-based line of the declaration in <see cref="File" />.</summary>
    public required int Line { get; init; }

    /// <summary>The business rules, in the author's words and in the order they were written.</summary>
    public required IReadOnlyList<string> Rules { get; init; }
}
