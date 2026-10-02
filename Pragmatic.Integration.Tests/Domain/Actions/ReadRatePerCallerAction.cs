using System.Security.Claims;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Integration.Tests.Infrastructure;
using Pragmatic.Result;

namespace Pragmatic.Integration.Tests.Domain.Actions;

/// <summary>
///     The shared <c>[ResponseCache]</c> varied by the header that says who is calling — the form once
///     recommended for keeping per-user answers apart.
/// </summary>
[DomainAction]
[Endpoint(Pragmatic.Endpoints.HttpVerb.Get, "/api/rates/per-caller")]
[AllowAnonymous]
[ResponseCache(Duration = 60, VaryByHeaders = [NoCredentialsAuthenticationHandler.UserHeader])]
public partial class ReadRatePerCallerAction : DomainAction<string>
{
    /// <summary>Who is calling, when anybody is.</summary>
    [FromClaim(ClaimTypes.Name, IsRequired = false)]
    public string? UserName { get; init; }

    private IRateSource _rates = null!;

    /// <inheritdoc />
    public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult(Result<string, IError>.Success($"{UserName ?? "anonymous"}|{_rates.Read("EUR")}"));
}
