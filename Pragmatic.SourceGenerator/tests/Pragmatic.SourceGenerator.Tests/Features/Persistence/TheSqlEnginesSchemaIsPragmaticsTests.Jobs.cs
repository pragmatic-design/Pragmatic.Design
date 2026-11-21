using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[EnableJobPersistence]</c> (#904): the Sql engine's <c>__Jobs</c> and <c>__RecurringJobs</c>, Pragmatic.Jobs.Engines' rows, are EF
///     Core's tables, column for column and index for index, so one database holds one job store whichever engine writes it.
/// </summary>
public partial class TheSqlEnginesSchemaIsPragmaticsTests
{
    /// <summary>A boundary keeping the job store, with one entity: a database's schema exists only where an entity lives.</summary>
    private const string JobsBoundary = """
        global using System;
        using Pragmatic.Persistence.Entity;

        namespace Probe.Work
        {
            [Pragmatic.Actions.Attributes.Boundary]
            [Pragmatic.Jobs.Attributes.EnableJobPersistence]
            public sealed partial class WorkBoundary;

            [Entity]
            public partial class Shift : IEntity
            {
                public string Name { get; private set; } = "";
            }
        }
        """;

    /// <summary>Pragmatic.Jobs.EFCore, by the type the generator recognises it by.</summary>
    private const string JobsEfCore = "namespace Pragmatic.Jobs.EFCore.Entities { public sealed class JobEntityTypeConfiguration { } }";

    private static readonly Type[] JobAttributes =
        [typeof(global::Pragmatic.Actions.Attributes.BoundaryAttribute), typeof(global::Pragmatic.Jobs.Attributes.EnableJobPersistenceAttribute)];

    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    public void TheJobTables_AreTheSameTablesOnBothBranches(string provider)
    {
        var (efOutput, _) = Run([JobsBoundary, EfCoreGate, Host, ProviderProbe(provider), JobsEfCore], JobAttributes);
        var efCore = Describe(Read(Generated(efOutput, "_Infra.Persistence.SchemaMetadata.g.cs"), "DefaultSchema", "Current")).ToList();
        var (sqlOutput, diagnostics) = Run([JobsBoundary, Host, SqlBridge, DriverProbe(provider), PackageSource("IJobRecord.cs"), PackageSource("IRecurringJobRecord.cs"),
            PackageSource("JobRow.cs"), PackageSource("RecurringJobRow.cs")],
            [typeof(global::Pragmatic.Persistence.Sql.SqlSession), .. JobAttributes]);
        diagnostics.Should().NotContain(d => d.Id == "PRAG3027", "the rows have a schema on the Sql engine");
        var engine = Describe(Read(Generated(sqlOutput, "_Infra.Persistence.SchemaMetadata.g.cs"), "DefaultSchema", "Current")).ToList();

        efCore.Should().Contain(l => l.Trim().StartsWith("index IX___Jobs_Status_Priority_ScheduledFor (Status, Priority, ScheduledFor)", StringComparison.Ordinal),
            "the control: EF Core's tables are there, with the poll's index");
        efCore.Should().Contain(l => l.Trim().StartsWith("index IX___RecurringJobs_IsEnabled_NextExecutionAt", StringComparison.Ordinal),
            "the control: and the schedules', with theirs");
        Jobs(efCore).Except(Jobs(engine)).Concat(Jobs(engine).Except(Jobs(efCore))).Should().BeEmpty();
    }

    /// <summary>The lines of the two job tables only: the boundary's own entity is compared by the other tests.</summary>
    private static List<string> Jobs(IEnumerable<string> schema)
        => [.. schema.Where(l => l.Contains("__Jobs", StringComparison.Ordinal) || l.Contains("__RecurringJobs", StringComparison.Ordinal))];
}
