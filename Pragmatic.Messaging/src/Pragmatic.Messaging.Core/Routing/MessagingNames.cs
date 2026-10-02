using System.Text;

namespace Pragmatic.Messaging.Routing;

/// <summary>
///     The one place a CLR name becomes a broker name.
/// </summary>
/// <remarks>
///     Shared by <see cref="SubscriptionName" /> and <see cref="DefaultMessageRouter" /> because a
///     subscription name and a topic name have to agree on what a type is called: two copies of this
///     conversion is how a publisher and a consumer end up on different addresses for one message.
///     ⚠️ The SG-generated router emits its own copy as class members — it runs in the consumer's
///     assembly and cannot see an internal here — and that copy has to keep matching this one.
/// </remarks>
internal static class MessagingNames
{
    /// <summary>Converts a PascalCase name to the kebab-case a broker address is written in.</summary>
    internal static string ToKebabCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var result = new StringBuilder(name.Length + 8);

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
