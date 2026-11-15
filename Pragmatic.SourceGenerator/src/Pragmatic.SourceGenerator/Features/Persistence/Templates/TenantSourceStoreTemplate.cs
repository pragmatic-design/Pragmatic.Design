using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

using static Pragmatic.SourceGenerator.Core.TemplateHelpers;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     The <c>ITenantStore</c> of a <c>[TenantSource]</c> entity, nested in it as <c>{Entity}.TenantStore</c>: a read view of its rows,
///     which the host registers in place of the empty in-memory store (<c>_Metadata.TenantSource.g.cs</c> names its factory).
/// </summary>
/// <remarks>
///     <para>
///         Every application whose tenants are rows wrote this class by hand, the same way. A singleton, as <c>ITenantStore</c> is provided
///         by the host: each read opens a scope of its own for the entity's read repository, which is scoped, rather than capturing one.
///     </para>
///     <para>
///         The writes refuse: a tenant's row is written by the application's own operations, under their permissions, and a store that wrote
///         it would be a second way in that no permission guards.
///     </para>
/// </remarks>
internal sealed class TenantSourceStoreTemplate : CSharpTemplate
{
    /// <summary>The nested type name.</summary>
    public const string ClassName = "TenantStore";

    private const string MultiTenancy = "global::Pragmatic.MultiTenancy";
    private const string Tasks = "global::System.Threading.Tasks";

    private readonly EntityMetadataModel _model;

    public TenantSourceStoreTemplate(EntityMetadataModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[TenantSource] on {_model.TypeName}";

    /// <summary>The factory the host calls, fully qualified: what the metadata names.</summary>
    public static string FactoryOf(EntityMetadataModel model) => $"global::{model.FullTypeName}.{ClassName}.Create";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, ClassName, _model.Namespace),
        ToSourceText());

    protected override bool Validate() => _model.IsValid && !_model.IsFromReference && _model.TenantSource is not null;

    public override void RenderFile()
    {
        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        Class(_model.TypeName, RenderStore,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderStore()
    {
        var entity = $"global::{_model.FullTypeName}";
        var source = _model.TenantSource!;
        var specifications = $"global::{(string.IsNullOrEmpty(_model.Namespace) || _model.Namespace == "<global namespace>" ? "" : _model.Namespace + ".")}{_model.TypeName}Specifications";
        var repository = $"global::Pragmatic.Persistence.Repository.IReadRepository<{entity}>";
        const string ct = "global::System.Threading.CancellationToken ct = default";

        XmlSummary($"The tenants this application serves, read from its <see cref=\"{_model.TypeName}\"/> rows ([TenantSource]).");
        AppendLine($"public sealed class {ClassName}(global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopes) : {MultiTenancy}.IEntityTenantStore");
        Block(() =>
        {
            XmlSummary("The store the host registers: built from the container, which the reads open their scopes from.");
            AppendLine($"public static {MultiTenancy}.ITenantStore Create(global::System.IServiceProvider services)");
            AppendLine($"    => new {ClassName}(global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(services));");
            AppendLine();
            AppendLine($"public global::System.Type EntityType => typeof({entity});");
            AppendLine();

            AppendLine($"public async {Tasks}.Task<{MultiTenancy}.TenantInfo?> GetByIdAsync(string tenantId, {ct})");
            Block(() =>
            {
                AppendLine("await using var scope = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateAsyncScope(scopes);");
                AppendLine($"var row = await global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{repository}>(scope.ServiceProvider)");
                AppendLine($"    .FirstOrDefaultAsync({specifications}.ByTenantSourceId(tenantId), ct).ConfigureAwait(false);");
                AppendLine("return row is null ? null : Describe(row);");
            });
            AppendLine();

            AppendLine($"public async {Tasks}.Task<global::System.Collections.Generic.IReadOnlyList<{MultiTenancy}.TenantInfo>> GetAllAsync({ct})");
            Block(() =>
            {
                AppendLine("await using var scope = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateAsyncScope(scopes);");
                AppendLine($"var rows = await global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{repository}>(scope.ServiceProvider)");
                AppendLine($"    .FindAsync({specifications}.TenantSourceAll(), ct).ConfigureAwait(false);");
                AppendLine($"var tenants = new global::System.Collections.Generic.List<{MultiTenancy}.TenantInfo>(rows.Count);");
                AppendLine("foreach (var row in rows)");
                AppendLine("    tenants.Add(Describe(row));");
                AppendLine("return tenants;");
            });
            AppendLine();

            AppendLine($"public async {Tasks}.Task<global::System.Collections.Generic.IReadOnlyList<{MultiTenancy}.TenantInfo>> GetActiveAsync({ct})");
            Block(() =>
            {
                AppendLine($"var active = new global::System.Collections.Generic.List<{MultiTenancy}.TenantInfo>();");
                AppendLine("foreach (var tenant in await GetAllAsync(ct).ConfigureAwait(false))");
                AppendLine("{");
                AppendLine($"    if (tenant.State == {MultiTenancy}.TenantState.Active)");
                AppendLine("        active.Add(tenant);");
                AppendLine("}");
                AppendLine("return active;");
            });
            AppendLine();

            var refusal = $"\"A {_model.TypeName} becomes a tenant, changes or goes away through the application's own operations, which check their permissions: the tenant store over it ([TenantSource]) only reads.\"";
            AppendLine($"public {Tasks}.Task<{MultiTenancy}.TenantInfo> CreateAsync({MultiTenancy}.TenantInfo tenant, {ct})");
            AppendLine($"    => throw new global::System.NotSupportedException({refusal});");
            AppendLine();
            AppendLine($"public {Tasks}.Task<bool> UpdateAsync({MultiTenancy}.TenantInfo tenant, {ct})");
            AppendLine($"    => throw new global::System.NotSupportedException({refusal});");
            AppendLine();
            AppendLine($"public {Tasks}.Task<bool> DeactivateAsync(string tenantId, {ct})");
            AppendLine($"    => throw new global::System.NotSupportedException({refusal});");
            AppendLine();
            AppendLine($"public {Tasks}.Task<bool> DeleteAsync(string tenantId, {ct})");
            AppendLine($"    => throw new global::System.NotSupportedException({refusal});");
            AppendLine();

            AppendLine($"private static {MultiTenancy}.TenantInfo Describe({entity} row) => new()");
            AppendLine("{");
            AppendLine($"    TenantId = row.{source.Id},");
            AppendLine($"    TenantName = row.{source.Name},");
            AppendLine($"    State = row.{source.State},");
            AppendLine(source.CreatedAt is null
                ? "    CreatedAt = global::System.DateTimeOffset.MinValue,"
                : source.CreatedAtNullable
                    ? $"    CreatedAt = row.{source.CreatedAt} ?? global::System.DateTimeOffset.MinValue,"
                    : $"    CreatedAt = row.{source.CreatedAt},");
            AppendLine("};");
        });
    }
}
