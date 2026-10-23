namespace Pragmatic.Migrations.Runner;

/// <summary>The lock table's statements, per provider; PostgreSQL's is the default.</summary>
public sealed partial class DatabaseLeaderElection
{
    private string BuildCreateTableSql() => _providerName switch
    {
        "SqlServer" => """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '__PragmaticLock')
            CREATE TABLE __PragmaticLock (
                LockName    NVARCHAR(128) NOT NULL PRIMARY KEY,
                HolderId    NVARCHAR(64)  NULL,
                AcquiredAt  DATETIMEOFFSET NULL,
                ExpiresAt   DATETIMEOFFSET NULL
            )
            """,
        "Sqlite" => """
            CREATE TABLE IF NOT EXISTS __PragmaticLock (
                LockName    TEXT NOT NULL PRIMARY KEY,
                HolderId    TEXT NULL,
                AcquiredAt  TEXT NULL,
                ExpiresAt   TEXT NULL
            )
            """,
        // Backticks: MySQL's own quoting, whatever the session's sql_mode. The times are UTC.
        MigrationConstants.ProviderMySql => """
            CREATE TABLE IF NOT EXISTS `__PragmaticLock` (
                `LockName`    VARCHAR(128) NOT NULL PRIMARY KEY,
                `HolderId`    VARCHAR(64)  NULL,
                `AcquiredAt`  DATETIME(6)  NULL,
                `ExpiresAt`   DATETIME(6)  NULL
            )
            """,
        // One statement, no terminating semicolon: Oracle's driver runs a single statement per command.
        MigrationConstants.ProviderOracle => """
            CREATE TABLE IF NOT EXISTS "__PragmaticLock" (
                "LockName"    VARCHAR2(128) NOT NULL PRIMARY KEY,
                "HolderId"    VARCHAR2(64)  NULL,
                "AcquiredAt"  TIMESTAMP     NULL,
                "ExpiresAt"   TIMESTAMP     NULL
            )
            """,
        _ => // PostgreSQL (default)
            """
            CREATE TABLE IF NOT EXISTS "__PragmaticLock" (
                "LockName"    VARCHAR(128) NOT NULL PRIMARY KEY,
                "HolderId"    VARCHAR(64)  NULL,
                "AcquiredAt"  TIMESTAMPTZ  NULL,
                "ExpiresAt"   TIMESTAMPTZ  NULL
            )
            """,
    };

    private string BuildSeedSql() => _providerName switch
    {
        "SqlServer" => """
            IF NOT EXISTS (SELECT 1 FROM __PragmaticLock WHERE LockName = 'migration')
            INSERT INTO __PragmaticLock (LockName) VALUES ('migration')
            """,
        "Sqlite" =>
            "INSERT OR IGNORE INTO __PragmaticLock (LockName) VALUES ('migration')",
        MigrationConstants.ProviderMySql =>
            "INSERT IGNORE INTO `__PragmaticLock` (`LockName`) VALUES ('migration')",
        MigrationConstants.ProviderOracle => """
            INSERT INTO "__PragmaticLock" ("LockName")
            SELECT 'migration' FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM "__PragmaticLock" WHERE "LockName" = 'migration')
            """,
        _ => // PostgreSQL
            """INSERT INTO "__PragmaticLock" ("LockName") VALUES ('migration') ON CONFLICT ("LockName") DO NOTHING""",
    };

