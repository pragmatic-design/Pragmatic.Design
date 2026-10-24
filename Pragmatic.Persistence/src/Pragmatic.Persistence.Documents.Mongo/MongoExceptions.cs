namespace Pragmatic.Persistence.Documents.Mongo;

/// <summary>What the server or the connection refused.</summary>
public class MongoException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A command the server answered with <c>ok: 0</c>: its code, code name and message.</summary>
public class MongoCommandException(int code, string codeName, string message, IReadOnlyList<string>? labels = null) : MongoException($"{codeName} ({code}): {message}")
{
    public int Code { get; } = code;

    public string CodeName { get; } = codeName;

    public string ServerMessage { get; } = message;

    /// <summary>The reply's <c>errorLabels</c>: <c>TransientTransactionError</c> says the whole transaction may be run again.</summary>
    public IReadOnlyList<string> Labels { get; } = labels ?? [];

    public bool HasLabel(string label) => Labels.Contains(label);
}

/// <summary>A write the server accepted as a command but refused for a document (<c>writeErrors</c>): the first error, and how many there were.</summary>
public sealed class MongoWriteException(int code, int index, string message, int count) : MongoException($"Write error {code} at document {index}{(count > 1 ? $" (and {count - 1} more)" : "")}: {message}")
{
    public int Code { get; } = code;

    /// <summary>The position, in the command's documents, of the first one refused.</summary>
    public int Index { get; } = index;

    public int Count { get; } = count;

    /// <summary>E11000: a unique index (the <c>_id</c>'s or another) already holds the key.</summary>
    public bool IsDuplicateKey => Code == 11000;
}

/// <summary>The credentials were refused, or the server's signature did not prove it knows them.</summary>
public sealed class MongoAuthenticationException(string message, Exception? inner = null) : MongoException(message, inner);
