using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>One read by key with navigations: the key a <c>[LoadEntity(Include = …)]</c> names it by, and its paths (<c>RoomTypes.Rates</c>) in the order declared.</summary>
internal sealed record IncludeReadModel(string Key, EquatableArray<string> Paths);
