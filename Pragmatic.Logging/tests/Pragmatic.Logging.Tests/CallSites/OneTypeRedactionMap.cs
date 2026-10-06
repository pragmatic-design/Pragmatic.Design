using Pragmatic.Serialization;

namespace Pragmatic.Logging.Tests.CallSites;

/// <summary>A redaction map that knows one type, so the declared redactor is not empty.</summary>
/// <remarks>
///     A non-empty declared redactor is what an application has, and it is what turns the generic
///     deferred path off: the call-site path has to hold with it on.
/// </remarks>
internal sealed class OneTypeRedactionMap : IRedactionMap
{
    public bool TryGetRedactedMembers(Type type, out IReadOnlyList<RedactedMember> members)
    {
        members = type == typeof(Uri) ? [new RedactedMember("Host", RedactionReason.NotLogged)] : [];
        return members.Count > 0;
    }
}
