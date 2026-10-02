using System.Collections.Immutable;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Diff;

/// <summary>
///     Computes schema changes by comparing desired (SG-generated) vs current (introspected) schemas.
///     Produces an ordered list of idempotent changes: drops first, then creates, then alters.
/// </summary>
public sealed class SchemaDiffEngine : ISchemaDiffEngine
{
    public SchemaDiff ComputeDiff(SchemaVersion desired, SchemaVersion? current, bool dropUnknownTables = true)
    {
        var changes = new List<SchemaChange>();

        // Matching tolerates one side omitting the schema qualifier, without ever pairing a table
        // with an arbitrary same-named one from another schema. See TableMatcher.
        var desiredTables = new TableMatcher(desired.Tables);
        var currentTables = new TableMatcher(current?.Tables ?? []);

        // Tables to drop (in current but not in desired). Keep the schema qualifier: an
        // unqualified DROP in a multi-schema database can hit the wrong table.
        if (dropUnknownTables)
        {
            foreach (var table in currentTables.All)
            {
                // A framework table is never dropped for being absent from the desired schema. That
                // schema is generated from the application's entities and cannot describe the outbox,
                // the audit trail or subject keys — so "unknown" here means "belongs to somebody else",
                // not "obsolete". Dropping one would destroy data the application never modelled and
                // therefore cannot restore.
                if (IsFrameworkTable(table.Name))
                    continue;

                if (desiredTables.Find(table) is null)
                    changes.Add(new DropTable(table.Name, table.SchemaName));
            }
        }

        foreach (var table in desiredTables.All)
        {
            var name = table.Name;
            var currentTable = currentTables.Find(table);

            if (currentTable is null)
            {
                changes.Add(new CreateTable(table));

                // Emit AddIndex and AddForeignKey for every index/FK on the new table
                foreach (var idx in table.Indexes)
                    changes.Add(new AddIndex(name, idx, SchemaName: table.SchemaName));
                foreach (var fk in table.ForeignKeys)
                    changes.Add(new AddForeignKey(name, fk, table.SchemaName));
                foreach (var check in table.CheckConstraints)
                    changes.Add(new AddCheckConstraint(name, check, table.SchemaName));

                continue;
            }

            // Table exists in both — diff columns, indexes, FKs. Prefer the introspected schema
            // (where the table actually lives) over the desired one (often null when unannotated).
            var schema = currentTable.SchemaName ?? table.SchemaName;
            DiffColumns(name, schema, table.Columns, currentTable.Columns, changes);
            DiffPrimaryKey(name, schema, table.Columns, currentTable.Columns, changes);
            DiffIndexes(name, schema, table.Indexes, currentTable.Indexes, changes);
            DiffForeignKeys(name, schema, table.ForeignKeys, currentTable.ForeignKeys, changes);
            DiffCheckConstraints(name, schema, table.CheckConstraints, currentTable.CheckConstraints, changes);
        }

        // Order: drops first, then creates, then column changes, then indexes, then FKs
        // This ensures FK target tables exist before FK constraints are added
        var ordered = changes
            .OrderBy(c => c switch
            {
                DropCheckConstraint => 0,
                DropForeignKey => 0,
                DropIndex => 1,
                DropColumn => 2,
                DropTable => 3,
                CreateTable => 4,
                AddColumn or AlterColumnType or AlterColumnNullability or AlterColumnDefault or RenameColumn => 5,
                // After the columns exist and are typed, before the indexes and FKs that may
                // reference the key.
                AlterPrimaryKey => 6,
                AddIndex => 7,
                AddForeignKey => 8,
                // After the columns exist: a check names them, and adding it before they do fails.
                AddCheckConstraint => 8,
                _ => 9
            })
            .ToImmutableArray();

        var hasBreaking = ordered.Any(c => c.IsBreaking);
        return new SchemaDiff(ordered, hasBreaking, desired);
    }

