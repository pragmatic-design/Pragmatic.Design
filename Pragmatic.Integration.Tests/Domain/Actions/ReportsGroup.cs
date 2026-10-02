using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;

namespace Pragmatic.Integration.Tests.Domain.Actions;

/// <summary>
///     A route group, so a test can configure options on one — the permissions above all.
/// </summary>
/// <remarks>
///     The group options are a runtime shape a host configures, and no application in this
///     repository configured their permissions: the code that enforced them was generated and never
///     exercised by a consumer, which is how it stayed an opaque claim comparison for as long as it did.
///     This group exists so the enforcement has a caller.
/// </remarks>
[EndpointGroup("/api/reports", Tag = "Reports")]
public sealed class ReportsGroup;

/// <summary>An endpoint inside that group, which answers whatever reaches it.</summary>
[DomainAction]
[Endpoint(Pragmatic.Endpoints.HttpVerb.Get, "/monthly")]
[EndpointGroup<ReportsGroup>]
public partial class MonthlyReportAction : DomainAction<string>
{
    /// <inheritdoc />
    public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult(Result<string, IError>.Success("monthly"));
}
