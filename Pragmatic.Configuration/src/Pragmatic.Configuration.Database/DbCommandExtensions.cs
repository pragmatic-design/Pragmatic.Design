using System.Data.Common;

namespace Pragmatic.Configuration.Database;

/// <summary>
///     Shared helper for adding parameters to ADO.NET commands.
///     Eliminates duplication across DatabaseConfigurationStore and DatabaseSecretStore.
/// </summary>
internal static class DbCommandExtensions
{
    internal static void AddParameter(this DbCommand command, string name, object? value)
    {
        var param = command.CreateParameter();
        param.ParameterName = name;
        param.Value = value ?? DBNull.Value;
        command.Parameters.Add(param);
    }
}
