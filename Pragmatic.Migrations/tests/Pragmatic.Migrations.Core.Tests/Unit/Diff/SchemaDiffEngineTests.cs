using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Core.Tests.Unit.Diff;

public class SchemaDiffEngineTests
{
    private readonly SchemaDiffEngine _engine = new();

    [Fact]
    public void ComputeDiff_IdenticalHashes_ReturnsEmpty()
    {
        var schema = new SchemaVersion([]);
        var result = _engine.ComputeDiff(schema, schema);

        result.HasChanges.Should().BeFalse();
        result.HasBreakingChanges.Should().BeFalse();
    }

    [Fact]
    public void ComputeDiff_CurrentIsNull_AllTablesAreCreates()
    {
        var desired = new SchemaVersion([
            new TableSchema("Users", null,
                [new ColumnSchema("Id", "uuid", false, true)],
                [], []),
            new TableSchema("Orders", null,
                [new ColumnSchema("Id", "uuid", false, true)],
                [], [])
        ]);

        var result = _engine.ComputeDiff(desired, null);

        result.Changes.Should().HaveCount(2);
        result.Changes.Should().AllBeOfType<CreateTable>();
        result.HasBreakingChanges.Should().BeFalse();
    }

    [Fact]
    public void ComputeDiff_TableRemoved_ProducesDropTable()
    {
        var desired = new SchemaVersion([]);
        var current = new SchemaVersion([
            new TableSchema("OldTable", null,
                [new ColumnSchema("Id", "int", false, true)],
                [], [])
        ]);

        var result = _engine.ComputeDiff(desired, current);

        result.Changes.Should().ContainSingle()
            .Which.Should().BeOfType<DropTable>()
            .Which.TableName.Should().Be("OldTable");
        result.HasBreakingChanges.Should().BeTrue();
    }

    [Theory]
    [InlineData("BIGSERIAL", "int8")]
    [InlineData("serial", "int4")]
    [InlineData("BIGINT IDENTITY(1,1)", "bigint")]
    public void ComputeDiff_AGeneratedKeyColumn_IsNotSeenAsATypeChange(string desired, string introspected)
    {
        // Neither SERIAL nor IDENTITY is a type the database reports back: PostgreSQL expands the first
        // into an integer plus a sequence, and the second is a column property. Without this the schema
        // would see a type change on every run and report recreating a primary key as breaking, against
        // a database that already matches. Found when the audit trail became the first generated table
        // with a database-assigned integer key.
        var desiredSchema = new SchemaVersion([
            new TableSchema("__AuditEntries", null, [new ColumnSchema("Seq", desired, false, true)], [], [])
        ]);
        var current = new SchemaVersion([
            new TableSchema("__AuditEntries", null, [new ColumnSchema("Seq", introspected, false, true)], [], [])
        ]);

        _engine.ComputeDiff(desiredSchema, current).HasChanges.Should().BeFalse();
    }

    /// <summary>
    ///     Every integer width, not just one. Only <c>integer</c> was aliased, so a long or short column
    ///     reported a type change on every run against a database that already matched — and a type
    ///     change is potentially breaking, so it can refuse to start. Found while adding the
    ///     generated-key rules next door.
    /// </summary>
    [Theory]
    [InlineData("bigint", "int8")]
    [InlineData("integer", "int4")]
    [InlineData("smallint", "int2")]
    public void ComputeDiff_AllThreeIntegerWidths_MatchTheirPostgresNames(string desired, string introspected)
        => SchemaDiffEngine.SqlTypeEquals(desired, introspected).Should().BeTrue($"{desired} == {introspected}");

