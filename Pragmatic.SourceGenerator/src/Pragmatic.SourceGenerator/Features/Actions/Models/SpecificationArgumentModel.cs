namespace Pragmatic.SourceGenerator.Features.Actions.Models;

/// <summary>
///     One argument of the rule a load reads by: the parameter, and the operation's property bound to it by
///     name.
/// </summary>
/// <param name="Parameter">The parameter of the specification method, as the named argument spells it.</param>
/// <param name="Property">The operation's property whose value it receives.</param>
internal sealed record SpecificationArgumentModel(string Parameter, string Property);
