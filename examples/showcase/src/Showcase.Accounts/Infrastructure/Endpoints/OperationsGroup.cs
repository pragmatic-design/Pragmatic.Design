namespace Showcase.Accounts.Infrastructure.Endpoints;

/// <summary>
///     The operator's surface: what a package proposes and this module chooses to publish for the
///     people running the system, under one prefix and one set of group options.
/// </summary>
/// <remarks>
///     Its only member is an exposed package action (<c>[ExposeEndpoint&lt;GetConfigValues,
///     OperationsGroup&gt;]</c> on <see cref="AccountsModule" />), so no <c>[Endpoint]</c> carries the
///     group into the module's endpoint metadata: the host finds it from the exposed endpoint.
/// </remarks>
[EndpointGroup("/api/operations", Tag = "Operations")]
public sealed class OperationsGroup;
