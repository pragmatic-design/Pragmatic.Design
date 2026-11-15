using Pragmatic.Composition.Attributes;

namespace Worker.Host;

/// <summary>
///     The worker's topology: no module to include, no database, and no HTTP. The job and the service it uses are the host's own.
/// </summary>
/// <remarks>Anonymous: a worker serves no request, so there is nobody to authenticate (PRAG1695 asks the host to say so).</remarks>
[Module]
[AnonymousHost]
public sealed class WorkerHostModule;
