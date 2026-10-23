namespace Pragmatic.Migrations.Sql;

/// <summary>
///     How a provider writes a named parameter in the statement text, and what name the parameter object carries.
/// </summary>
/// <remarks>
///     ⚠️ Oracle's driver rejects <c>@name</c> in the text: it is <c>:name</c>, with the parameter named <c>name</c>. The
///     statements that use this name each parameter once, so they bind the same whether the driver binds by name or by position
///     (Oracle's default).
/// </remarks>
internal static class SqlParameterSyntax
{
    /// <summary>The parameter <paramref name="name" /> as the statement text spells it on <paramref name="providerName" />.</summary>
    public static string Placeholder(string? providerName, string name)
        => IsOracle(providerName) ? ":" + name : "@" + name;

    /// <summary>The name the parameter object carries on <paramref name="providerName" />.</summary>
    public static string ParameterName(string? providerName, string name)
        => IsOracle(providerName) ? name : "@" + name;

    private static bool IsOracle(string? providerName)
        => string.Equals(providerName, MigrationConstants.ProviderOracle, StringComparison.OrdinalIgnoreCase);
}
