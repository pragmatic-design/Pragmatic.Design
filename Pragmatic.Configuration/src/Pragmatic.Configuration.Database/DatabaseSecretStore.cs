using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Configuration.Database.Audit;
using Pragmatic.Configuration.Database.Dialects;
using Pragmatic.Configuration.Database.Encryption;
using Pragmatic.Configuration.Database.Schema;
using Pragmatic.Cryptography;

namespace Pragmatic.Configuration.Database;

/// <summary>
///     ISecretStore backed by a relational database with AES-256-GCM encryption at rest.
/// </summary>
internal sealed partial class DatabaseSecretStore(
    IDbConnectionFactory connectionFactory,
    ISqlDialect dialect,
    ISecretEncryptor encryptor,
    Audit.ConfigurationAuditRecorder audit,
    ConfigurationSchemaManager schema,
    IOptions<DatabaseConfigurationOptions> options,
    ILogger<DatabaseSecretStore> logger)
    : ISecretStore, IWritableSecretStore
{
    private readonly DatabaseConfigurationOptions _options = options.Value;

    // One-time schema initialization
    private Task? _initTask;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public async Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
        => await GetSecretInternalAsync(key, null, ct).ConfigureAwait(false);

    public async Task<string?> GetSecretAsync(string key, string tenantId, CancellationToken ct = default)
        => await GetSecretInternalAsync(key, tenantId, ct).ConfigureAwait(false);

    /// <summary>
    ///     Sets (creates or updates) an encrypted secret value.
    /// </summary>
    public async Task SetSecretAsync(string key, string value, string? tenantId = null, CancellationToken ct = default)
    {
        var connection = connectionFactory.CreateConnection();
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await EnsureInitializedAsync(connection, ct).ConfigureAwait(false);

            // Bind the ciphertext to the secret's identity so a DB-write attacker can't move an
            // encrypted value from one secret to another and have it still authenticate.
            var encrypted = encryptor.Encrypt(value, SecretAad.For(key, tenantId));

            // Upsert + audit share one transaction so the secret and its audit record commit atomically.
            var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var command = connection.CreateCommand();
                await using (command.ConfigureAwait(false))
                {
                    command.Transaction = transaction;
                    command.CommandText = dialect.SetSecret;

                    command.AddParameter("@key", key);
                    command.AddParameter("@encryptedValue", encrypted);
                    command.AddParameter("@tenantId", tenantId);
                    command.AddParameter("@updatedBy", _options.AuditUser);

                    await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await audit.RecordAsync(connection, transaction, "secret", key, tenantId, "set", null, _options.AuditUser, ct).ConfigureAwait(false);

                await transaction.CommitAsync(ct).ConfigureAwait(false);
                LogSecretSet(key, tenantId ?? "(base)");
            }
        }
    }

    /// <summary>
    ///     Deletes a secret and records the deletion in the audit log (same transaction). Idempotent:
    ///     deleting a missing secret leaves no row and still writes the audit entry.
    /// </summary>
    public async Task DeleteSecretAsync(string key, string? tenantId = null, CancellationToken ct = default)
    {
        var connection = connectionFactory.CreateConnection();
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await EnsureInitializedAsync(connection, ct).ConfigureAwait(false);

            var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var command = connection.CreateCommand();
                await using (command.ConfigureAwait(false))
                {
                    command.Transaction = transaction;
                    command.CommandText = dialect.DeleteSecret;

                    command.AddParameter("@key", key);
                    command.AddParameter("@tenantId", tenantId);

                    await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await audit.RecordAsync(connection, transaction, "secret", key, tenantId, "deleted", "(encrypted)", _options.AuditUser, ct).ConfigureAwait(false);

                await transaction.CommitAsync(ct).ConfigureAwait(false);
                LogSecretDeleted(key, tenantId ?? "(base)");
            }
        }
    }

    private async Task<string?> GetSecretInternalAsync(string key, string? tenantId, CancellationToken ct)
    {
        var connection = connectionFactory.CreateConnection();
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await EnsureInitializedAsync(connection, ct).ConfigureAwait(false);

            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = dialect.GetSecret;

                command.AddParameter("@key", key);
                command.AddParameter("@tenantId", tenantId);

                var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);

                if (result is DBNull or null)
                    return null;

                var encrypted = (byte[])result;

                // Fail closed but gracefully: a tampered value, or one encrypted under a rotated key,
                // returns null (secret unavailable) and is logged, rather than throwing to the caller.
                if (encryptor.TryDecrypt(encrypted, out var plain, SecretAad.For(key, tenantId)))
                    return plain;

                LogSecretDecryptFailed(key, tenantId ?? "(base)");
                return null;
            }
        }
    }

    /// <summary>
    ///     Ensures the database schema is created exactly once, regardless of concurrent callers.
    /// </summary>
    private async Task EnsureInitializedAsync(DbConnection connection, CancellationToken ct)
    {
        if (_initTask is { IsCompletedSuccessfully: true }) return;

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initTask is { IsFaulted: true })
                _initTask = null;

            _initTask ??= schema.EnsureCreatedAsync(connection, ct);
            await _initTask.ConfigureAwait(false);
        }
        finally
        {
            _initLock.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Secret set: {Key} (tenant: {TenantId})")]
    private partial void LogSecretSet(string key, string tenantId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Secret deleted: {Key} (tenant: {TenantId})")]
    private partial void LogSecretDeleted(string key, string tenantId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Secret decryption failed for {Key} (tenant: {TenantId}) — tampered value or wrong/rotated key; returning null")]
    private partial void LogSecretDecryptFailed(string key, string tenantId);
}
