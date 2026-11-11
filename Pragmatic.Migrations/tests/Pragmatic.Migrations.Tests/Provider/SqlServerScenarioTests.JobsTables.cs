#pragma warning disable CA2007 // ConfigureAwait in test code

using System.Collections.Immutable;
using Pragmatic.Migrations.Schema;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Migrations.Tests.Provider;

public partial class SqlServerScenarioTests
{
    /// <summary>
    ///     The jobs' tables as the generator declares them on SQL Server: created, and read back as declared.
    /// </summary>
    /// <remarks>
    ///     The columns are those of <c>SchemaMetadataTransform.BuildJobsTable</c> and <c>BuildRecurringJobsTable</c> for SQL Server,
    ///     which <c>TheJobsTablesFitSqlServerTests</c> in the generator's suite pins. Copied, not generated: this suite does not run
    ///     the schema transform. <c>ParametersJson</c> is capped at 65535 and <c>Error</c> at 4096 by the configuration, both over SQL
    ///     Server's 4000, so both are <c>nvarchar(max)</c>.
    /// </remarks>
    [Fact]
    public async Task TheJobsTables_AsTheGeneratorDeclaresThem_AreCreatedAndConverge()
    {
        await using var conn = await CreateConnectionAsync();
        var schema = MakeSchema(JobsTable("nvarchar(max)", "nvarchar(max)"), RecurringJobsTable("nvarchar(max)"));

        var result = await ApplySchemaAsync(conn, schema);
        result.Success.Should().BeTrue(result.Error);

        _diffEngine.ComputeDiff(schema, await CreateIntrospector().IntrospectAsync(conn)).Changes.Select(change => change.ToString())
            .Should().BeEmpty("the database holds the jobs' tables as the schema declared them");
    }

    /// <summary>
    ///     The declaration the generator wrote before: SQL Server refuses <c>nvarchar(65535)</c>, so the migration fails and a host with
    ///     jobs does not start. It is what makes the test above a statement about SQL Server and not about the runner.
    /// </summary>
    [Fact]
    public async Task TheJobsTables_WithALengthOverSqlServersLimit_AreRefused()
    {
        await using var conn = await CreateConnectionAsync();

        var result = await ApplySchemaAsync(conn, MakeSchema(JobsTable("nvarchar(65535)", "nvarchar(4096)")));

        result.Success.Should().BeFalse("SQL Server's nvarchar holds at most 4000 characters, or max");
        // The database's own refusal, which names the size: not a failure of anything else on the way.
        result.Error.Should().Contain("65535");
    }

    private static TableSchema JobsTable(string parametersJson, string error) =>
        new("__Jobs", "dbo",
            ImmutableArray.Create(
                new ColumnSchema("Id", "uniqueidentifier", false, true),
                new ColumnSchema("JobType", "nvarchar(512)", false, false),
                new ColumnSchema("ParametersJson", parametersJson, true, false),
                new ColumnSchema("ParameterType", "nvarchar(512)", true, false),
                new ColumnSchema("Status", "int", false, false),
                new ColumnSchema("ScheduledFor", "datetimeoffset", false, false),
                new ColumnSchema("Priority", "int", false, false),
                new ColumnSchema("StartedAt", "datetimeoffset", true, false),
                new ColumnSchema("CompletedAt", "datetimeoffset", true, false),
                new ColumnSchema("Error", error, true, false),
                new ColumnSchema("Attempt", "int", false, false),
                new ColumnSchema("MaxAttempts", "int", false, false),
                new ColumnSchema("LeaseExpiresAt", "datetimeoffset", true, false),
                new ColumnSchema("LeasedBy", "nvarchar(128)", true, false),
                new ColumnSchema("ContinuationJobType", "nvarchar(512)", true, false),
                new ColumnSchema("ContinuationParametersJson", "nvarchar(max)", true, false),
                new ColumnSchema("CorrelationId", "nvarchar(64)", true, false),
                new ColumnSchema("TenantId", "nvarchar(128)", true, false),
                new ColumnSchema("CreatedAt", "datetimeoffset", false, false)),
            ImmutableArray.Create(
                new IndexSchema("IX___Jobs_Status_Priority_ScheduledFor", ImmutableArray.Create("Status", "Priority", "ScheduledFor")),
                new IndexSchema("IX___Jobs_Status_LeaseExpiresAt", ImmutableArray.Create("Status", "LeaseExpiresAt")),
                new IndexSchema("IX___Jobs_CorrelationId", ImmutableArray.Create("CorrelationId")),
                new IndexSchema("IX___Jobs_CompletedAt", ImmutableArray.Create("CompletedAt"))),
            []);

    private static TableSchema RecurringJobsTable(string parametersJson) =>
        new("__RecurringJobs", "dbo",
            ImmutableArray.Create(
                new ColumnSchema("Id", "nvarchar(256)", false, true),
                new ColumnSchema("JobType", "nvarchar(512)", false, false),
                new ColumnSchema("CronExpression", "nvarchar(128)", false, false),
                new ColumnSchema("ParametersJson", parametersJson, true, false),
                new ColumnSchema("ParameterType", "nvarchar(512)", true, false),
                new ColumnSchema("LastExecutedAt", "datetimeoffset", true, false),
                new ColumnSchema("NextExecutionAt", "datetimeoffset", true, false),
                new ColumnSchema("IsEnabled", "bit", false, false),
                new ColumnSchema("TenantId", "nvarchar(128)", true, false),
                new ColumnSchema("TimeZoneId", "nvarchar(64)", true, false),
                new ColumnSchema("MisfirePolicy", "int", false, false)),
            ImmutableArray.Create(
                new IndexSchema("IX___RecurringJobs_IsEnabled_NextExecutionAt", ImmutableArray.Create("IsEnabled", "NextExecutionAt"))),
            []);
}
