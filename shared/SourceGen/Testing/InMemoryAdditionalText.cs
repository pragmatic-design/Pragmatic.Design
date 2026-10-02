// Pragmatic.SourceGen.Testing - In-memory additional file
// This file is linked into test projects, not compiled directly.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Pragmatic.SourceGen.Testing;

/// <summary>
///     An <c>AdditionalFiles</c> item held in memory — a <c>roles.pragmatic.json</c>, a translation file — for a
///     generator run under test.
/// </summary>
public sealed class InMemoryAdditionalText(string path, string content) : AdditionalText
{
    /// <inheritdoc />
    public override string Path { get; } = path;

    /// <inheritdoc />
    public override SourceText? GetText(CancellationToken cancellationToken = default) => SourceText.From(content);
}
