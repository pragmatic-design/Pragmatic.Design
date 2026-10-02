using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.Authorization;
using Pragmatic.Identity;
using Pragmatic.Configuration;
using Pragmatic.Result;
using Pragmatic.Result.Http;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Configuration.Management.Actions;

/// <summary>
///     Command: sets (creates or updates) a configuration value.
/// </summary>
[DomainAction]
[RequirePermission("configuration.values.write")]
[BelongsTo<ConfigurationManagementPackage>]
public sealed partial class SetConfigValue : VoidDomainAction
{
    private IConfigurationStore _store = null!;
    private ICurrentUser _currentUser = null!;

    // Registered by AddPragmaticConfiguration, which the package's actions already require, and filled by
    // the generated host from the [Configuration] sections. An application that declares none has an
    // empty catalogue, and nothing is writable.
    private Pragmatic.Configuration.Discovery.IConfigurationCatalog _catalog = null!;

    /// <summary>Configuration key.</summary>
    public required string Key { get; set; }

    /// <summary>The value to set.</summary>
    public required string Value { get; set; }

    /// <summary>Optional tenant scope. Null = base value.</summary>
    public string? TenantId { get; set; }

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        ThrowIfNull(_store, nameof(_store));
        ThrowIfNull(_currentUser, nameof(_currentUser));

        // A null TenantId is the base value every tenant inherits: writing it is writing to all of them.
        if (!TenantBinding.Permits(_currentUser, TenantId))
            return VoidResult<IError>.Failure(ForbiddenError.ActionDenied("set-config-value", $"tenant:{TenantId ?? "base"}"));


        if (string.IsNullOrWhiteSpace(Key))
            return VoidResult<IError>.Failure(BadRequestError.Create("KeyRequired"));

        // IConfigurationStore leaves the key to its caller, and over HTTP the caller is this action: a key
        // of another shape reaches backends that interpret it, and one nothing declares is a setting nobody
        // reads. Only a declared property, or a key beneath one, is written.
        if (!HasTheShapeOfAKey(Key))
            return VoidResult<IError>.Failure(new BadRequestError { Reason = $"'{Key}' is not a configuration key: segments of letters, digits, '_', '.', '-', separated by ':'." });

        if (!IsDeclared(Key))
            return VoidResult<IError>.Failure(new BadRequestError { Reason = $"No [Configuration] section declares '{Key}'." });

        if (Value is null)
            return VoidResult<IError>.Failure(BadRequestError.Create("ValueRequired"));

        await _store.SetAsync(Key, Value, TenantId, ct).ConfigureAwait(false);
        return VoidResult<IError>.Success();
    }

    private const int MaxKeyLength = 256;

    private static bool HasTheShapeOfAKey(string key)
    {
        if (key.Length > MaxKeyLength)
            return false;

        foreach (var segment in key.Split(':'))
        {
            if (segment.Length == 0)
                return false;

            foreach (var c in segment)
            {
                if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-'))
                    return false;
            }
        }

        return true;
    }

    // Case-insensitive, as IConfiguration reads keys. A key beneath a property — Holidays:0, Retry:Count —
    // is that property's value too.
    private bool IsDeclared(string key)
    {
        foreach (var section in _catalog.Sections)
        {
            foreach (var property in section.Properties)
            {
                var declared = section.KeyOf(property);
                if (string.Equals(key, declared, StringComparison.OrdinalIgnoreCase)
                    || key.StartsWith(declared + ":", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
