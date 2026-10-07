using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Serialization.Models;

/// <summary>A generated method that writes one type as a JSON object to a <c>Utf8JsonWriter</c>.</summary>
/// <param name="Name">The method's name, unique in the assembly's writer class.</param>
/// <param name="TypeExpr">The type it writes, fully qualified.</param>
/// <param name="Members">What it writes, in the order the type's JSON shape gives.</param>
/// <param name="IsEntryPoint">
///     Whether something outside the writer class calls it, and so needs a delegate field: a logged
///     argument's type, or a response's.
/// </param>
/// <remarks>
///     One type can have more than one method: the mask a declaration puts on a nested member depends on the
///     path from the outermost type, so a nested object reached under a masked path gets a method of its own,
///     and one reached outside every masked path gets the plain one.
/// </remarks>
internal sealed record JsonWriterMethodModel(
    string Name,
    string TypeExpr,
    EquatableArray<JsonWriterMemberModel> Members,
    bool IsEntryPoint);
