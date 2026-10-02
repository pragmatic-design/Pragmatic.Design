using System.Security.Claims;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Integration.Tests.Infrastructure;
using Pragmatic.Result;

namespace Pragmatic.Integration.Tests.Domain.Actions;

/// <summary>
///     A read with <c>[ResponseCache]</c> in its default, shared form, answering with who asked and how
///     many reads the source has served — so a response that came from the output cache shows, and so
///     does one that was computed for somebody else.
/// </summary>
/// <remarks>
///     Anonymous callers are let in on purpose: the anonymous request is the control that proves the
///     cache is on at all, against which the authenticated requests are read.
/// </remarks>
[DomainAction]
[Endpoint(Pragmatic.Endpoints.HttpVerb.Get, "/api/rates/shared")]
[AllowAnonymous]
[ResponseCache(Duration = 60)]
public partial class ReadSharedRateAction : DomainAction<string>
{
    /// <summary>Who is calling, when anybody is.</summary>
    [FromClaim(ClaimTypes.Name, IsRequired = false)]
    public string? UserName { get; init; }

    private IRateSource _rates = null!;

    /// <inheritdoc />
    public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult(Result<string, IError>.Success($"{UserName ?? "anonymous"}|{_rates.Read("EUR")}"));
}
