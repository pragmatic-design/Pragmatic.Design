using System.Text;

namespace Pragmatic.SourceGenerator.Features.Glossary;

/// <summary>
///     The topic a message travels on, derived from its namespace the way
///     <c>Pragmatic.Messaging.Routing.DefaultMessageRouter.GetTopic</c> derives it:
///     <c>{boundary-kebab}.events</c>, where the boundary is the namespace's second segment.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>This is a second copy of a rule that also lives in the runtime, and it cannot be the
///         only copy nor share one.</b> <c>IMessageRouter</c> is a service resolved when a message is
///         published; this generator runs at compile time and cannot reference
///         <c>Pragmatic.Messaging</c> at all (it targets netstandard2.0 and must stay dependency-free).
///         So the rule is mirrored here, and the generated document <b>declares</b> that it assumed it
///         (<c>x-pragmatic-address-rule</c>) rather than implying the application could not have
///         changed it.
///     </para>
///     <para>
///         ⚠️ <b>Why the generator does not detect a custom router instead.</b> An application that
///         registers its own <c>IMessageRouter</c> usually registers it in the host, while the events
///         live in a contracts assembly — a different compilation, which this generator cannot see even
///         in principle. A detection that is right only when the two happen to sit together would be
///         silent exactly when it is wrong, so the document names its assumption and lets a reader
///         check it.
///     </para>
///     <para>
///         The one rule that must not drift is the kebab-casing, because it is what a broker's
///         exchanges are already named after in a running deployment: <c>LeaveRequests</c> →
///         <c>leave-requests</c>, and a boundary of one word is unchanged.
///         <c>TheChannelAddressIsTheTopicTheTransportUsesTests</c> holds the table.
///     </para>
/// </remarks>
internal static class MessageTopicConvention
{
    /// <summary>The rule, in words, for the document to carry beside the addresses it produced.</summary>
    public const string Rule =
        "DefaultMessageRouter: the topic is {boundary}.events, where {boundary} is the kebab-cased "
        + "second segment of the message's namespace (the first when there is only one). An application "
        + "that registers its own IMessageRouter routes by its own rule, and these addresses do not "
        + "describe it.";

    /// <summary>
    ///     The topic for a message declared in <paramref name="containingNamespace" />.
    /// </summary>
    /// <remarks>
    ///     ⚠️ An event in the <b>global</b> namespace answers <c>".events"</c>, which looks like a
    ///     mistake and is not: it is what <c>DefaultMessageRouter</c> answers for a type whose
    ///     <c>Namespace</c> is null, so it is the topic such an event would really be published to. The
    ///     first version of this method returned an empty string instead — tidier, and a lie about where
    ///     the message goes. An existing template test caught the divergence, which is the whole reason
    ///     a mirrored rule is worth testing against the original's behaviour rather than against what it
    ///     ought to be.
    /// </remarks>
    public static string TopicFor(string containingNamespace)
    {
        var parts = (containingNamespace ?? "").Split('.');
        var boundary = parts.Length >= 2 ? parts[1] : parts[0];

        return ToKebabCase(boundary) + ".events";
    }

    /// <summary>
    ///     Mirrors <c>DefaultMessageRouter.ToKebabCase</c>: a dash before every upper-case letter that
    ///     is not the first, everything lower-cased.
    /// </summary>
    private static string ToKebabCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var result = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
                result.Append('-');
            result.Append(char.ToLowerInvariant(c));
        }

        return result.ToString();
    }
}
