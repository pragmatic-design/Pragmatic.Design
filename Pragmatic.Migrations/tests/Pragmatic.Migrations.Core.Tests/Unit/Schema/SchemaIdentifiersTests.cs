using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Core.Tests.Unit.Schema;

/// <summary>
///     A name over the database's limit is fitted as EF Core fits it (the first limit − 1 characters, then <c>~</c>), in every place a schema
///     names something; the database's own name for it is then the schema's.
/// </summary>
public class SchemaIdentifiersTests
{
    // 70 characters: EF Core's index on __OutboxMessages, which a generated schema declares.
    private const string OutboxIndex = "IX___OutboxMessages_ProcessedAt_NextAttemptAt_ClaimedUntil_CreatedAt";

    [Fact]
    public void Fit_ANameOverTheLimit_KeepsTheFirstLimitMinusOneCharactersAndEndsWithATilde()
    {
        SchemaIdentifiers.Fit(OutboxIndex, 64).Should().Be("IX___OutboxMessages_ProcessedAt_NextAttemptAt_ClaimedUntil_Crea~");
        SchemaIdentifiers.Fit(OutboxIndex, 64).Length.Should().Be(64);
    }

    [Fact]
    public void Fit_ANameWithinTheLimit_IsLeftAsItIs()
        => SchemaIdentifiers.Fit("IX_Orders_Total", 64).Should().Be("IX_Orders_Total");

    [Fact]
    public void FitTo_WithoutALimit_OrWithNoNameOverIt_ReturnsTheSameSchema()
    {
        var schema = Schema(Outbox(OutboxIndex, "FK_Outbox_OrderId_Orders", "CK_Outbox_Positive"));

        SchemaIdentifiers.FitTo(schema, null).Should().BeSameAs(schema);
        SchemaIdentifiers.FitTo(schema, 128).Should().BeSameAs(schema);
    }

    [Fact]
    public void FitTo_FitsTheIndexTheForeignKeyAndTheCheck_AndKeepsTheRest()
    {
        var longForeignKey = "FK_" + new string('F', 70);
        var longCheck = "CK_" + new string('C', 70);
        var schema = new SchemaVersion([Outbox(OutboxIndex, longForeignKey, longCheck)], "Orders", "MySql", "ConnectionStrings:Orders");

        var fitted = SchemaIdentifiers.FitTo(schema, 64);

        var table = fitted.Tables.Single();
        table.Indexes.Single().Name.Should().Be(SchemaIdentifiers.Fit(OutboxIndex, 64));
        table.ForeignKeys.Single().Name.Should().Be(SchemaIdentifiers.Fit(longForeignKey, 64));
        table.CheckConstraints.Single().Name.Should().Be(SchemaIdentifiers.Fit(longCheck, 64));
        (fitted.DatabaseName, fitted.ProviderName, fitted.ConfigKey).Should().Be(("Orders", "MySql", "ConnectionStrings:Orders"));
    }

    [Fact]
    public void FitTo_HashesTheFittedNames_NotTheOnesTheSchemaWasHashedWithBefore()
    {
        var longCheck = "CK_" + new string('C', 70);
        var schema = Schema(Outbox(OutboxIndex, "FK_Outbox_OrderId_Orders", longCheck));
        _ = schema.Hash; // cached on the unfitted schema first, as the runner's log line does

        var fitted = SchemaIdentifiers.FitTo(schema, 64);

        fitted.Hash.Should().Be(Schema(Outbox(SchemaIdentifiers.Fit(OutboxIndex, 64), "FK_Outbox_OrderId_Orders", SchemaIdentifiers.Fit(longCheck, 64))).Hash);
    }

    private static SchemaVersion Schema(params TableSchema[] tables) => new([.. tables]);

    private static TableSchema Outbox(string index, string foreignKey, string check) => new("Outbox", null,
        [new ColumnSchema("Id", "char(36)", false, true), new ColumnSchema("OrderId", "char(36)", false, false), new ColumnSchema("Total", "int", false, false)],
        [new IndexSchema(index, ImmutableArray.Create("Total"))],
        [new ForeignKeySchema(foreignKey, "OrderId", "Orders", "Id", ReferentialAction.Cascade)],
        [new CheckConstraintSchema(check, "`Total` >= 0")]);
}
