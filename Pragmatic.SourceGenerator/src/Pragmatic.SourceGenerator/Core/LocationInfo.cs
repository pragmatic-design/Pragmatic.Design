using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     Cache-safe carrier of a diagnostic position for incremental-generator models.
///     Captures file path + spans as plain values and rebuilds an external-file
///     <see cref="Location"/> on demand — it must NOT hold the original <see cref="Location"/>:
///     that object references its <see cref="SyntaxTree"/>, and a cached model outliving its
///     compilation would make the generator report a diagnostic whose tree is not part of the
///     CURRENT compilation. Roslyn's suppression filtering then throws
///     "SyntaxTree is not part of the compilation", killing source generation / classification /
///     CodeLens in the IDE (the CLI never notices — single run, fresh trees).
///     <para>
///     Excluded from equality (<see cref="Equals(LocationInfo)"/> always true, hash 0): a
///     position never changes the generated output, and including it would make the owning model
///     compare unequal on every re-parse, defeating incremental caching. Trade-offs of the
///     rebuilt location: possibly slightly stale after an edit that shifts the declaration, and
///     <c>#pragma warning disable</c> cannot suppress it (no tree) — the standard, accepted ones
///     for source generators.
///     </para>
/// </summary>
internal readonly struct LocationInfo : IEquatable<LocationInfo>
{
    private readonly string? _filePath;
    private readonly TextSpan _textSpan;
    private readonly LinePositionSpan _lineSpan;

    private LocationInfo(string filePath, TextSpan textSpan, LinePositionSpan lineSpan)
    {
        _filePath = filePath;
        _textSpan = textSpan;
        _lineSpan = lineSpan;
    }

    /// <summary>Captures the position of a source location as plain values, or null if there is none.</summary>
    public static LocationInfo? From(Location? location)
    {
        if (location?.SourceTree is not { } tree || string.IsNullOrEmpty(tree.FilePath))
            return null;

        return new LocationInfo(tree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }

    /// <summary>A tree-free <see cref="Location"/> rebuilt from the captured values, valid on any compilation.</summary>
    public Location? ToLocation()
        => _filePath is null ? null : Location.Create(_filePath, _textSpan, _lineSpan);

    /// <summary>
    ///     Rebinds the captured position to the CURRENT compilation's tree for the same file,
    ///     so source suppressions (<c>#pragma warning disable</c>, <c>[SuppressMessage]</c>)
    ///     apply to the diagnostic. Falls back to the tree-free location when the file is gone
    ///     or the span no longer fits (mid-edit).
    /// </summary>
    public Location? ToLocation(Compilation? compilation)
    {
        if (_filePath is null)
            return null;

        if (compilation is not null)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (string.Equals(tree.FilePath, _filePath, StringComparison.Ordinal) && _textSpan.End <= tree.Length)
                    return Location.Create(tree, _textSpan);
            }
        }

        return Location.Create(_filePath, _textSpan, _lineSpan);
    }

    // Excluded from equality: a location never changes the generated output, and including it
    // would make the owning model compare unequal on every re-parse, defeating incremental caching.
    public bool Equals(LocationInfo other) => true;
    public override bool Equals(object? obj) => obj is LocationInfo;
    public override int GetHashCode() => 0;
}
