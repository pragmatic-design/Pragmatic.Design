using Casework.Verify;
using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Steps;
using Pragmatic.Internationalization.AspNetCore.Steps;

namespace Casework.Verify.Host;

/// <summary>
///     The topology of this process: one module, one database.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>No <c>[AnonymousHost]</c></b>, on purpose: it says "this service has no users", which is
///         true, and is read as "this service has no callers", which is not. A system records an answer
///         through this service's API, with a token this host validates, and the attribute would be a
///         declaration that is false — the comfortable kind, because nothing would fail.
///     </para>
///     <para>
///         What it has instead is not an identity store: <c>Program.cs</c> validates a JWT and holds no
///         accounts, so the caller is a subject with a role and nothing this service could look up.
///     </para>
/// </remarks>
[Module]
[Include<VerifyModule, VerifyDatabase>]
[NeedsStep<InternationalizationStep>]
[NeedsStep<RoutingStep>]
public sealed class VerifyHostModule;
