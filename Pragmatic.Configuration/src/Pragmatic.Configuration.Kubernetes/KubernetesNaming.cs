using System.Text;

namespace Pragmatic.Configuration.Kubernetes;

/// <summary>
///     Maps Pragmatic logical keys to Kubernetes object/data names. A tenant gets its own object
///     (<c>{name}-{tenant}</c>); within an object, data keys allow only <c>[-._A-Za-z0-9]</c>, so the
///     Pragmatic <c>:</c> separator is encoded as <c>__</c> (reversible for section reads).
/// </summary>
internal static class KubernetesNaming
{
    /// <summary>The ConfigMap/Secret object name for the given tenant (or the base object when null).</summary>
    public static string ObjectName(string baseName, string? tenantId)
        => tenantId is null ? baseName : SanitizeObjectName($"{baseName}-{tenantId}");

    /// <summary>Encodes a logical key as a valid data key (<c>:</c> → <c>__</c>, other invalid chars → <c>.</c>).</summary>
    public static string DataKey(string logicalKey)
    {
        var sb = new StringBuilder(logicalKey.Length);
        foreach (var c in logicalKey.Replace(":", "__", StringComparison.Ordinal))
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' ? c : '.');
        return sb.ToString();
    }

    /// <summary>Reverses <see cref="DataKey" /> for section results (<c>__</c> → <c>:</c>).</summary>
    public static string LogicalKey(string dataKey) => dataKey.Replace("__", ":", StringComparison.Ordinal);

    private static string SanitizeObjectName(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw.ToLowerInvariant())
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '.' ? c : '-');
        return sb.ToString();
    }
}
