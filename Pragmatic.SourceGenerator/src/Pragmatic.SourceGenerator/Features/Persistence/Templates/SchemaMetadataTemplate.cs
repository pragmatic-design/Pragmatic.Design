using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates _Infra.Persistence.SchemaMetadata.g.cs with a static SchemaVersion
///     containing the desired database schema at compile time.
/// </summary>
internal sealed class SchemaMetadataTemplate : CSharpTemplate
{
    private readonly SchemaMetadataModel _model;

    public SchemaMetadataTemplate(SchemaMetadataModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";

    public override Artifact RenderOutput()
    {
        var hintName = _model.DatabaseName == "Default"
            ? VirtualFolderHints.ForAssembly("Persistence", "SchemaMetadata")
            : VirtualFolderHints.ForAssembly("Persistence", $"SchemaMetadata.{_model.DatabaseName}");
        return new Artifact(hintName, ToSourceText());
    }

    protected override bool Validate() => _model.Tables.Length > 0;

    public override void RenderFile()
    {
        AddUsing("System.Collections.Immutable");
        AddUsing("Pragmatic.Migrations.Schema");

        AppendNamespace(_model.Namespace);
        AppendLine();

        var className = $"{_model.DatabaseName}Schema";
        XmlSummary($"SG-generated schema metadata for database {_model.DatabaseName}. Used by Pragmatic Migrations diff engine.");

        Class(className, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderBody()
    {
        AppendLine($"public static readonly SchemaVersion Current = new(");
        IncreaseIndent();
        AppendLine("[");
        IncreaseIndent();

        for (var i = 0; i < _model.Tables.Length; i++)
        {
            RenderTable(_model.Tables[i]);
            if (i < _model.Tables.Length - 1)
                AppendLine(",");
        }

        DecreaseIndent();
        AppendLine("],");
        var providerName = _model.Provider switch
        {
            Core.EfCoreProvider.PostgreSql => "PostgreSql",
            Core.EfCoreProvider.SqlServer => "SqlServer",
            Core.EfCoreProvider.Sqlite => "Sqlite",
            _ => "Generic"
        };
        AppendLine($"DatabaseName: \"{_model.DatabaseName}\",");
        var configKeyArg = _model.ConfigKey is not null ? $"\"{_model.ConfigKey}\"" : "null";
        AppendLine($"ProviderName: \"{providerName}\",");
        AppendLine($"ConfigKey: {configKeyArg});");
        DecreaseIndent();
    }

    private void RenderTable(TableSchemaModel table)
    {
        var schema = table.SchemaName is not null ? $"\"{table.SchemaName}\"" : "null";
        AppendLine($"new TableSchema(\"{table.TableName}\", {schema},");
        IncreaseIndent();

        // Columns
        AppendLine("[");
        IncreaseIndent();
        for (var i = 0; i < table.Columns.Length; i++)
        {
            var col = table.Columns[i];
            var pk = col.IsPrimaryKey ? "true" : "false";
            var nullable = col.IsNullable ? "true" : "false";
            var optionals = "";
            if (col.DefaultValue is not null)
                optionals += $", \"{EscapeString(col.DefaultValue)}\"";
            else if (col.RenamedFrom is not null)
                optionals += ", null"; // DefaultValue placeholder
            if (col.RenamedFrom is not null)
                optionals += $", \"{EscapeString(col.RenamedFrom)}\"";
            var suffix = i < table.Columns.Length - 1 ? "," : "";
            AppendLine($"new ColumnSchema(\"{col.Name}\", \"{col.SqlType}\", {nullable}, {pk}{optionals}){suffix}");
        }
        DecreaseIndent();
        AppendLine("],");

        // Indexes
        AppendLine("[");
        IncreaseIndent();
        for (var i = 0; i < table.Indexes.Length; i++)
        {
            var idx = table.Indexes[i];
            var colsStr = string.Join(", ", idx.Columns.Select(c => $"\"{c}\""));
            var unique = idx.IsUnique ? ", true" : "";
            var filter = idx.Filter is not null ? $", \"{EscapeString(idx.Filter)}\"" : "";
            var suffix = i < table.Indexes.Length - 1 ? "," : "";
            AppendLine($"new IndexSchema(\"{idx.Name}\", ImmutableArray.Create({colsStr}){unique}{filter}){suffix}");
        }
        DecreaseIndent();
        AppendLine("],");

        // Foreign keys
        AppendLine("[");
        IncreaseIndent();
        for (var i = 0; i < table.ForeignKeys.Length; i++)
        {
            var fk = table.ForeignKeys[i];
            var suffix = i < table.ForeignKeys.Length - 1 ? "," : "";
            // OnDelete is a ReferentialAction enum (Pragmatic.Migrations.Schema). The model carries
            // the EF DeleteBehavior name ("NoAction"/"Cascade"/"SetNull"/"Restrict") which maps 1:1.
            var onDelete = MapOnDelete(fk.OnDelete);
            AppendLine($"new ForeignKeySchema(\"{fk.Name}\", \"{fk.Column}\", \"{fk.ReferencedTable}\", \"{fk.ReferencedColumn}\", ReferentialAction.{onDelete}){suffix}");
        }
        DecreaseIndent();
        AppendLine("],");

        // Check constraints
        AppendLine("[");
        IncreaseIndent();
        var checks = table.CheckConstraints.IsDefaultOrEmpty
            ? ImmutableArray<CheckConstraintSchemaModel>.Empty
            : table.CheckConstraints.AsImmutableArray();
        for (var i = 0; i < checks.Length; i++)
        {
            var check = checks[i];
            var suffix = i < checks.Length - 1 ? "," : "";
            AppendLine($"new CheckConstraintSchema(\"{check.Name}\", \"{EscapeString(check.Expression)}\"){suffix}");
        }
        DecreaseIndent();
        AppendLine("])");

        DecreaseIndent();
    }


    private static string EscapeString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>Maps an EF DeleteBehavior name to a ReferentialAction enum member name.</summary>
    private static string MapOnDelete(string onDelete) => onDelete switch
    {
        "Cascade" => "Cascade",
        "SetNull" => "SetNull",
        "Restrict" => "Restrict",
        _ => "NoAction"
    };
}
