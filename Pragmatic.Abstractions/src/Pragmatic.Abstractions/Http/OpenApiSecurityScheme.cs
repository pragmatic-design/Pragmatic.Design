namespace Pragmatic.Abstractions.Http;

/// <summary>
///     How callers authenticate, as the published contract describes it.
/// </summary>
/// <remarks>
///     <para>
///         Mirrors the OpenAPI Security Scheme Object, minus what this framework has no way to
///         produce. The names match the specification deliberately: this record is serialised
///         straight into <c>components.securitySchemes</c>, and a property whose name differs from
///         the specification is one more place where the document can drift from what it must say.
///     </para>
///     <para>
///         ⚠️ Written by whoever introduces an authentication, not by whoever writes the document.
///         The generator once picked the scheme itself, from a list of the ways to authenticate it
///         happened to know: correct for the three that register <c>JwtBearer</c>, wrong for the
///         development handler, and wrong for anything added later. A list like that ages in silence
///         — a new way arrives, nobody edits the mapping, and the document is wrong with nothing
///         turning red.
///     </para>
/// </remarks>
public sealed record OpenApiSecurityScheme
{
    /// <summary>The key under <c>components.securitySchemes</c>, and the name operations reference.</summary>
    public required string Name { get; init; }

    /// <summary>The OpenAPI type: <c>http</c>, <c>apiKey</c>, <c>oauth2</c>, <c>openIdConnect</c>, <c>mutualTLS</c>.</summary>
    public required string Type { get; init; }

    /// <summary>For <c>http</c>: the HTTP scheme, such as <c>bearer</c> or <c>basic</c>.</summary>
    public string? Scheme { get; init; }

    /// <summary>For <c>http</c>/<c>bearer</c>: a hint at the token format, conventionally <c>JWT</c>.</summary>
    public string? BearerFormat { get; init; }

    /// <summary>For <c>apiKey</c>: the name of the header, query parameter or cookie carrying it.</summary>
    public string? ParameterName { get; init; }

    /// <summary>For <c>apiKey</c>: where it travels — <c>header</c>, <c>query</c> or <c>cookie</c>.</summary>
    public string? In { get; init; }

    /// <summary>For <c>openIdConnect</c>: the discovery document.</summary>
    public string? OpenIdConnectUrl { get; init; }

    /// <summary>What a person reading the document should know about it.</summary>
    public string? Description { get; init; }

    /// <summary>The bearer token scheme the three JWT entry points of this framework set up.</summary>
    /// <param name="name">The key it is published under; the default matches the usual convention.</param>
    /// <param name="description">What a reader should know, if anything beyond the obvious.</param>
    public static OpenApiSecurityScheme Bearer(string name = "bearerAuth", string? description = null)
        => new()
        {
            Name = name,
            Type = "http",
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = description,
        };
}
