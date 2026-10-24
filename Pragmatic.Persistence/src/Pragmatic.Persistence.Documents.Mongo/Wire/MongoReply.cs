using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Mongo.Wire;

/// <summary>How a reply says it failed: <c>ok: 0</c> with a code and a message, or <c>ok: 1</c> with <c>writeErrors</c> or a <c>writeConcernError</c>.</summary>
internal static class MongoReply
{
    public static void ThrowIfFailed(BsonDocument reply)
    {
        if (!reply.TryGetNumber("ok", out var ok) || ok != 1)
        {
            reply.TryGetNumber("code", out var code);
            reply.TryGetString("codeName", out var codeName);
            reply.TryGetString("errmsg", out var message);
            throw new MongoCommandException((int)code, codeName.Length > 0 ? codeName : "CommandFailed", message.Length > 0 ? message : "the server answered ok: 0", Labels(reply));
        }

        if (reply.TryGetDocument("writeErrors", out var writeErrors))
        {
            var errors = writeErrors.Documents();
            if (errors.Count > 0)
            {
                var first = errors[0];
                first.TryGetNumber("code", out var code);
                first.TryGetNumber("index", out var index);
                first.TryGetString("errmsg", out var message);
                throw new MongoWriteException((int)code, (int)index, message, errors.Count);
            }
        }

        if (reply.TryGetDocument("writeConcernError", out var concern))
        {
            concern.TryGetNumber("code", out var code);
            concern.TryGetString("codeName", out var codeName);
            concern.TryGetString("errmsg", out var message);
            throw new MongoCommandException((int)code, codeName.Length > 0 ? codeName : "WriteConcernFailed", message, Labels(reply));
        }
    }

    /// <summary>The reply's <c>errorLabels</c> array (its elements are named "0", "1", …).</summary>
    private static List<string> Labels(BsonDocument reply)
    {
        var labels = new List<string>();
        if (reply.TryGetDocument("errorLabels", out var array))
        {
            foreach (var (name, _) in array.Elements())
            {
                if (array.TryGetString(name, out var label))
                    labels.Add(label);
            }
        }

        return labels;
    }
}
