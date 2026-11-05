using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;

/// <summary>
///     A Pragmatic entity of a hierarchy as its document partial writes and reads it (#539): the discriminator field, this type's value of it, and
///     the types below it, whose own reader and writer a document of theirs is handed to.
/// </summary>
/// <param name="FieldLiteral">The discriminator field's name, as a C# string literal.</param>
/// <param name="NameLiteral">This type's discriminator value, as a C# string literal.</param>
/// <param name="IsAbstract">An abstract type: a document is always one of the types below it.</param>
/// <param name="Below">The types below this one, the deepest first.</param>
internal sealed record DocumentInheritanceModel(
    string FieldLiteral,
    string NameLiteral,
    bool IsAbstract,
    EquatableArray<DocumentSubtypeModel> Below);
