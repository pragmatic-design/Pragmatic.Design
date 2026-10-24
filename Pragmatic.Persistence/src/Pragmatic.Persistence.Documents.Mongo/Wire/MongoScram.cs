using System.Security.Cryptography;
using System.Text;
using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Mongo.Wire;

/// <summary>
///     SCRAM-SHA-256 (RFC 5802, RFC 7677) through <c>saslStart</c> / <c>saslContinue</c>: the client proves it knows the password without
///     sending it, and checks the server's signature (a server that does not know the password cannot forge it). The password goes through
///     SASLprep as NFKC normalization (the mapping and the prohibited-character checks of RFC 4013 are not applied: an ASCII password, the
///     common case, is unchanged by all of them).
/// </summary>
internal static class MongoScram
{
    private const string Mechanism = "SCRAM-SHA-256";

    /// <summary>The exchange on <paramref name="connection" />'s asynchronous calls, or its synchronous ones (<paramref name="useAsync" />, as <c>MongoConnection</c>).</summary>
    public static async ValueTask AuthenticateCoreAsync(MongoConnection connection, string user, string password, string source, bool useAsync, CancellationToken ct)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var clientFirstBare = "n=" + Escape(user) + ",r=" + nonce;
        BsonDocument reply;
        try
        {
            reply = await connection.CommandCoreAsync(SaslStart(clientFirstBare, source), useAsync, ct).ConfigureAwait(false);
        }
        catch (MongoCommandException e)
        {
            throw new MongoAuthenticationException($"{Mechanism} for '{user}' on '{source}' was refused: {e.Message}", e);
        }

        reply.TryGetNumber("conversationId", out var conversation);
        var serverFirst = Payload(reply);
        var fields = Fields(serverFirst);
        if (!fields.TryGetValue('r', out var combinedNonce) || !combinedNonce.StartsWith(nonce, StringComparison.Ordinal))
            throw new MongoAuthenticationException("The server's nonce does not extend the client's.");
        if (!fields.TryGetValue('s', out var salt) || !fields.TryGetValue('i', out var iterationText) || !int.TryParse(iterationText, out var iterations) || iterations < 4096)
            throw new MongoAuthenticationException($"The server's first message is not SCRAM's: '{serverFirst}'.");

        var salted = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(Prepare(password)), Convert.FromBase64String(salt), iterations, HashAlgorithmName.SHA256, 32);
        var clientKey = HMACSHA256.HashData(salted, "Client Key"u8);
        var storedKey = SHA256.HashData(clientKey);
        var withoutProof = "c=biws,r=" + combinedNonce; // biws: base64 of "n,,", the GS2 header (no channel binding)
        var authMessage = Encoding.UTF8.GetBytes(clientFirstBare + "," + serverFirst + "," + withoutProof);
        var clientSignature = HMACSHA256.HashData(storedKey, authMessage);
        var proof = new byte[clientKey.Length];
        for (var i = 0; i < proof.Length; i++)
            proof[i] = (byte)(clientKey[i] ^ clientSignature[i]);
        var serverKey = HMACSHA256.HashData(salted, "Server Key"u8);
        var expectedServerSignature = Convert.ToBase64String(HMACSHA256.HashData(serverKey, authMessage));

        try
        {
            reply = await connection.CommandCoreAsync(SaslContinue((int)conversation, withoutProof + ",p=" + Convert.ToBase64String(proof), source), useAsync, ct).ConfigureAwait(false);
        }
        catch (MongoCommandException e)
        {
            throw new MongoAuthenticationException($"{Mechanism} for '{user}' on '{source}' was refused: {e.Message}", e);
        }

        var serverFinal = Fields(Payload(reply));
        if (serverFinal.TryGetValue('e', out var error))
            throw new MongoAuthenticationException($"The server refused the proof: {error}.");
        if (!serverFinal.TryGetValue('v', out var serverSignature) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(serverSignature), Encoding.ASCII.GetBytes(expectedServerSignature)))
            throw new MongoAuthenticationException("The server's signature does not match: it does not know the password, or the exchange was tampered with.");
        // skipEmptyExchange: the server ends the conversation here; an older one asks for one more empty round.
        while (!(reply.TryGetBoolean("done", out var done) && done))
            reply = await connection.CommandCoreAsync(SaslContinue((int)conversation, "", source), useAsync, ct).ConfigureAwait(false);
    }

    private static ReadOnlyMemory<byte> SaslStart(string clientFirstBare, string source)
    {
        var writer = new BsonWriter();
        writer.StartDocument();
        writer.Write("saslStart", 1);
        writer.Write("mechanism", Mechanism);
        writer.Write("payload", Encoding.UTF8.GetBytes("n,," + clientFirstBare));
        writer.StartDocument("options");
        writer.Write("skipEmptyExchange", true);
        writer.EndDocument();
        writer.Write("$db", source);
        writer.EndDocument();
        return writer.Written;
    }

    private static ReadOnlyMemory<byte> SaslContinue(int conversation, string payload, string source)
    {
        var writer = new BsonWriter();
        writer.StartDocument();
        writer.Write("saslContinue", 1);
        writer.Write("conversationId", conversation);
        writer.Write("payload", Encoding.UTF8.GetBytes(payload));
        writer.Write("$db", source);
        writer.EndDocument();
        return writer.Written;
    }

    private static string Payload(BsonDocument reply)
        => reply.TryGetBinary("payload", out var payload) ? Encoding.UTF8.GetString(payload.Span) : throw new MongoAuthenticationException("The server's SASL reply has no payload.");

    private static Dictionary<char, string> Fields(string message)
    {
        var fields = new Dictionary<char, string>();
        foreach (var part in message.Split(','))
        {
            if (part.Length >= 2 && part[1] == '=')
                fields[part[0]] = part[2..];
        }

        return fields;
    }

    /// <summary>A SCRAM user name: ',' and '=' escaped as =2C and =3D.</summary>
    private static string Escape(string user) => user.Replace("=", "=3D", StringComparison.Ordinal).Replace(",", "=2C", StringComparison.Ordinal);

    private static string Prepare(string password) => Ascii.IsValid(password) ? password : password.Normalize(NormalizationForm.FormKC);
}
