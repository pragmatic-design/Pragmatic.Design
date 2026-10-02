namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     The provider call a generated database registration makes: <c>options.UseNpgsql(…)</c> and its arguments.
/// </summary>
/// <remarks>
///     <para>
///         One place, because three emitters make this call — the host's DbContext registration and the two
///         migration paths of the entry point — and each carried its own copy of the provider switch.
///     </para>
///     <para>
///         A server provider gets <c>EnableRetryOnFailure()</c>. Without it the first command on a pooled
///         connection the server has closed — a database restart, a failover, an idle timeout — answers 500,
///         and the application has no place to ask for the retry: this registration is generated. An
///         application that does not want it turns it off through <c>UseDatabase</c>, which is applied after
///         this call.
///     </para>
///     <para>
///         SQLite has no retrying strategy to enable, and InMemory has no connection to lose.
///     </para>
///     <para>
///         MySql is Oracle's <c>MySql.EntityFrameworkCore</c>, the only MySQL provider on EF Core 10, whose
///         entry point is <c>UseMySQL</c>. The name written here was Pomelo's <c>UseMySql</c>, which stops at
///         EF Core 9: no MySQL provider was referenced anywhere, so the line had never been compiled, and a
///         MySql host failed with CS1061 inside a generated file. The call is now compiled against the
///         provider (<c>TheProviderCallCompilesTests</c>), and gets the retry the other server providers get.
///     </para>
/// </remarks>
internal static class DatabaseProviderCall
{
    private const string Retry = "providerOptions => providerOptions.EnableRetryOnFailure()";

    public static string UseMethod(string? provider) =>
        provider switch
        {
            "SqlServer" => "UseSqlServer",
            "PostgreSql" => "UseNpgsql",
            "SQLite" => "UseSqlite",
            "MySql" => "UseMySQL",
            "InMemory" => "UseInMemoryDatabase",
            _ => "UseSqlServer"
        };

    /// <summary>
    ///     The arguments of <see cref="UseMethod" />: <paramref name="connection" />, then the retry when the
    ///     provider has one. A <see langword="null" /> connection is written as a comment, which keeps the
    ///     line compiling on the provider's options-only overload.
    /// </summary>
    /// <param name="provider">The declared provider.</param>
    /// <param name="connection">The expression that reads the connection string, or <see langword="null" />.</param>
    /// <param name="configKey">
    ///     The key <paramref name="connection" /> reads, when it reads one: its value can be absent.
    /// </param>
    /// <remarks>
    ///     <c>UseMySQL</c> is the one entry point whose connection string is a non-nullable <c>string</c>, and
    ///     <c>configuration[key]</c> is a <c>string?</c>: passed as it is, the generated line is a CS8604 in
    ///     every MySql application, and an error in one built with warnings as errors. The read is completed
    ///     with a throw naming the key — the failure an absent value reaches anyway, in the provider's own
    ///     argument check, which cannot say which key was missing.
    /// </remarks>
    /// <param name="retry">
    ///     <c>false</c> for a context that enlists in a transaction another context began — the audit trail's:
    ///     a retrying strategy refuses to run inside a transaction it did not start.
    /// </param>
    public static string Arguments(string? provider, string? connection, string? configKey, bool retry = true)
    {
        var useMethod = UseMethod(provider);
        var retries = retry && useMethod is "UseSqlServer" or "UseNpgsql" or "UseMySQL";
        if (connection is null)
            return retries
                ? $"/* connection string config key not set */ {Retry}"
                : "/* connection string config key not set */";

        if (useMethod == "UseMySQL" && configKey is not null)
            connection = $"{connection} ?? throw new global::System.InvalidOperationException("
                         + $"\"The connection string '{configKey}' is not configured.\")";

        return retries ? $"{connection}, {Retry}" : connection;
    }
}
