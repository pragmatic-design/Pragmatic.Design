namespace Pragmatic.Email.Templates.Nodes;

/// <summary>
///     Raw HTML block.
/// </summary>
/// <remarks>
///     <para>
///         <b>Trust contract.</b> Safe by default: when <see cref="IsTrusted" /> is <c>false</c>
///         (the default), the resolved <see cref="Html" /> is HTML-encoded by the renderer. Because
///         <see cref="Html" /> may contain <c>{{expressions}}</c> bound to runtime data, set
///         <see cref="IsTrusted" /> to <c>true</c> only when both the template author and the bound
///         data are fully trusted — the resolved value is then emitted verbatim with no sanitization.
///     </para>
/// </remarks>
public sealed record EmailHtmlTemplate : EmailNodeTemplate
{
    /// <summary>Raw HTML content. May contain <c>{{expressions}}</c>.</summary>
    public required string Html { get; init; }

    /// <summary>
    ///     Whether the resolved <see cref="Html" /> is trusted and may be emitted verbatim.
    ///     Defaults to <c>false</c> (safe). Set to <c>true</c> only for fully-trusted template + data.
    /// </summary>
    public bool IsTrusted { get; init; }
}
