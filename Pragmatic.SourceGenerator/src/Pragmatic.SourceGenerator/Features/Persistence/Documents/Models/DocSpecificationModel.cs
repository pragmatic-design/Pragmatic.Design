namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;

/// <summary>
///     A <c>Spec&lt;T&gt;.Where(predicate)</c> on a Pragmatic entity, compiled where it is written: its condition (MongoDB's statements, Cosmos DB's
///     text and value writers), the interceptor that builds a <c>DocCompiledSpecification&lt;T&gt;</c> instead of Pragmatic's leaf, and the values
///     the lambda captured. The condition's parts are the translation's, as text.
/// </summary>
/// <param name="Index">The specification's number in the file.</param>
/// <param name="Attribute">The <c>[InterceptsLocation]</c> attribute of the call.</param>
/// <param name="EntityType">The entity, fully qualified.</param>
/// <param name="Mongo">The statements that write the MongoDB condition to <c>w</c> from the slots <c>s</c>, one per line, indented.</param>
/// <param name="CosmosLiteral">Cosmos DB's condition text, as a C# string literal.</param>
/// <param name="CosmosWrites">How many Cosmos DB parameter values the condition writes.</param>
/// <param name="CosmosWriter">The writer of those values (<c>static (w, i, s) =&gt; …</c>), or <c>null</c>.</param>
/// <param name="Captures">The values the lambda captured, read off its tree, in the condition's order.</param>
internal sealed record DocSpecificationModel(int Index, string Attribute, string EntityType, string Mongo, string CosmosLiteral, int CosmosWrites, string CosmosWriter, string Captures);
