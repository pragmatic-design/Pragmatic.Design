// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>The name a property carries on the wire when nothing renames it.</summary>
/// <remarks>
///     <para>
///         <c>JsonNamingPolicy.CamelCase</c>, reproduced, because that is the host's policy and what the reflection
///         resolver applies to every type no generated context covers. Anything a generator emits a JSON name into —
///         the module's context, the client's context, a response writer — has to produce the same name, or a type
///         is written one way by reflection and another way once a context covers it (or the client reads the
///         server's document under names the server never writes).
///     </para>
///     <para>
///         ⚠️ Not "lower the first letter": the two differ on every name that starts with an acronym (<c>URL</c> is
///         <c>url</c>, <c>IOStream</c> is <c>ioStream</c>). <c>WhatAResponseWriterCoversTests</c> compares this with
///         the policy itself.
///     </para>
/// </remarks>
internal static class JsonWireNames
{
    /// <summary>
    ///     <c>JsonNamingPolicy.CamelCase</c>: the leading run of capitals is lowered, except the last of a run that a
    ///     lower-case letter follows (<c>URLPath</c> → <c>urlPath</c>).
    /// </summary>
    public static string CamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0]))
            return name;

        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (i == 1 && !char.IsUpper(chars[i]))
                break;

            var hasNext = i + 1 < chars.Length;
            if (i > 0 && hasNext && !char.IsUpper(chars[i + 1]))
            {
                if (chars[i + 1] == ' ')
                    chars[i] = char.ToLowerInvariant(chars[i]);
                break;
            }

            chars[i] = char.ToLowerInvariant(chars[i]);
        }

        return new string(chars);
    }
}