    /// <summary>
    ///     Every type the generator emits for PostgreSQL, against the <c>udt_name</c> the introspector
    ///     reads back.
    /// </summary>
    /// <remarks>
    ///     The two sides are written by different code and nothing else connects them. A missing alias
    ///     here is invisible until a schema reports a type change on every run against a database that
    ///     already matches — and a type change is potentially breaking, so it can refuse to start.
    /// </remarks>
    [Theory]
    [InlineData("text", "text")]
    [InlineData("integer", "int4")]
    [InlineData("bigint", "int8")]
    [InlineData("smallint", "int2")]
    [InlineData("boolean", "bool")]
    [InlineData("double precision", "float8")]
    [InlineData("real", "float4")]
    [InlineData("uuid", "uuid")]
    [InlineData("timestamp", "timestamp")]
    [InlineData("timestamptz", "timestamptz")]
    [InlineData("date", "date")]
    [InlineData("time", "time")]
    [InlineData("bytea", "bytea")]
    [InlineData("varchar(64)", "varchar(64)")]
    [InlineData("numeric(18,2)", "numeric(18,2)")]
    public void ComputeDiff_EveryPostgresTypeTheGeneratorEmits_RoundTripsThroughIntrospection(string emitted, string introspected)
        => SchemaDiffEngine.SqlTypeEquals(emitted, introspected).Should().BeTrue($"{emitted} <-> {introspected}");

    /// <summary>The same round-trip for SQL Server, whose introspector formats types differently.</summary>
    [Theory]
    [InlineData("nvarchar(max)", "nvarchar(max)")]
    [InlineData("nvarchar(64)", "nvarchar(64)")]
    [InlineData("int", "int")]
    [InlineData("bigint", "bigint")]
    [InlineData("smallint", "smallint")]
    [InlineData("tinyint", "tinyint")]
    [InlineData("bit", "bit")]
    [InlineData("decimal(18,2)", "decimal(18,2)")]
    [InlineData("float", "float")]
    [InlineData("real", "real")]
    [InlineData("uniqueidentifier", "uniqueidentifier")]
    [InlineData("datetime2", "datetime2")]
    [InlineData("datetimeoffset", "datetimeoffset")]
    [InlineData("date", "date")]
    [InlineData("time", "time")]
    [InlineData("varbinary(max)", "varbinary(max)")]
    public void ComputeDiff_EverySqlServerTypeTheGeneratorEmits_RoundTripsThroughIntrospection(
        string emitted, string introspected)
        => SchemaDiffEngine.SqlTypeEquals(emitted, introspected).Should().BeTrue($"{emitted} <-> {introspected}");

    [Fact]
    public void ComputeDiff_AFrameworkTableAbsentFromTheDesiredSchema_IsNotDropped()
    {
        // The desired schema is generated from the application's entities, so it cannot describe the
        // audit trail, the outbox or subject keys. "Unknown" therefore means "belongs to somebody
        // else", not "obsolete" — and dropping one destroys data the application never modelled and
        // cannot restore. Found when adding the audit trail to the Showcase database made its own
        // migration refuse to start, reporting three breaking changes.
        var desired = new SchemaVersion([]);
        var current = new SchemaVersion([
            new TableSchema("__AuditEntries", null,
                [new ColumnSchema("Seq", "bigint", false, true)],
                [], [])
        ]);

        var result = _engine.ComputeDiff(desired, current);

        result.HasChanges.Should().BeFalse();
        result.HasBreakingChanges.Should().BeFalse();
    }

    /// <summary>
    ///     The configuration store's tables predate the <c>__</c> convention and must be protected by
    ///     name.
    /// </summary>
    /// <remarks>
    ///     Checking only the prefix left these four droppable while looking like every framework table
    ///     was covered — and one of them holds encrypted secrets, which nothing else can reconstruct.
    /// </remarks>
    [Theory]
    [InlineData("pragmatic_config")]
    [InlineData("pragmatic_secrets")]
    [InlineData("pragmatic_config_change")]
    [InlineData("pragmatic_config_notify")]
    [InlineData("pragmatic_config_audit")]
    public void ComputeDiff_AConfigurationStoreTable_IsNotDropped(string table)
    {
        var current = new SchemaVersion([
            new TableSchema(table, null, [new ColumnSchema("id", "int", false, true)], [], [])
        ]);

        _engine.ComputeDiff(new SchemaVersion([]), current).HasChanges.Should().BeFalse();
    }

