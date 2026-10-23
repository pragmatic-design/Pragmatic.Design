using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>An identity entity's inserts: the columns bound without the key, and the key read back (<see cref="KeyRead" />, over <c>r</c> and <c>d</c>).</summary>
internal sealed record IdentityModel(EquatableArray<string> BindsWithoutKey, string KeyProperty, string KeyRead);
