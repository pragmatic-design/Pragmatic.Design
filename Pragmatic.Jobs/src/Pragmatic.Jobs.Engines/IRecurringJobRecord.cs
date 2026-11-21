namespace Pragmatic.Jobs.Engines;

/// <summary>The fields of a stored recurring definition, as the Sql row and the document share them (<see cref="JobRecords" />).</summary>
public interface IRecurringJobRecord
{
    string Id { get; set; }

    string JobType { get; set; }

    string CronExpression { get; set; }

    string? ParametersJson { get; set; }

    string? ParameterType { get; set; }

    DateTimeOffset? LastExecutedAt { get; set; }

    DateTimeOffset? NextExecutionAt { get; set; }

    bool IsEnabled { get; set; }

    string? TenantId { get; set; }

    string? TimeZoneId { get; set; }

    /// <summary>The <see cref="Attributes.MisfirePolicy" />, by number.</summary>
    int MisfirePolicy { get; set; }
}