    /// <summary>
    ///     Adds what the desired schema declares and the database does not have, and drops what it has
    ///     and the schema no longer declares.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Compared by <b>name only</b>, deliberately. A check's expression comes back from the
    ///     database normalised — parenthesised, re-cased, identifiers requoted — so comparing the text
    ///     would report a difference on every run and reissue the same constraint forever. An introspector
    ///     that reports no checks at all therefore causes them to be added once, not repeatedly.
    ///     <para>
    ///         The cost is that changing an expression without changing its name is not noticed. Naming
    ///         a check after what it asserts, as the generator does, makes that the rare case.
    ///     </para>
    /// </remarks>
    private static void DiffCheckConstraints(
        string tableName,
        string? schemaName,
        ImmutableArray<CheckConstraintSchema> desired,
        ImmutableArray<CheckConstraintSchema> current,
        List<SchemaChange> changes)
    {
        foreach (var check in desired)
        {
            if (!current.Any(c => string.Equals(c.Name, check.Name, StringComparison.OrdinalIgnoreCase)))
                changes.Add(new AddCheckConstraint(tableName, check, schemaName));
        }

        foreach (var existing in current)
        {
            if (!desired.Any(c => string.Equals(c.Name, existing.Name, StringComparison.OrdinalIgnoreCase)))
                changes.Add(new DropCheckConstraint(tableName, existing.Name, schemaName));
        }
    }

    private static void DiffColumns(
        string tableName,
        string? schemaName,
        ImmutableArray<ColumnSchema> desired,
        ImmutableArray<ColumnSchema> current,
        List<SchemaChange> changes)
    {
        var desiredCols = desired.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var currentCols = current.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        // Check for renames first (desired has RenamedFrom pointing to current column)
        var renamedOldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (newName, col) in desiredCols)
        {
            if (col.RenamedFrom is null) continue;
            if (!currentCols.ContainsKey(col.RenamedFrom)) continue;
            if (currentCols.ContainsKey(newName)) continue; // New name already exists — not a rename

            changes.Add(new RenameColumn(tableName, col.RenamedFrom, newName, schemaName));
            renamedOldNames.Add(col.RenamedFrom);
        }

        // Columns to drop (in current but not in desired, and not renamed)
        foreach (var (name, _) in currentCols)
        {
            if (!desiredCols.ContainsKey(name) && !renamedOldNames.Contains(name))
                changes.Add(new DropColumn(tableName, name, schemaName));
        }

        // Columns to add (in desired but not in current, and not a rename target)
        foreach (var (name, col) in desiredCols)
        {
            if (currentCols.ContainsKey(name) || col.RenamedFrom is not null)
                continue;

            changes.Add(new AddColumn(tableName, col, schemaName));
        }

