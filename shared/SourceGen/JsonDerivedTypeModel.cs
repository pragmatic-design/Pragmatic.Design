// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>
///     A derived type of a polymorphic base: its type expression and the (already-formatted) discriminator
///     argument (a quoted string or a bare integer) for <c>new JsonDerivedType(typeof(T), ...)</c>.
/// </summary>
internal sealed record JsonDerivedTypeModel(
    string TypeExpr,
    string DiscriminatorArg);
