namespace Pragmatic.Configuration.Aws;

/// <summary>
///     Maps Pragmatic logical keys (colon-separated, e.g. <c>Booking:Cancel</c>) to AWS entry names, which use
///     <c>/</c> hierarchy. Applies the optional path prefix and tenant nesting, and reverses the mapping for
///     Parameter Store section queries.
/// </summary>
internal static class AwsNaming
{
    /// <summary>The path segments before the logical key, without leading or trailing slash (may be empty).</summary>
    private static string BasePath(string? prefix, string? tenantId)
    {
        var segments = new List<string>();
        if (!string.IsNullOrEmpty(prefix))
            segments.Add(prefix!.Trim('/'));
        if (tenantId is not null)
        {
            segments.Add("tenants");
            segments.Add(tenantId);
        }

        return string.Join("/", segments);
    }

    /// <summary>Secrets Manager name (no leading slash): <c>[base/]key</c> with <c>:</c> → <c>/</c>.</summary>
    public static string SecretName(string? prefix, string? tenantId, string key)
    {
        var basePath = BasePath(prefix, tenantId);
        var leaf = key.Replace(':', '/');
        return basePath.Length == 0 ? leaf : $"{basePath}/{leaf}";
    }

    /// <summary>SSM parameter name (leading slash required): <c>/[base/]key</c> with <c>:</c> → <c>/</c>.</summary>
    public static string ParameterName(string? prefix, string? tenantId, string key)
        => "/" + SecretName(prefix, tenantId, key);

    /// <summary>SSM path for a section query (leading slash, no trailing slash).</summary>
    public static string SectionPath(string? prefix, string? tenantId, string sectionPrefix)
    {
        var basePath = BasePath(prefix, tenantId);
        var section = sectionPrefix.Replace(':', '/').Trim('/');
        var combined = basePath.Length == 0 ? section : $"{basePath}/{section}";
        return "/" + combined.TrimEnd('/');
    }

    /// <summary>Reverses <see cref="ParameterName" />: full SSM name → logical colon-separated key.</summary>
    public static string ToLogicalKey(string parameterName, string? prefix, string? tenantId)
    {
        var name = parameterName.TrimStart('/');
        var basePath = BasePath(prefix, tenantId);
        if (basePath.Length > 0 && name.StartsWith(basePath + "/", StringComparison.Ordinal))
            name = name[(basePath.Length + 1)..];

        return name.Replace('/', ':');
    }
}
