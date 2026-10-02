namespace Pragmatic.Messaging.RequestReply;

/// <summary>
///     Naming conventions for distributed request/reply. The REQUESTER computes the queue at
///     runtime from the request type; the RESPONDER side is SG-generated with the same
///     convention baked in at compile time — both must stay in sync.
/// </summary>
public static class RequestReplyConventions
{
    /// <summary>Point-to-point queue a request type is sent to: <c>requests.{kebab-case-name}</c>.</summary>
    public static string QueueFor(Type requestType)
        => "requests." + ToKebab(requestType.Name);

    /// <summary>Kebab-cases a type name (OrderQuote → order-quote). Mirrors the SG helper.</summary>
    public static string ToKebab(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var builder = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                    builder.Append('-');
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
