namespace Pragmatic.SourceGenerator.Features.Logging.Models;

/// <summary>A type the generated call site is written into, as the generated file reopens it.</summary>
/// <param name="Keyword">How it is declared: <c>class</c>, <c>struct</c>, <c>record</c>, <c>record struct</c>, <c>interface</c>.</param>
/// <param name="Name">Its name with its type parameters, as the reopening must repeat them.</param>
/// <param name="IsStatic">
///     Whether it is static: omitting <c>static</c> on the reopening is a conflicting-modifiers error.
/// </param>
/// <param name="IsPartial">
///     Whether any of its declarations says <c>partial</c>. A container that does not cannot be reopened,
///     and the call site is not generated; the analyzer names the type to change.
/// </param>
internal sealed record LogContainerModel(string Keyword, string Name, bool IsStatic, bool IsPartial);
