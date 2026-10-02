// Pragmatic.SourceGenerator - Composition - Pragmatic Host Template (Remote Boundaries)

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Remote boundary registration: named HttpClient per remote module,
///     IHttpContextAccessor for header propagation.
/// </summary>
internal sealed partial class PragmaticHostTemplate
{
    private void RenderRemoteBoundaryHttpClients()
    {
        Comment("Remote boundary HTTP clients");
        AppendLine("services.AddHttpContextAccessor();");
        // Identity propagation handler — forwards the caller's Authorization header onto
        // each outbound /_pragmatic/invoke call so the remote host authenticates the originator.
        AppendLine("services.AddTransient<global::Pragmatic.Composition.Remote.PragmaticRemoteAuthHandler>();");
        // Range propagation: without it the remote boundary never sees the end user's Range and reads
        // the whole object out of storage to serve a slice of it.
        AppendLine("services.AddTransient<global::Pragmatic.Composition.Remote.PragmaticRemoteRangeHandler>();");

        foreach (var rb in _model.RemoteBoundaries.OrderBy(r => r.ModuleName))
        {
            var clientName = $"Pragmatic.Remote.{rb.ModuleName}";
            var configPath = $"Pragmatic:RemoteBoundaries:{rb.ModuleName}:BaseUrl";

            AppendLine();
            Comment($"Remote boundary: {rb.ModuleName}");

            if (rb.BaseUrl is not null)
            {
                // Hardcoded base URL from attribute
                AppendLine($"services.AddHttpClient(\"{clientName}\", client =>");
                AppendLine("{");
                IncreaseIndent();
                AppendLine($"client.BaseAddress = new System.Uri(\"{rb.BaseUrl}\");");
                DecreaseIndent();
                AppendLine("})");
                IncreaseIndent();
                AppendLine(".AddHttpMessageHandler<global::Pragmatic.Composition.Remote.PragmaticRemoteAuthHandler>()");
                AppendLine(".AddHttpMessageHandler<global::Pragmatic.Composition.Remote.PragmaticRemoteRangeHandler>();");
                DecreaseIndent();
            }
            else
            {
                // URL from configuration
                AppendLine($"services.AddHttpClient(\"{clientName}\", (sp, client) =>");
                AppendLine("{");
                IncreaseIndent();
                AppendLine($"var config = sp.GetRequiredService<IConfiguration>();");
                AppendLine($"var baseUrl = config[\"{configPath}\"];");
                AppendLine($"if (!string.IsNullOrEmpty(baseUrl))");
                Block(() => AppendLine("client.BaseAddress = new System.Uri(baseUrl);"));
                DecreaseIndent();
                AppendLine("})");
                IncreaseIndent();
                AppendLine(".AddHttpMessageHandler<global::Pragmatic.Composition.Remote.PragmaticRemoteAuthHandler>()");
                AppendLine(".AddHttpMessageHandler<global::Pragmatic.Composition.Remote.PragmaticRemoteRangeHandler>();");
                DecreaseIndent();
            }
        }

        AppendLine();
    }
}
