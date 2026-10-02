using System.Text;

namespace Pragmatic.Configuration.Gcp;

/// <summary>
///     Builds GCP Secret Manager secret ids from Pragmatic logical keys. Secret ids allow only
///     <c>[A-Za-z0-9_-]</c> and are flat (no hierarchy), so the prefix, optional tenant, and key are joined
///     with <c>-</c> and every other character (including the Pragmatic <c>:</c> separator) is replaced by
///     <c>-</c>.
/// </summary>
internal static class GcpSecretName
{
    public static string Build(string? prefix, string? tenantId, string key)
    {
        var segments = new List<string>();
        if (!string.IsNullOrEmpty(prefix))
            segments.Add(prefix!);
        if (tenantId is not null)
        {
            segments.Add("tenants");
            segments.Add(tenantId);
        }

        segments.Add(key);
        return Sanitize(string.Join("-", segments));
    }

    private static string Sanitize(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '-');
        return sb.ToString();
    }
}
