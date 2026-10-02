using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Caching.Models;

internal sealed record InvalidatesModel : GeneratorModel
{
    public EquatableArray<string> Tags { get; init; } = EquatableArray<string>.Empty;
    public EquatableArray<string> Keys { get; init; } = EquatableArray<string>.Empty;
    public required EquatableArray<PlaceholderPropertyModel> Properties { get; init; }
    public bool IsPartial { get; init; }

    /// <summary>
    ///     Fully qualified name of the declaring type. Kept from the symbol rather than rebuilt from
    ///     <c>Namespace + TypeName</c> so a nested type still resolves.
    /// </summary>
    public string TypeFqn { get; init; } = string.Empty;

    /// <summary>
    ///     The type implements <c>Pragmatic.Events.IDomainEvent</c>. Decides which of the two
    ///     invalidation paths applies: a generated <c>IDomainEventHandler</c> (the dispatcher calls it)
    ///     or the <c>ICacheInvalidator</c> partial (the mutation invoker calls it).
    /// </summary>
    public bool IsDomainEvent { get; init; }

    /// <summary>FQN of the Category type, or null for broadcast.</summary>
    public string? CategoryTypeFqn { get; init; }

    /// <summary>Declaration position for diagnostics (excluded from equality — see LocationInfo).</summary>
    public LocationInfo? Location { get; init; }
}

internal sealed record PlaceholderPropertyModel
{
    public required string Name { get; init; }
    public required string Type { get; init; }
}