    [Fact]
    public void ComputeDiff_AnApplicationTableIsStillDropped_AlongsideAFrameworkOne()
    {
        // The guard must not become a blanket "never drop anything".
        var desired = new SchemaVersion([]);
        var current = new SchemaVersion([
            new TableSchema("__AuditEntries", null, [new ColumnSchema("Seq", "bigint", false, true)], [], []),
            new TableSchema("OldTable", null, [new ColumnSchema("Id", "int", false, true)], [], [])
        ]);

        var result = _engine.ComputeDiff(desired, current);

        result.Changes.Should().ContainSingle()
            .Which.Should().BeOfType<DropTable>()
            .Which.TableName.Should().Be("OldTable");
    }

    [Fact]
    public void ComputeDiff_ColumnAdded_ProducesAddColumn()
    {
        var current = new SchemaVersion([
            new TableSchema("Users", null,
                [new ColumnSchema("Id", "uuid", false, true)],
                [], [])
        ]);

        var desired = new SchemaVersion([
            new TableSchema("Users", null,
                [
                    new ColumnSchema("Id", "uuid", false, true),
                    new ColumnSchema("Email", "text", true, false)
                ],
                [], [])
        ]);

        var result = _engine.ComputeDiff(desired, current);

        result.Changes.Should().ContainSingle()
            .Which.Should().BeOfType<AddColumn>()
            .Which.Column.Name.Should().Be("Email");
        result.HasBreakingChanges.Should().BeFalse();
    }

    [Fact]
    public void ComputeDiff_ColumnRemoved_ProducesDropColumn()
    {
        var current = new SchemaVersion([
            new TableSchema("Users", null,
                [
                    new ColumnSchema("Id", "uuid", false, true),
                    new ColumnSchema("OldField", "text", true, false)
                ],
                [], [])
        ]);

        var desired = new SchemaVersion([
            new TableSchema("Users", null,
                [new ColumnSchema("Id", "uuid", false, true)],
                [], [])
        ]);

        var result = _engine.ComputeDiff(desired, current);

        result.Changes.Should().ContainSingle()
            .Which.Should().BeOfType<DropColumn>()
            .Which.ColumnName.Should().Be("OldField");
        result.HasBreakingChanges.Should().BeTrue();
    }

    [Fact]
    public void ComputeDiff_ColumnRenamed_ProducesRenameColumn()
    {
        var current = new SchemaVersion([
            new TableSchema("Users", null,
                [
                    new ColumnSchema("Id", "uuid", false, true),
                    new ColumnSchema("FirstName", "text", false, false)
                ],
                [], [])
        ]);

        var desired = new SchemaVersion([
            new TableSchema("Users", null,
                [
                    new ColumnSchema("Id", "uuid", false, true),
                    new ColumnSchema("GivenName", "text", false, false, RenamedFrom: "FirstName")
                ],
                [], [])
        ]);

        var result = _engine.ComputeDiff(desired, current);

        result.Changes.Should().ContainSingle()
            .Which.Should().BeOfType<RenameColumn>()
            .Which.Should().Match<RenameColumn>(r => r.OldName == "FirstName" && r.NewName == "GivenName");
    }

    [Fact]
    public void ComputeDiff_ColumnTypeChanged_ProducesAlterColumnType()
    {
        var current = new SchemaVersion([
            new TableSchema("Orders", null,
                [
                    new ColumnSchema("Id", "uuid", false, true),
                    new ColumnSchema("Amount", "integer", false, false)
                ],
                [], [])
        ]);

        var desired = new SchemaVersion([
            new TableSchema("Orders", null,
                [
                    new ColumnSchema("Id", "uuid", false, true),
                    new ColumnSchema("Amount", "numeric(18,2)", false, false)
                ],
                [], [])
        ]);

        var result = _engine.ComputeDiff(desired, current);

        result.Changes.Should().ContainSingle()
            .Which.Should().BeOfType<AlterColumnType>();
    }

