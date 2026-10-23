using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Templates;

/// <summary>
///     The members Pragmatic's generator declares on an entity when EF Core is referenced, declared here when it does not: member for member as
///     <c>EntityTraitsTemplate</c>, <c>OwnershipPropertyTemplate</c>, <c>ScopedPropertyTemplate</c>, <c>EntityRelationsTemplate</c>,
///     <c>EntitySettersTemplate</c> and the filter templates write them.
/// </summary>
internal sealed class EntityShapeTemplate(EntityShapeModel model) : CSharpTemplate
{
    private const string Filters = "global::Pragmatic.Persistence.Query.Filters.";
    private const string Json = "[global::System.Text.Json.Serialization.JsonIgnore]";

    protected override string? GeneratorName => "Pragmatic.Persistence.Sql";

    protected override string? SourceInfo => $"the members Pragmatic's generator declares on {model.EntityName} when EF Core is referenced";

    public override Artifact RenderOutput() => new(model.Hint, ToSourceText());

    public override void RenderFile()
    {
        if (model.Namespace is not null)
            AppendLine($"namespace {model.Namespace};");
        AppendLine();
        AppendLine($"partial class {model.EntityName}{(model.Interfaces.Count == 0 ? "" : " : " + string.Join(", ", model.Interfaces))}");
        AppendLine("{");
        foreach (var member in model.Members)
            RenderMember(member);
        foreach (var filter in model.Filters)
            RenderFilter(filter);
        AppendLine("}");
    }

    private void RenderMember(ShapeMemberModel m)
    {
        switch (m.Kind)
        {
            case ShapeMemberKind.PersistenceId:
                AppendLine("    public global::System.Guid PersistenceId { get; set; } = global::System.Guid.CreateVersion7();");
                break;
            case ShapeMemberKind.Id:
                AppendLine("    public global::System.Guid Id => PersistenceId;");
                break;
            case ShapeMemberKind.IsDeleted:
                AppendLine("    public bool IsDeleted { get; set; }");
                break;
            case ShapeMemberKind.DeletedAt:
                AppendLine("    public global::System.DateTimeOffset? DeletedAt { get; set; }");
                break;
            case ShapeMemberKind.DeletedBy:
                AppendLine("    public string? DeletedBy { get; set; }");
                break;
            case ShapeMemberKind.CreatedAt:
                AppendLine("    public global::System.DateTimeOffset CreatedAt { get; set; }");
                break;
            case ShapeMemberKind.CreatedBy:
                AppendLine("    public string? CreatedBy { get; set; }");
                break;
            case ShapeMemberKind.UpdatedAt:
                AppendLine("    public global::System.DateTimeOffset? UpdatedAt { get; set; }");
                break;
            case ShapeMemberKind.UpdatedBy:
                AppendLine("    public string? UpdatedBy { get; set; }");
                break;
            case ShapeMemberKind.Owner:
                AppendLine("    public string OwnerId { get; private set; } = string.Empty;");
                AppendLine("    internal void SetOwnerId(string ownerId) => OwnerId = ownerId;");
                AppendLine("    void global::Pragmatic.Persistence.Entity.IOwnershipAssignable.AssignOwner(string ownerId) => OwnerId = ownerId;");
                break;
            case ShapeMemberKind.AccessScopes:
                AppendLine("    public global::System.Collections.Generic.List<string> AccessScopes { get; private set; } = [];");
                AppendLine("    internal void GrantScope(string scope) { if (!AccessScopes.Contains(scope)) AccessScopes.Add(scope); }");
                AppendLine("    internal void RevokeScope(string scope) => AccessScopes.Remove(scope);");
                break;
            case ShapeMemberKind.RowVersion:
                // [ConcurrencyAware] (owner's decision of 2026-10-22): a ulong, wide enough for SQL Server's rowversion as for xmin.
                AppendLine("    public ulong RowVersion { get; internal set; }");
                break;
            case ShapeMemberKind.ForeignKey:
                AppendLine($"    public {m.Type} {m.Name} {{ get; private set; }}");
                break;
            case ShapeMemberKind.ForeignKeySetter:
            {
                var key = m.Name.Substring("Set".Length);
                AppendLine($"    internal void {m.Name}({m.Type} value) => {key} = value;");
                break;
            }

            case ShapeMemberKind.Reference:
                AppendLine(m.Required ? $"    public {m.Type} {m.Name} {{ get; set; }} = null!;" : $"    public {m.Type}? {m.Name} {{ get; set; }}");
                break;
            case ShapeMemberKind.Collection:
                if (m.JoinTable is not null)
                    AppendLine($"    [global::Pragmatic.Persistence.Sql.ManyToMany(\"{m.JoinTable}\", \"{m.ThisKey}\", \"{m.OtherKey}\")]");
                AppendLine($"    public global::System.Collections.Generic.ICollection<{m.Type}> {m.Name} {{ get; set; }} = [];");
                break;
            case ShapeMemberKind.ChangeTracking:
                AppendLine("    private readonly global::System.Collections.Generic.HashSet<string> _modifiedProperties = [];");
                AppendLine("    private readonly global::System.Collections.Generic.HashSet<string> _collectionsModified = [];");
                AppendLine($"    {Json} public global::System.Collections.Generic.IReadOnlySet<string> ModifiedProperties => _modifiedProperties;");
                AppendLine($"    {Json} public global::System.Collections.Generic.IReadOnlySet<string> CollectionsModified => _collectionsModified;");
                AppendLine("    public void ResetModifiedProperties() { _modifiedProperties.Clear(); _collectionsModified.Clear(); }");
                AppendLine($"    {Json} [global::System.ComponentModel.DataAnnotations.Schema.NotMapped] public bool IsNew {{ get; set; }}");
                break;
            case ShapeMemberKind.TrackedSetter:
            {
                var record = m.Required ? $" _modifiedProperties.Add(nameof({m.Name}));" : "";
                AppendLine($"    internal void Set{m.Name}({m.Type} value) {{ if (global::System.Collections.Generic.EqualityComparer<{m.Type}>.Default.Equals({m.Name}, value)) return; {m.Name} = value;{record} }}");
                break;
            }
        }
    }

    private void RenderFilter(ShapeFilterModel f)
    {
        AppendLine($"    public sealed class {f.Name}{(f.Parameters.Length == 0 ? "" : $"({f.Parameters})")} : {f.Bases}");
        AppendLine("    {");
        if (f.Bypass is not null)
            AppendLine($"        public string BypassPermission => \"{f.Bypass}\";");
        AppendLine($"        public int Priority => {f.Priority};");
        AppendLine($"        public {Filters}FilterScope Scope => {Filters}FilterScope.{f.Scope};");
        AppendLine($"        public global::System.Linq.Expressions.Expression<global::System.Func<{model.EntityType}, bool>> GetFilter() {f.GetFilterBody}");
        AppendLine("    }");
        if (f.IncludeHistory)
            AppendLine($"    public static global::System.IDisposable IncludeHistory({Filters}IQueryFilterToggle filters) => filters.Disable<{model.EntityType}.TemporalFilter>();");
    }
}
