namespace Pragmatic.Persistence.Sql;

/// <summary>The SQL the database speaks: quoting, paging, and what it stores the .NET types as.</summary>
/// <remarks>
///     Linked into Pragmatic.SourceGenerator as source, where it is internal: a public copy there would make a project that references
///     both the generator and this runtime (the generator's tests) fail with CS0433.
/// </remarks>
#if PRAGMATIC_SOURCE_GENERATOR
internal
#else
public
#endif
    enum SqlDialect
{
    Sqlite,
    PostgreSql,
    SqlServer,

    /// <summary>
    ///     MySQL 8 (and MariaDB where they agree), spoken with <c>sql_mode</c> ANSI_QUOTES and PIPES_AS_CONCAT on the session
    ///     (<c>SqlMySql.Prepare</c>): identifiers quoted with double quotes as everywhere but SQL Server, <c>LIMIT … OFFSET …</c>, no RETURNING.
    /// </summary>
    MySql,

    /// <summary>
    ///     Oracle Database 23ai (BOOLEAN, multi-row VALUES, <c>IF EXISTS</c>, SQL macros, VECTOR): identifiers in double quotes, parameters
    ///     <c>:pN</c> (the text keeps <c>@pN</c> and the session adapts it, <c>SqlOracle.Adapt</c>), <c>OFFSET … ROWS FETCH NEXT … ROWS ONLY</c>,
    ///     several statements run as one PL/SQL block returning their rows (<c>DBMS_SQL.RETURN_RESULT</c>).
    /// </summary>
    Oracle,
}
