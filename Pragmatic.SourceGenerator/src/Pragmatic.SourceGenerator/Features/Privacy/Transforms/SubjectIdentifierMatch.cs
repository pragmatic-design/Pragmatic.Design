namespace Pragmatic.SourceGenerator.Features.Privacy.Transforms;

/// <summary>
///     How a generated adapter turns the identity string the registry resolves into something it can
///     compare against the subject's identifier column.
/// </summary>
/// <remarks>
///     <para>
///         <c>ISubjectRegistry.ResolveIdentityAsync</c> returns a <c>string</c> and can return nothing
///         else — identities are stored encrypted, and what comes back is what was put in. The column,
///         on the other hand, is whatever the domain declared.
///     </para>
///     <para>
///         The conversion happens <em>before</em> the query, never inside it. <c>row.Id.ToString() ==
///         identity</c> reads fine and either fails to translate or evaluates client-side over the whole
///         table — for a data-subject lookup, the difference between an index seek and reading
///         everyone's data.
///     </para>
/// </remarks>
internal sealed class SubjectIdentifierMatch
{
    private SubjectIdentifierMatch(string? parseType)
    {
        ParseType = parseType;
        Operand = parseType is null ? "identity" : "subjectKey";
    }

    /// <summary>
    ///     The type to parse the identity into, or null when the identifier is already a string.
    /// </summary>
    public string? ParseType { get; }

    /// <summary>The expression the identifier column is compared to.</summary>
    public string Operand { get; }

    /// <summary>
    ///     The parse-or-give-up line to emit before the query, given what the caller returns when the
    ///     identity does not parse. Null when no conversion is needed.
    /// </summary>
    public string? GuardStatement(string noMatchResult)
        => ParseType is null
            ? null
            : $"if (!{ParseType}.TryParse(identity, out var {Operand})) return {noMatchResult};";

    /// <summary>
    ///     The match for an identifier of this declared type, or null when there is none — which is
    ///     PRAG2908.
    /// </summary>
    public static SubjectIdentifierMatch? For(string typeDisplay)
        // The nullable annotation makes no difference: a null column cannot equal an identity the
        // registry resolved to a non-null string.
        => typeDisplay.TrimEnd('?') switch
        {
            "string" => new SubjectIdentifierMatch(null),
            "System.Guid" => new SubjectIdentifierMatch("global::System.Guid"),
            "int" => new SubjectIdentifierMatch("int"),
            "long" => new SubjectIdentifierMatch("long"),
            _ => null
        };
}
