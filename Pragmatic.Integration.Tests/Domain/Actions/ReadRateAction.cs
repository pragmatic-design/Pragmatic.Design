using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization;
using Pragmatic.Caching.Attributes;
using Pragmatic.Integration.Tests.Domain.Errors;
using Pragmatic.Integration.Tests.Infrastructure;
using Pragmatic.Result;

namespace Pragmatic.Integration.Tests.Domain.Actions;

/// <summary>A cacheable read that is an action, not a query: the shape the attribute's own example shows.</summary>
/// <remarks>
///     The generator gives it <c>ICacheable</c>; without a reader every invocation would run the body.
/// </remarks>
[DomainAction]
[Cacheable(Duration = "5m", Tags = ["rates"])]
[RequirePermission(RatePermissions.Read)]
public partial class ReadRateAction : DomainAction<string>
{
    /// <summary>The currency the rate is for — the cache key.</summary>
    public required string Currency { get; init; }

    private IRateSource _rates = null!;

    /// <summary>The currency no rate exists for: the read runs, and fails.</summary>
    public const string Unknown = "XXX";

    /// <inheritdoc />
    public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
    {
        var rate = _rates.Read(Currency);

        return Task.FromResult(Currency == Unknown
            ? Result<string, IError>.Failure(new NotFoundError { ResourceType = "Rate", ResourceId = Currency })
            : Result<string, IError>.Success(rate));
    }
}
