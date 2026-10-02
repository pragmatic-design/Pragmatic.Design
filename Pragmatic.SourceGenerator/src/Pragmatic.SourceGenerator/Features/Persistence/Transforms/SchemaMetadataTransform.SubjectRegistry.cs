using System.Collections.Immutable;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     The subject registry's tables, emitted into the database that holds a <c>[DataSubject]</c>.
/// </summary>
/// <remarks>
///     Mirrors <c>Pragmatic.Privacy.EFCore</c>'s entity configurations, the dual source the audit trail's
///     tables live with too. Without them the generated context mapped the tables and the migration never
///     created them, so the registry failed on its first query.
///     <c>SubjectRegistrySchemaMatchesEfModelTests</c> holds the two in step.
/// </remarks>
internal static partial class SchemaMetadataTransform
{
    /// <summary>Each subject's reference, its blind index and its encrypted identity.</summary>
    /// <remarks>
    ///     Timestamps are ticks, as in the EF configuration: several providers cannot translate a
    ///     <c>DateTimeOffset</c> comparison.
    /// </remarks>
    private static TableSchemaModel BuildSubjectsTable(EfCoreProvider provider)
    {
        var schemaName = provider switch
        {
            EfCoreProvider.PostgreSql => "public",
            EfCoreProvider.SqlServer => "dbo",
            _ => null
        };

        string Str(int len) => SqlTypeMapper.MapToSqlType("System.String", provider, len, null, null, false);
        var ticks = SqlTypeMapper.MapToSqlType("System.Int64", provider);
        var bytes = SqlTypeMapper.MapToSqlType("System.Byte[]", provider);

        var columns = ImmutableArray.Create(
            new ColumnSchemaModel("SubjectRef", Str(64), false, true),
            new ColumnSchemaModel("SubjectType", Str(128), false, false),
            new ColumnSchemaModel("LookupIndex", bytes, true, false),
            new ColumnSchemaModel("Identifier", bytes, true, false),
            new ColumnSchemaModel("CreatedAt", ticks, false, false),
            new ColumnSchemaModel("ForgottenAt", ticks, true, false));

        // Unique: two live subjects sharing a blind index would make a reference ambiguous. An erased row
        // keeps a null index, which a unique index does not compare.
        var indexes = ImmutableArray.Create(
            new IndexSchemaModel("IX___Subjects_LookupIndex", ImmutableArray.Create("LookupIndex"), IsUnique: true));

        return new TableSchemaModel("__Subjects", schemaName, null, columns, indexes,
            ImmutableArray<ForeignKeySchemaModel>.Empty);
    }

    /// <summary>Consent records, withdrawn ones included, keyed by subject, purpose and notice version.</summary>
    private static TableSchemaModel BuildConsentsTable(EfCoreProvider provider)
    {
        var schemaName = provider switch
        {
            EfCoreProvider.PostgreSql => "public",
            EfCoreProvider.SqlServer => "dbo",
            _ => null
        };

        string Str(int len) => SqlTypeMapper.MapToSqlType("System.String", provider, len, null, null, false);
        var ticks = SqlTypeMapper.MapToSqlType("System.Int64", provider);

        var columns = ImmutableArray.Create(
            new ColumnSchemaModel("SubjectRef", Str(64), false, true),
            new ColumnSchemaModel("Purpose", Str(256), false, true),
            new ColumnSchemaModel("NoticeVersion", Str(64), false, true),
            new ColumnSchemaModel("GrantedAt", ticks, false, false),
            new ColumnSchemaModel("WithdrawnAt", ticks, true, false),
            new ColumnSchemaModel("Source", Str(256), true, false));

        var indexes = ImmutableArray.Create(
            new IndexSchemaModel("IX___Consents_SubjectRef", ImmutableArray.Create("SubjectRef")));

        return new TableSchemaModel("__Consents", schemaName, null, columns, indexes,
            ImmutableArray<ForeignKeySchemaModel>.Empty);
    }
}
