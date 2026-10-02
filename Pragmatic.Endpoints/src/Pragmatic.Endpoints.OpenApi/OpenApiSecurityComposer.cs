using System.Text;
using Pragmatic.Abstractions.Http;

namespace Pragmatic.Endpoints.OpenApi;

/// <summary>
///     Puts the registered security schemes into the compile-time document.
/// </summary>
/// <remarks>
///     <para>
///         The two halves of the answer are known in two places. <b>Which</b> operations require
///         authentication is settled at compile time — <c>[AllowAnonymous]</c>, and the empty
///         <c>security</c> array each anonymous operation already publishes. <b>What</b> the
///         requirement looks like on the wire is chosen in <c>Program.cs</c>, by the call that sets
///         the authentication up, and no generator can see it.
///     </para>
///     <para>
///         So the generator does not guess, and this composes. A guess of bearer/JWT would be correct
///         for the three entry points that register <c>JwtBearer</c> and wrong for the development
///         handler and for anything added later. A list of the known ways to authenticate ages in
///         silence — one more arrives, nobody edits the list, and the document is wrong with nothing
///         turning red.
///     </para>
///     <para>
///         ⚠️ Composed by insertion, not by parsing and re-serialising. Property order carries no
///         meaning in JSON, so both pieces go in immediately after an opening brace this generator
///         writes: no allocation of a parsed tree per request, nothing to keep AOT-safe, and the
///         document that ships is byte-for-byte the generated one plus two objects. Done once and
///         held, because the answer cannot change while the process lives.
///     </para>
/// </remarks>
internal static class OpenApiSecurityComposer
{
    private const string ComponentsAnchor = "\"components\": {";

    /// <summary>
    ///     Returns the document with the schemes declared and required, or unchanged when there is
    ///     nothing to declare.
    /// </summary>
    /// <param name="json">The generated document.</param>
    /// <param name="schemes">What the registered contributors describe.</param>
    public static string Compose(string json, IReadOnlyList<OpenApiSecurityScheme> schemes)
    {
        if (schemes.Count == 0)
            return json;

        // The anchor is written by our own generator, so it is here — but a document from anywhere
        // else must come through unharmed rather than half-edited.
        var anchor = json.IndexOf(ComponentsAnchor, StringComparison.Ordinal);
        if (anchor < 0 || json.Length == 0 || json[0] != '{')
            return json;

        var withSchemes = json.Insert(anchor + ComponentsAnchor.Length, RenderSchemes(schemes));

        return withSchemes.Insert(1, RenderRequirement(schemes));
    }

    /// <summary>The declaration: one entry per contributor, under <c>components.securitySchemes</c>.</summary>
    private static string RenderSchemes(IReadOnlyList<OpenApiSecurityScheme> schemes)
    {
        var b = new StringBuilder("\n    \"securitySchemes\": {");

        for (var i = 0; i < schemes.Count; i++)
        {
            var s = schemes[i];
            b.Append(i == 0 ? "\n      " : ",\n      ");
            b.Append(Quote(s.Name)).Append(": {");
            b.Append("\n        \"type\": ").Append(Quote(s.Type));
            Append(b, "scheme", s.Scheme);
            Append(b, "bearerFormat", s.BearerFormat);
            Append(b, "name", s.ParameterName);
            Append(b, "in", s.In);
            Append(b, "openIdConnectUrl", s.OpenIdConnectUrl);
            Append(b, "description", s.Description);
            b.Append("\n      }");
        }

        return b.Append("\n    },").ToString();
    }

    /// <summary>
    ///     The requirement, at document level: it applies to every operation that does not opt out
    ///     with the empty array the generator already wrote on the anonymous ones.
    /// </summary>
    private static string RenderRequirement(IReadOnlyList<OpenApiSecurityScheme> schemes)
    {
        var b = new StringBuilder("\n  \"security\": [\n    {");

        for (var i = 0; i < schemes.Count; i++)
            b.Append(i == 0 ? "\n      " : ",\n      ").Append(Quote(schemes[i].Name)).Append(": []");

        return b.Append("\n    }\n  ],").ToString();
    }

    private static void Append(StringBuilder b, string property, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            b.Append(",\n        ").Append(Quote(property)).Append(": ").Append(Quote(value!));
    }

    /// <summary>
    ///     A JSON string literal. Hand-written because the alternative pulls a serialiser in to quote
    ///     six short values, and these come from a scheme name and a description.
    /// </summary>
    private static string Quote(string value)
    {
        var b = new StringBuilder(value.Length + 2).Append('"');

        foreach (var ch in value)
            switch (ch)
            {
                case '"': b.Append("\\\""); break;
                case '\\': b.Append("\\\\"); break;
                case '\n': b.Append("\\n"); break;
                case '\r': b.Append("\\r"); break;
                case '\t': b.Append("\\t"); break;
                default:
                    if (ch < ' ')
                        b.Append("\\u").Append(((int)ch).ToString("x4"));
                    else
                        b.Append(ch);
                    break;
            }

        return b.Append('"').ToString();
    }
}
