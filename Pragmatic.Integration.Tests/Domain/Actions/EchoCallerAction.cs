using System.Security.Claims;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;

namespace Pragmatic.Integration.Tests.Domain.Actions;

/// <summary>
///     Answers with the claims and cookies it was given, so a test can see what reached it.
/// </summary>
/// <remarks>
///     Every property is <c>required</c> or <c>init</c>, the shape that has to compile for claims and
///     cookies too. The required claim is the role because the test scheme lets a caller be
///     authenticated without one: a missing claim is then refused by the generated read, not by the
///     challenge in front of it.
/// </remarks>
[DomainAction]
[Endpoint(Pragmatic.Endpoints.HttpVerb.Get, "/api/caller")]
public partial class EchoCallerAction : DomainAction<string>
{
    /// <summary>The caller's role: required.</summary>
    [FromClaim(ClaimTypes.Role)]
    public required string Role { get; init; }

    /// <summary>Who is calling, when the principal says.</summary>
    [FromClaim(ClaimTypes.Name, IsRequired = false)]
    public string? UserName { get; init; }

    /// <summary>A claim the test scheme never issues: typed, with a declared default.</summary>
    [FromClaim("tier", IsRequired = false)]
    public int Tier { get; init; } = 2;

    /// <summary>The caller's session: required, and typed.</summary>
    [FromCookie("session")]
    public required Guid Session { get; init; }

    /// <summary>The caller's theme, when the browser sends one.</summary>
    [FromCookie("theme", IsRequired = false)]
    public string? Theme { get; init; }

    /// <summary>How much to show: typed, with a declared default.</summary>
    [FromCookie("level", IsRequired = false)]
    public int Level { get; init; } = 3;

    /// <inheritdoc />
    public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult(Result<string, IError>.Success(
            $"{Role}|{UserName ?? "none"}|{Tier}|{Session:N}|{Theme ?? "none"}|{Level}"));
}