        // Columns that exist in both — diff type, nullability, default
        foreach (var (name, desiredCol) in desiredCols)
        {
            // For renamed columns, compare against the old name
            var currentName = desiredCol.RenamedFrom ?? name;
            if (!currentCols.TryGetValue(currentName, out var currentCol))
                continue;

            // Type change. Carry the desired nullability along: providers that restate the whole
            // column definition (SQL Server) must not lose it, and a separate AlterColumnNullability
            // is only emitted when the nullability actually differs.
            if (!SqlTypeEquals(desiredCol.SqlType, currentCol.SqlType))
            {
                var isNarrowing = IsNarrowingTypeChange(currentCol.SqlType, desiredCol.SqlType);
                changes.Add(new AlterColumnType(
                    tableName, name, currentCol.SqlType, desiredCol.SqlType, isNarrowing,
                    schemaName, desiredCol.IsNullable));
            }

            // Nullability change
            if (desiredCol.IsNullable != currentCol.IsNullable)
                changes.Add(new AlterColumnNullability(tableName, name, desiredCol.IsNullable, desiredCol.SqlType, schemaName));

            // Default value change. Normalise both sides so provider-specific paren wrapping
            // (e.g. SQL Server's "((0))" / "(((0)))" vs a desired "0") does not produce a
            // spurious diff. See NormalizeDefault.
            if (!string.Equals(NormalizeDefault(desiredCol.DefaultValue), NormalizeDefault(currentCol.DefaultValue), StringComparison.OrdinalIgnoreCase))
                changes.Add(new AlterColumnDefault(tableName, name, currentCol.DefaultValue, desiredCol.DefaultValue, schemaName));
        }
    }

    /// <summary>
    ///     Detects a change to the set of primary-key columns. Without comparing
    ///     <see cref="ColumnSchema.IsPrimaryKey" />, redefining a table's key would be accepted at
    ///     compile time and then silently ignored forever against an existing database.
    /// </summary>
    private static void DiffPrimaryKey(
        string tableName,
        string? schemaName,
        ImmutableArray<ColumnSchema> desired,
        ImmutableArray<ColumnSchema> current,
        List<SchemaChange> changes)
    {
        var desiredPk = desired.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToImmutableArray();
        var currentPk = current.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToImmutableArray();

        // A table with no declared key on either side is not a change. Column ORDER is part of the
        // key (it determines the index order), so compare as a sequence, not as a set.
        if (desiredPk.SequenceEqual(currentPk, StringComparer.OrdinalIgnoreCase))
            return;

        // Nothing to alter while the table itself is still being created — CreateTable already
        // emits the key inline.
        if (desiredPk.Length == 0 && currentPk.Length == 0)
            return;

        changes.Add(new AlterPrimaryKey(tableName, currentPk, desiredPk, schemaName));
    }

    private static void DiffIndexes(
        string tableName,
        string? schemaName,
        ImmutableArray<IndexSchema> desired,
        ImmutableArray<IndexSchema> current,
        List<SchemaChange> changes)
    {
        // Match indexes by structural identity (columns + uniqueness), not by name.
        // SG and EF Core may use different naming conventions.
        static string IdxKey(IndexSchema idx) =>
            $"{string.Join(",", idx.Columns)}|{idx.IsUnique}".ToUpperInvariant();

        var desiredByKey = new Dictionary<string, IndexSchema>(StringComparer.OrdinalIgnoreCase);
        foreach (var idx in desired)
            desiredByKey.TryAdd(IdxKey(idx), idx);

        var currentByKey = new Dictionary<string, IndexSchema>(StringComparer.OrdinalIgnoreCase);
        foreach (var idx in current)
            currentByKey.TryAdd(IdxKey(idx), idx);

        foreach (var (key, cur) in currentByKey)
        {
            if (!desiredByKey.ContainsKey(key))
                changes.Add(new DropIndex(tableName, cur.Name, schemaName));
        }

        foreach (var (key, idx) in desiredByKey)
        {
            if (!currentByKey.TryGetValue(key, out var cur))
            {
                changes.Add(new AddIndex(tableName, idx, SchemaName: schemaName));
                continue;
            }

            // Index exists structurally — check if filter changed, as a predicate and not as the text each
            // database spells it with (IndexFilterExpression).
            if (!string.Equals(IndexFilterExpression.Normalize(idx.Filter), IndexFilterExpression.Normalize(cur.Filter),
                    StringComparison.Ordinal))
            {
                changes.Add(new DropIndex(tableName, cur.Name, schemaName));
                changes.Add(new AddIndex(tableName, idx, SchemaName: schemaName));
            }
        }
    }

    private static void DiffForeignKeys(
        string tableName,
        string? schemaName,
        ImmutableArray<ForeignKeySchema> desired,
        ImmutableArray<ForeignKeySchema> current,
        List<SchemaChange> changes)
    {
        // Match FKs by structural identity (Column + ReferencedTable + ReferencedColumn), not by
        // name — SG and EF Core may generate different FK naming conventions. The referenced
        // column matters: an FK re-targeted to a different column of the same table is a change.
        static string FkKey(ForeignKeySchema fk) =>
            $"{fk.Column}→{fk.ReferencedTable}.{fk.ReferencedColumn}".ToUpperInvariant();

        var desiredByKey = desired.ToDictionary(FkKey, StringComparer.OrdinalIgnoreCase);
        var currentByKey = current.ToDictionary(FkKey, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, cur) in currentByKey)
        {
            if (!desiredByKey.ContainsKey(key))
                changes.Add(new DropForeignKey(tableName, cur.Name, schemaName));
        }

        foreach (var (key, fk) in desiredByKey)
        {
            if (!currentByKey.TryGetValue(key, out var cur))
            {
                changes.Add(new AddForeignKey(tableName, fk, schemaName));
                continue;
            }

            // FK exists structurally — check if OnDelete behavior changed
            if (AsWritten(fk.OnDelete) != AsWritten(cur.OnDelete))
            {
                changes.Add(new DropForeignKey(tableName, cur.Name, schemaName));
                changes.Add(new AddForeignKey(tableName, fk, schemaName));
            }
        }
    }

    /// <summary>
    ///     The ON DELETE action as the database holds it once written, which is what introspection reads back.
    /// </summary>
    /// <remarks>
    ///     Every generator writes <see cref="ReferentialAction.Restrict" /> as <c>NO ACTION</c>
    ///     (<c>SqlGeneratorBase.ToSqlDeleteAction</c>: SQL Server has no <c>RESTRICT</c>), so a database never
    ///     reports <c>Restrict</c> for a key this library created. Compared as declared, a <c>Restrict</c> key
    ///     would be dropped and added again on every run and the schema would never converge.
    /// </remarks>
    private static ReferentialAction AsWritten(ReferentialAction action)
        => action == ReferentialAction.Restrict ? ReferentialAction.NoAction : action;

    /// <summary>
    ///     Normalises a default-value expression for comparison: strips ALL fully-wrapping
    ///     balanced parentheses (SQL Server stores defaults as "((0))" / "(((0)))" while the
    ///     desired schema typically carries "0"), trims whitespace and lowercases. A wrapping
    ///     pair is only removed when the opening paren's match is the final character, so an
    ///     expression like "(a)+(b)" is left intact.
    /// </summary>
    internal static string? NormalizeDefault(string? value)
    {
        if (value is null) return null;

        var v = value.Trim();
        while (v.Length >= 2 && v[0] == '(' && v[^1] == ')' && IsWrappingPair(v))
            v = v[1..^1].Trim();

        return v.ToLowerInvariant();
    }

    private static bool IsWrappingPair(string value)
    {
        var depth = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '(') depth++;
            else if (value[i] == ')')
            {
                depth--;
                if (depth == 0) return i == value.Length - 1;
            }
        }

        return false;
    }

    /// <summary>
    ///     Compares SQL types with normalization (e.g. "varchar(256)" == "character varying(256)").
    /// </summary>
    /// <summary>
    ///     Whether a table belongs to the framework rather than to the application's schema.
    /// </summary>
    /// <remarks>
    ///     Most framework tables carry the <c>__</c> prefix, but the configuration store's predate that
    ///     convention and are named <c>pragmatic_*</c> — including the one holding encrypted secrets,
    ///     which nothing else can reconstruct. Checking only the prefix would have left those four
    ///     droppable while looking like every framework table was covered.
    /// </remarks>
    private static bool IsFrameworkTable(string name)
        => name.StartsWith(MigrationConstants.FrameworkTablePrefix, StringComparison.Ordinal)
           || Array.Exists(MigrationConstants.LegacyFrameworkTables,
               legacy => string.Equals(legacy, name, StringComparison.OrdinalIgnoreCase));

    internal static bool SqlTypeEquals(string a, string b)
    {
        return string.Equals(NormalizeSqlType(a), NormalizeSqlType(b), StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeSqlType(string sqlType)
    {
        var normalized = sqlType.Trim().ToLowerInvariant();

        // Generated-key sugar. Neither of these is a type the database will report back: PostgreSQL
        // expands SERIAL into an integer column plus a sequence, and SQL Server's IDENTITY is a column
        // property. A schema that asks for one and then introspects the result would otherwise see a
        // type change on every run, and report dropping-and-recreating a primary key as a breaking
        // change -- against a database that already matches.
        normalized = normalized switch
        {
            "bigserial" or "serial8" => "int8",
            "serial" or "serial4" => "int4",
            "smallserial" or "serial2" => "int2",
            _ => normalized,
        };

        var identity = normalized.IndexOf("identity", StringComparison.Ordinal);
        if (identity > 0)
            normalized = normalized[..identity].Trim();

        // PostgreSQL aliases
        normalized = normalized
            .Replace("character varying", "varchar")
            .Replace("character", "char")
            .Replace("double precision", "float8")
            .Replace("real", "float4")
            .Replace("timestamp without time zone", "timestamp")
            .Replace("timestamp with time zone", "timestamptz")
            .Replace("boolean", "bool")
            // All three integer widths, not just one. PostgreSQL introspects int8/int4/int2 while the
            // schema is generated as bigint/integer/smallint, so a missing alias here shows up as a
            // type change on every run against a database that already matches — and a type change is
            // potentially breaking, so it can refuse to start. Only `integer` was aliased, which left
            // every long and short column producing a phantom change.
            .Replace("bigint", "int8")
            .Replace("smallint", "int2")
            .Replace("integer", "int4");

        // PostgreSQL array types: _text → text[], _int4 → int4[], etc.
        if (normalized.StartsWith("_", StringComparison.Ordinal) && !normalized.Contains("["))
            normalized = normalized.Substring(1) + "[]";

        // SQL Server aliases
        normalized = normalized
            .Replace("datetime2(7)", "datetime2");

        return normalized;
    }

    /// <summary>
    ///     Decides whether a type change can lose data, which is what marks it breaking.
    ///     <para>
    ///     Two cases count. Same family, smaller capacity — <c>varchar(256)</c> → <c>varchar(50)</c>,
    ///     <c>decimal(18,2)</c> → <c>decimal(18,1)</c> (fewer fractional digits), <c>text</c> →
    ///     <c>varchar(N)</c>. And a change of family altogether — <c>varchar(50)</c> → <c>int</c>,
    ///     <c>timestamptz</c> → <c>text</c>: the conversion either fails or reinterprets every
    ///     existing value, so it must never be applied without an explicit Force.
    ///     </para>
    /// </summary>
    internal static bool IsNarrowingTypeChange(string oldType, string newType)
    {
        var (oldLen, oldScale) = ExtractLengthAndScale(oldType);
        var (newLen, newScale) = ExtractLengthAndScale(newType);

        var oldNorm = NormalizeSqlType(oldType);
        var newNorm = NormalizeSqlType(newType);

        // Different type families: the conversion is not capacity-preserving at all.
        if (TypeFamilyOf(oldNorm) != TypeFamilyOf(newNorm))
            return true;

        // If both have explicit lengths/precision: smaller length OR smaller scale is narrowing.
        if (oldLen.HasValue && newLen.HasValue)
            return newLen.Value < oldLen.Value
                   || (oldScale.HasValue && newScale.HasValue && newScale.Value < oldScale.Value);

        // text → varchar(N) is narrowing
        if ((oldNorm == "text" || oldNorm.Contains("max")) && newLen.HasValue)
            return true;

        return false;
    }

    /// <summary>
    ///     Coarse family of a normalised SQL type. Only used to tell "same kind of data, different
    ///     size" from "different kind of data"; an unrecognised type gets its own bare name as the
    ///     family, so an unknown-to-unknown change of name is treated as a family change.
    /// </summary>
    private static string TypeFamilyOf(string normalizedType)
    {
        var bare = normalizedType;
        var paren = bare.IndexOf('(');
        if (paren > 0) bare = bare.Substring(0, paren);
        bare = bare.Trim().TrimEnd('[', ']');

        return bare switch
        {
            "varchar" or "char" or "text" or "nvarchar" or "nchar" or "ntext" or "citext" => "text",
            "int2" or "int4" or "int8" or "smallint" or "int" or "bigint" or "tinyint" => "integer",
            "numeric" or "decimal" or "money" or "smallmoney" => "decimal",
            "float4" or "float8" or "real" or "float" or "double" => "float",
            "bool" or "bit" => "boolean",
            "date" => "date",
            "time" or "timetz" => "time",
            "timestamp" or "timestamptz" or "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" => "timestamp",
            "uuid" or "uniqueidentifier" => "uuid",
            "bytea" or "varbinary" or "binary" or "image" => "binary",
            "json" or "jsonb" or "xml" => "document",
            _ => bare
        };
    }

    private static (int? Length, int? Scale) ExtractLengthAndScale(string sqlType)
    {
        var open = sqlType.IndexOf('(');
        var close = sqlType.IndexOf(')');
        if (open < 0 || close <= open) return (null, null);

        var inner = sqlType.Substring(open + 1, close - open - 1);
        if (string.Equals(inner, "max", StringComparison.OrdinalIgnoreCase))
            return (null, null);

        var parts = inner.Split(',');
        int? length = int.TryParse(parts[0], out var len) ? len : null;
        int? scale = parts.Length > 1 && int.TryParse(parts[1], out var s) ? s : null;
        return (length, scale);
    }
}