    [Fact]
    public void ComputeDiff_IndexAdded_ProducesAddIndex()
    {
        var current = new SchemaVersion([
            new TableSchema("Users", null,
                [new ColumnSchema("Id", "uuid", false, true)],
                [], [])
        ]);

        var desired = new SchemaVersion([
            new TableSchema("Users", null,
                [new ColumnSchema("Id", "uuid", false, true)],
                [new IndexSchema("IX_Users_Email", ImmutableArray.Create("Email"), true)],
                [])
        ]);

        var result = _engine.ComputeDiff(desired, current);

        result.Changes.Should().ContainSingle()
            .Which.Should().BeOfType<AddIndex>();
    }

    [Fact]
    public void ComputeDiff_ForeignKeyAdded_ProducesAddForeignKey()
    {
        var current = new SchemaVersion([
            new TableSchema("Orders", null,
                [new ColumnSchema("Id", "uuid", false, true)],
                [], [])
        ]);

        var desired = new SchemaVersion([
            new TableSchema("Orders", null,
                [new ColumnSchema("Id", "uuid", false, true)],
                [],
                [new ForeignKeySchema("FK_Orders_UserId_Users", "UserId", "Users", "Id", ReferentialAction.NoAction)])
        ]);

        var result = _engine.ComputeDiff(desired, current);

        result.Changes.Should().ContainSingle()
            .Which.Should().BeOfType<AddForeignKey>();
    }

    [Fact]
    public void ComputeDiff_NullabilityChanged_ProducesAlterNullability()
    {
        var current = new SchemaVersion([
            new TableSchema("Users", null,
                [
                    new ColumnSchema("Id", "uuid", false, true),
                    new ColumnSchema("Email", "text", true, false)
                ],
                [], [])
        ]);

        var desired = new SchemaVersion([
            new TableSchema("Users", null,
                [
                    new ColumnSchema("Id", "uuid", false, true),
                    new ColumnSchema("Email", "text", false, false)
                ],
                [], [])
        ]);

        var result = _engine.ComputeDiff(desired, current);

        result.Changes.Should().ContainSingle()
            .Which.Should().BeOfType<AlterColumnNullability>()
            .Which.NewIsNullable.Should().BeFalse();
        result.HasBreakingChanges.Should().BeTrue(); // SET NOT NULL is breaking
    }

    [Fact]
    public void ComputeDiff_ComplexScenario_ProducesCorrectChanges()
    {
        var current = new SchemaVersion([
            new TableSchema("Users", null,
                [
                    new ColumnSchema("Id", "uuid", false, true),
                    new ColumnSchema("FirstName", "text", false, false),
                    new ColumnSchema("OldField", "text", true, false)
                ],
                [new IndexSchema("IX_Users_OldField", ImmutableArray.Create("OldField"))],
                []),
            new TableSchema("Logs", null,
                [new ColumnSchema("Id", "int", false, true)],
                [], [])
        ]);

        var desired = new SchemaVersion([
            new TableSchema("Users", null,
                [
                    new ColumnSchema("Id", "uuid", false, true),
                    new ColumnSchema("GivenName", "text", false, false, RenamedFrom: "FirstName"),
                    new ColumnSchema("Email", "varchar(256)", true, false)
                ],
                [new IndexSchema("IX_Users_Email", ImmutableArray.Create("Email"), true)],
                []),
            new TableSchema("Orders", null,
                [new ColumnSchema("Id", "uuid", false, true)],
                [], [])
        ]);

        var result = _engine.ComputeDiff(desired, current);

        // Drop Logs table, Create Orders table, Rename FirstName→GivenName,
        // Drop OldField, Add Email, Drop IX_Users_OldField, Add IX_Users_Email
        result.Changes.Should().HaveCount(7);
        result.Changes.OfType<DropTable>().Should().ContainSingle(t => t.TableName == "Logs");
        result.Changes.OfType<CreateTable>().Should().ContainSingle(t => t.Table.Name == "Orders");
        result.Changes.OfType<RenameColumn>().Should().ContainSingle();
        result.Changes.OfType<DropColumn>().Should().ContainSingle(c => c.ColumnName == "OldField");
        result.Changes.OfType<AddColumn>().Should().ContainSingle(c => c.Column.Name == "Email");
        result.HasBreakingChanges.Should().BeTrue(); // DropTable + DropColumn
    }
}

