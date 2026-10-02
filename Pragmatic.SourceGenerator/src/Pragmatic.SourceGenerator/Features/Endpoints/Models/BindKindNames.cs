namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     Classifies a parameter by the <em>name</em> of its type, for the endpoints this generator builds
///     itself.
/// </summary>
/// <remarks>
///     <para>
///         Resource CRUD and trait endpoints have no symbol at any point — their models are assembled
///         from type text — so <c>BindKindClassifier</c>, which asks the symbol, never sees them. Their
///         route parameters are identifiers written by the builder itself, so a short table covers them.
///     </para>
///     <para>
///         A name this does not recognise stays <see cref="BindKind.Complex" />, which makes the
///         generated code hand the raw string to a typed parameter and **fail the build**. That is the
///         intended failure mode: a misclassification is a compile error, never a request that binds
///         the wrong value.
///     </para>
/// </remarks>
internal static class BindKindNames
{
    public static BindKind FromTypeName(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return BindKind.Complex;

        var name = typeName!.TrimEnd('?');
        if (name.StartsWith("global::", System.StringComparison.Ordinal))
            name = name.Substring(8);

        var lastDot = name.LastIndexOf('.');
        if (lastDot >= 0)
            name = name.Substring(lastDot + 1);

        return name switch
        {
            "string" or "String" => BindKind.String,
            "Guid" or "int" or "Int32" or "long" or "Int64" or "short" or "Int16" or "byte" or "Byte"
                or "uint" or "UInt32" or "ulong" or "UInt64" or "bool" or "Boolean" or "decimal" or "Decimal"
                or "double" or "Double" or "float" or "Single" or "DateTime" or "DateTimeOffset"
                or "DateOnly" or "TimeOnly" or "TimeSpan" => BindKind.Parsable,
            _ => BindKind.Complex,
        };
    }
}
