using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary><c>[Index]</c> (#524): the same non-unique index on both branches.</summary>
public partial class TheSqlEnginesSchemaIsPragmaticsTests
{
    /// <summary>A tenant's assignment with two declared indexes, one composite: the columns as written, the tenant not added.</summary>
    private const string IndexedEntity = """
        global using System;
        using Pragmatic.Persistence.Entity;

        namespace Probe.Entities;

        [Entity]
        [Index(nameof(UserId), nameof(RoleName))]
        [Index(nameof(RoleName))]
        public partial class Assignment : IEntity, Pragmatic.MultiTenancy.ITenantEntity
        {
            public string UserId { get; private set; } = "";
            public string RoleName { get; private set; } = "";
            public string TenantId { get; set; } = "";
        }
        """;

    /// <summary>
    ///     The declared indexes on the Sql engine are EF Core's: the same names, columns and order, not unique, no filter. The control is
    ///     EF Core's side holding them: an equality of two schemas without them would pass too.
    /// </summary>
    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    public void AnIndex_IsTheSameIndexOnBothBranches(string provider)
    {
        var efCore = Describe(EfCoreSchema(provider, IndexedEntity)).ToList();
        var (sql, diagnostics) = SqlSchema(provider, IndexedEntity);
        diagnostics.Should().NotContain(d => d.Id == "PRAG3027", "an [Index] is described, not refused");
        var engine = Describe(sql).ToList();

        efCore.Should().Contain(l => l.Trim() == "index IX_Assignments_UserId_RoleName (UserId, RoleName)", "the control: the composite index is there, not unique");
        efCore.Should().Contain(l => l.Trim() == "index IX_Assignments_RoleName (RoleName)", "the control: the single-column index is there");
        efCore.Except(engine).Concat(engine.Except(efCore)).Should().BeEmpty();
    }
}
