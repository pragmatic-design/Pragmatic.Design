using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Introspection;

namespace Pragmatic.Migrations.Core.Tests.Unit.Introspection;

/// <summary>
///     SQLite reports no check constraint through a PRAGMA: they are read back from the CREATE TABLE text
///     in <c>sqlite_master</c>. The text is whatever created the table, so the reader has to find the
///     named checks at the level of the column list and nowhere else.
/// </summary>
public class SqliteCheckConstraintTextTests
{
    [Fact]
    public void A_named_check_is_read_with_its_name_and_expression()
    {
        var checks = SqliteSchemaIntrospector.ExtractCheckConstraints("""
            CREATE TABLE "StaffAssignments" (
                "Id" TEXT NOT NULL,
                "ValidFrom" TEXT NOT NULL,
                "ValidTo" TEXT NULL,
                PRIMARY KEY ("Id"),
                CONSTRAINT "FK_StaffAssignments_PropertyId_Properties" FOREIGN KEY ("Id") REFERENCES "Properties" ("Id") ON DELETE CASCADE,
                CONSTRAINT "CK_StaffAssignments_ValidRange" CHECK ("ValidTo" IS NULL OR ("ValidTo" >= "ValidFrom"))
            )
            """);

        checks.Select(c => (c.Name, c.Expression)).Should().Equal(new[]
        {
            ("CK_StaffAssignments_ValidRange", "\"ValidTo\" IS NULL OR (\"ValidTo\" >= \"ValidFrom\")"),
        });
    }

    [Fact]
    public void Quoted_names_unquote_and_what_only_looks_like_a_check_is_not_one()
    {
        var checks = SqliteSchemaIntrospector.ExtractCheckConstraints("""
            CREATE TABLE t (
                a INTEGER DEFAULT 0 CONSTRAINT [CK a b] CHECK (a > 0),
                note TEXT DEFAULT 'CONSTRAINT x CHECK (1)',
                "CONSTRAINT" INTEGER,
                CHECK (a < 10),
                constraint `CK_t_b` check(a <> ')')
            )
            """);

        checks.Select(c => c.Name).Should().Equal(new[] { "CK a b", "CK_t_b" });
        checks.Last().Expression.Should().Be("a <> ')'");
    }
}