public class SqlTypeNormalizationTests
{
    [Theory]
    [InlineData("varchar(256)", "character varying(256)", true)]
    [InlineData("boolean", "bool", true)]
    [InlineData("integer", "int4", true)]
    [InlineData("timestamptz", "timestamp with time zone", true)]
    [InlineData("text", "varchar(256)", false)]
    [InlineData("uuid", "uniqueidentifier", false)]
    [InlineData("_text", "text[]", true)]
    [InlineData("_int4", "int4[]", true)]
    [InlineData("_bool", "bool[]", true)]
    public void SqlTypeEquals_NormalizesCorrectly(string a, string b, bool expected)
    {
        SchemaDiffEngine.SqlTypeEquals(a, b).Should().Be(expected);
    }

    [Theory]
    [InlineData("text", "varchar(256)", true)]
    [InlineData("varchar(512)", "varchar(256)", true)]
    [InlineData("varchar(256)", "varchar(512)", false)]
    [InlineData("integer", "bigint", false)]
    public void IsNarrowingTypeChange_DetectsCorrectly(string oldType, string newType, bool expected)
    {
        SchemaDiffEngine.IsNarrowingTypeChange(oldType, newType).Should().Be(expected);
    }

    [Theory]
    // A change of type FAMILY is never capacity-preserving: the conversion either fails outright or
    // reinterprets every stored value, so it must be blocked without an explicit Force.
    [InlineData("varchar(50)", "int")]
    [InlineData("text", "uuid")]
    [InlineData("timestamptz", "text")]
    [InlineData("uuid", "bigint")]
    [InlineData("jsonb", "int4")]
    public void IsNarrowingTypeChange_CrossFamilyConversion_IsNarrowing(string oldType, string newType)
    {
        SchemaDiffEngine.IsNarrowingTypeChange(oldType, newType).Should().BeTrue();
    }

    [Theory]
    // Same family, same-or-larger capacity: safe, must stay non-breaking.
    [InlineData("varchar(50)", "text")]
    [InlineData("int4", "int8")]
    [InlineData("timestamp", "timestamptz")]
    [InlineData("character varying(50)", "varchar(50)")]
    public void IsNarrowingTypeChange_WideningWithinFamily_IsNotNarrowing(string oldType, string newType)
    {
        SchemaDiffEngine.IsNarrowingTypeChange(oldType, newType).Should().BeFalse();
    }

    [Fact]
    public void ComputeDiff_TypeChange_CarriesTheDesiredNullability()
    {
        // Providers that restate the whole column definition need it; without it they would have
        // to guess, and guessing "nullable" silently drops a NOT NULL constraint.
        var current = new SchemaVersion([
            new TableSchema("Users", null, [new ColumnSchema("Email", "varchar(100)", false, false)], [], [])
        ]);
        var desired = new SchemaVersion([
            new TableSchema("Users", null, [new ColumnSchema("Email", "varchar(200)", false, false)], [], [])
        ]);

        var diff = new SchemaDiffEngine().ComputeDiff(desired, current);

        diff.Changes.OfType<AlterColumnType>().Should().ContainSingle()
            .Which.IsNullable.Should().BeFalse();
    }
}
