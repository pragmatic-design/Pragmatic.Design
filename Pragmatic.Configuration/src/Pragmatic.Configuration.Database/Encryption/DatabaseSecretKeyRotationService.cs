using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Configuration.Database.Dialects;
using Pragmatic.Configuration.Database.Schema;
using Pragmatic.Cryptography;

namespace Pragmatic.Configuration.Database.Encryption;

/// <summary>
///     Re-encrypts all stored secrets under the current key. Decryption uses the full key ring (current +
///     previous), so values written before a rotation are read with the old key and rewritten with the new
///     one within a single connection.
/// </summary>
internal sealed partial class DatabaseSecretKeyRotationService(
    IDbConnectionFactory connectionFactory,
    ISqlDialect dialect,
    ISecretEncryptor encryptor,
    ConfigurationSchemaManager schema,
    IOptions<DatabaseConfigurationOptions> options,
    ILogger<DatabaseSecretKeyRotationService> logger)
    : ISecretKeyRotationService
{
    private readonly DatabaseConfigurationOptions _options = options.Value;

    public async Task<SecretRotationReport> ReEncryptAllAsync(CancellationToken ct = default)
    {
        var connection = connectionFactory.CreateConnection();
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await schema.EnsureCreatedAsync(connection, ct).ConfigureAwait(false);

            var rows = await ReadAllAsync(connection, ct).ConfigureAwait(false);

            var reEncrypted = 0;
            var failed = 0;
            foreach (var row in rows)
            {
                var aad = SecretAad.For(row.Key, row.TenantId);
                if (!encryptor.TryDecrypt(row.Encrypted, out var plain, aad))
                {
                    // Undecryptable under every ring key: leave as-is (do not lose the value).
                    failed++;
                    LogRotationSkipped(row.Key, row.TenantId ?? "(base)");
                    continue;
                }

                var reencrypted = encryptor.Encrypt(plain, aad);
                await UpdateAsync(connection, row.Key, reencrypted, row.TenantId, ct).ConfigureAwait(false);
                reEncrypted++;
            }

            LogRotationComplete(rows.Count, reEncrypted, failed);
            return new SecretRotationReport(rows.Count, reEncrypted, failed);
        }
    }

    private async Task<List<SecretRow>> ReadAllAsync(DbConnection connection, CancellationToken ct)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = dialect.GetAllSecrets;

            var rows = new List<SecretRow>();
            var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var key = reader.GetString(0);
                    var encrypted = (byte[])reader.GetValue(1);
                    var tenantId = reader.IsDBNull(2) ? null : reader.GetString(2);
                    rows.Add(new SecretRow(key, encrypted, tenantId));
                }
            }

            return rows;
        }
    }

    private async Task UpdateAsync(
        DbConnection connection, string key, byte[] encrypted, string? tenantId, CancellationToken ct)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = dialect.SetSecret;
            command.AddParameter("@key", key);
            command.AddParameter("@encryptedValue", encrypted);
            command.AddParameter("@tenantId", tenantId);
            command.AddParameter("@updatedBy", _options.AuditUser);

            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private readonly record struct SecretRow(string Key, byte[] Encrypted, string? TenantId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Secret re-encryption skipped for {Key} (tenant: {TenantId}) — undecryptable under any ring key")]
    private partial void LogRotationSkipped(string key, string tenantId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Secret key rotation complete: {Total} examined, {ReEncrypted} re-encrypted, {Failed} skipped")]
    private partial void LogRotationComplete(int total, int reEncrypted, int failed);
}
