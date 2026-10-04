using Pragmatic.Serialization;

namespace Pragmatic.Logging.Benchmarks.Redaction;

/// <summary>
///     The map the generator would write for <see cref="Customer" /> had its <c>Email</c> been declared
///     <c>[PersonalData]</c>; written by hand because this project does not run the generator.
/// </summary>
internal sealed class CustomerRedactionMap : IRedactionMap
{
    private static readonly RedactedMember[] Members =
        [new RedactedMember(nameof(Customer.Email), RedactionReason.PersonalData, "Contact")];

    public bool TryGetRedactedMembers(Type type, out IReadOnlyList<RedactedMember> members)
    {
        members = type == typeof(Customer) ? Members : [];
        return members.Count > 0;
    }
}
