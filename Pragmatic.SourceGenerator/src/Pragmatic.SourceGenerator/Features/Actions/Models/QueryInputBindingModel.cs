namespace Pragmatic.SourceGenerator.Features.Actions.Models;

/// <summary>An input of the query a <c>[LoadFrom]</c> runs, and the operation's property bound to it by name.</summary>
/// <param name="QueryProperty">The query's input property.</param>
/// <param name="OperationProperty">The operation's property whose value it receives.</param>
internal sealed record QueryInputBindingModel(string QueryProperty, string OperationProperty);
