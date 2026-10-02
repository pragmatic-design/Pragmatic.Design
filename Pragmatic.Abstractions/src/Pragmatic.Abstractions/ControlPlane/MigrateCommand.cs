namespace Pragmatic.ControlPlane;

/// <summary>
///     Instructs a host to run database migrations.
/// </summary>
/// <param name="DatabaseFilter">Optional: only migrate this database. Null = all databases.</param>
public sealed record MigrateCommand(string? DatabaseFilter = null) : HostCommand
{
    /// <inheritdoc />
    public override string CommandTypeName => nameof(MigrateCommand);
}
