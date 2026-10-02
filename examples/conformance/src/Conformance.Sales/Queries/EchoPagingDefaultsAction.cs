using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;

namespace Conformance.Sales.Queries;

/// <summary>
///     A GET with two scalars that carry a C# initializer, answering with what it received.
/// </summary>
/// <remarks>
///     <para>
///         The generated GET binding reads every scalar from the query string. The question is what the
///         parameter is worth when the caller <b>does not send it</b>: the declared initializer, or
///         <c>default</c>. ⚠️ It must be the first: a <c>Page = 1</c> reaching the handler as 0 would
///         turn a <c>WithPaging(0, 0)</c> into an empty result without saying why, and push consumers to
///         work around it with <c>int?</c> and the default inside <c>Execute</c>.
///     </para>
///     <para>
///         ⚠️ <c>[AllowAnonymous]</c>: binding is what is measured here, and a permission in between would
///         refuse first.
///     </para>
/// </remarks>
[DomainAction]
[AllowAnonymous]
[Endpoint(HttpVerb.Get, "api/paging-defaults")]
public partial class EchoPagingDefaultsAction : DomainAction<PagingEcho>
{
    /// <summary>Which page. Omitted, the first.</summary>
    public int Page { get; init; } = 1;

    /// <summary>How many rows per page. Omitted, twenty.</summary>
    public int PageSize { get; init; } = 20;

    /// <inheritdoc />
    public override Task<Result<PagingEcho, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult(Result<PagingEcho, IError>.Success(new PagingEcho(Page, PageSize)));
}

/// <summary>What the action received, returned as it is.</summary>
public sealed record PagingEcho(int Page, int PageSize);
