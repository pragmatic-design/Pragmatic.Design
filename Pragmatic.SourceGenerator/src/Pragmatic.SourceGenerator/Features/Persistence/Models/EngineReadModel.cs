using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     A read by key with navigations an operation's invoker asks of an engine that compiles its reads: the entity and the paths, known at
///     build. Handed from the feature that writes the invoker to the engine that compiles the read, since no generator sees another's output.
/// </summary>
internal sealed record EngineReadModel(string EntityFullTypeName, EquatableArray<string> Paths);
