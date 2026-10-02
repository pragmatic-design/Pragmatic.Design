namespace Pragmatic.Identity;

/// <summary>What kind of party is acting on someone else's behalf.</summary>
/// <remarks>
///     Informational for authorisation — the policy decides authority, not this — but it drives audit,
///     notification and the never-delegable list, which are legitimately different for a support
///     operator and for an autonomous process.
/// </remarks>
public enum ActorKind
{
    /// <summary>Another person: support, a colleague covering while someone is away.</summary>
    User = 0,

    /// <summary>A background process, job or integration.</summary>
    Service = 1,

    /// <summary>An agent runtime executing on the subject's behalf.</summary>
    Agent = 2,
}