    private string BuildTryAcquireSql() => _providerName switch
    {
        "SqlServer" => """
            UPDATE __PragmaticLock
            SET HolderId = @holderId,
                AcquiredAt = SYSUTCDATETIME(),
                ExpiresAt = DATEADD(MINUTE, @timeoutMinutes, SYSUTCDATETIME())
            WHERE LockName = 'migration'
              AND (HolderId IS NULL OR HolderId = @holderId OR ExpiresAt < SYSUTCDATETIME())
            """,
        "Sqlite" => """
            UPDATE __PragmaticLock
            SET HolderId = @holderId,
                AcquiredAt = datetime('now'),
                ExpiresAt = datetime('now', '+' || @timeoutMinutes || ' minutes')
            WHERE LockName = 'migration'
              AND (HolderId IS NULL OR HolderId = @holderId OR ExpiresAt < datetime('now'))
            """,
        // MySqlConnector reports the rows matched, not the rows changed, so a re-acquisition by the holder counts.
        MigrationConstants.ProviderMySql => """
            UPDATE `__PragmaticLock`
            SET `HolderId` = @holderId,
                `AcquiredAt` = UTC_TIMESTAMP(6),
                `ExpiresAt` = DATE_ADD(UTC_TIMESTAMP(6), INTERVAL @timeoutMinutes MINUTE)
            WHERE `LockName` = 'migration'
              AND (`HolderId` IS NULL OR `HolderId` = @holderId OR `ExpiresAt` < UTC_TIMESTAMP(6))
            """,
        // Each parameter named once (holderIdAgain): the statement binds the same by name or by position.
        MigrationConstants.ProviderOracle => """
            UPDATE "__PragmaticLock"
            SET "HolderId" = :holderId,
                "AcquiredAt" = SYS_EXTRACT_UTC(SYSTIMESTAMP),
                "ExpiresAt" = SYS_EXTRACT_UTC(SYSTIMESTAMP) + NUMTODSINTERVAL(:timeoutMinutes, 'MINUTE')
            WHERE "LockName" = 'migration'
              AND ("HolderId" IS NULL OR "HolderId" = :holderIdAgain OR "ExpiresAt" < SYS_EXTRACT_UTC(SYSTIMESTAMP))
            """,
        _ => // PostgreSQL
            """
            UPDATE "__PragmaticLock"
            SET "HolderId" = @holderId,
                "AcquiredAt" = NOW(),
                "ExpiresAt" = NOW() + MAKE_INTERVAL(mins => @timeoutMinutes)
            WHERE "LockName" = 'migration'
              AND ("HolderId" IS NULL OR "HolderId" = @holderId OR "ExpiresAt" < NOW())
            """,
    };

    private string BuildReleaseSql() => _providerName switch
    {
        "SqlServer" => """
            UPDATE __PragmaticLock
            SET HolderId = NULL, AcquiredAt = NULL, ExpiresAt = NULL
            WHERE LockName = 'migration' AND HolderId = @holderId
            """,
        "Sqlite" => """
            UPDATE __PragmaticLock
            SET HolderId = NULL, AcquiredAt = NULL, ExpiresAt = NULL
            WHERE LockName = 'migration' AND HolderId = @holderId
            """,
        MigrationConstants.ProviderMySql => """
            UPDATE `__PragmaticLock`
            SET `HolderId` = NULL, `AcquiredAt` = NULL, `ExpiresAt` = NULL
            WHERE `LockName` = 'migration' AND `HolderId` = @holderId
            """,
        MigrationConstants.ProviderOracle => """
            UPDATE "__PragmaticLock"
            SET "HolderId" = NULL, "AcquiredAt" = NULL, "ExpiresAt" = NULL
            WHERE "LockName" = 'migration' AND "HolderId" = :holderId
            """,
        _ => // PostgreSQL
            """
            UPDATE "__PragmaticLock"
            SET "HolderId" = NULL, "AcquiredAt" = NULL, "ExpiresAt" = NULL
            WHERE "LockName" = 'migration' AND "HolderId" = @holderId
            """,
    };

    // Returns the current HolderId when the lock IS held and not yet expired.
    // WaitForLeaderCompletionAsync loops until the result is NULL/DBNull (lock released/expired).
    private string BuildCheckSql() => _providerName switch
    {
        "SqlServer" =>
            "SELECT HolderId FROM __PragmaticLock WHERE LockName = 'migration' AND HolderId IS NOT NULL AND ExpiresAt >= SYSUTCDATETIME()",
        "Sqlite" =>
            "SELECT HolderId FROM __PragmaticLock WHERE LockName = 'migration' AND HolderId IS NOT NULL AND ExpiresAt >= datetime('now')",
        MigrationConstants.ProviderMySql =>
            "SELECT `HolderId` FROM `__PragmaticLock` WHERE `LockName` = 'migration' AND `HolderId` IS NOT NULL AND `ExpiresAt` >= UTC_TIMESTAMP(6)",
        MigrationConstants.ProviderOracle =>
            """SELECT "HolderId" FROM "__PragmaticLock" WHERE "LockName" = 'migration' AND "HolderId" IS NOT NULL AND "ExpiresAt" >= SYS_EXTRACT_UTC(SYSTIMESTAMP)""",
        _ => // PostgreSQL
            """SELECT "HolderId" FROM "__PragmaticLock" WHERE "LockName" = 'migration' AND "HolderId" IS NOT NULL AND "ExpiresAt" >= NOW()""",
    };
}
