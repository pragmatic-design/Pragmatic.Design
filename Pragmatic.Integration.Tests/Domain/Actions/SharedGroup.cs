using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;

namespace Pragmatic.Integration.Tests.Domain.Actions;

/// <summary>
///     A group with no prefix of its own: it exists only so its endpoints share configuration.
/// </summary>
/// <remarks>
///     The generated registration gives such a group a <c>MapGroup</c> anyway: mapped straight onto the
///     root, its endpoints would have nothing to hang the <c>ConfigureGroup("Shared", …)</c> options on,
///     and every option set for it would be dropped without a word.
/// </remarks>
[EndpointGroup("")]
public sealed class SharedGroup;

/// <summary>An endpoint inside that group, which answers whatever reaches it.</summary>
[DomainAction]
[Endpoint(Pragmatic.Endpoints.HttpVerb.Get, "/api/shared-report")]
[EndpointGroup<SharedGroup>]
public partial class SharedReportAction : DomainAction<string>
{
    /// <inheritdoc />
    public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult(Result<string, IError>.Success("shared"));
}
