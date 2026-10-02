---
title: "Troubleshooting"
description: "Practical problem/solution guide for Pragmatic.Configuration. Each section covers a common issue, the likely causes, and the fix."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Configuration/docs/troubleshooting.md
sidebar:
  order: 5
---
Practical problem/solution guide for Pragmatic.Configuration. Each section covers a common issue, the likely causes, and the fix.

---

## Options Values Are All Default (Not Bound)

Your `[Configuration]` class compiles, but `IOptions<T>` returns default values instead of values from `appsettings.json`.

### Checklist

1. **Did you call the assembly aggregator?** Verify that `services.Add{Prefix}Configuration(configuration)` is called in your `IStartupStep`:

   ```csharp
   services.AddMyAppConfiguration(configuration);
   ```

2. **Does the section path match your JSON?** The section is inferred by removing the `Options` suffix. `BookingOptions` expects a `"Booking"` key in `appsettings.json`, not `"BookingOptions"`. Check with `[Configuration(SectionPath = "...")]` if unsure.

3. **Is the JSON structure correct?** The section must be a nested object, not a flat key:

   ```json
   // Correct
   { "Booking": { "MaxGuests": 50 } }

   // Wrong -- flat key, not bound
   { "Booking:MaxGuests": 50 }
   ```

4. **Is the `IConfiguration` instance the right one?** Ensure you pass `configuration` (from `IStartupStep` or `builder.Configuration`), not a stale or empty configuration root.

5. **Are the property names cased correctly?** Microsoft Configuration binding is case-insensitive, but the JSON property names must match after case normalization.

---

## PRAG2000: Class Must Be Partial

**Error:** `[Configuration] class 'BookingOptions' must be declared as partial`

**Fix:** Add the `partial` keyword:

```csharp
[Configuration]
public partial class BookingOptions { ... }
```

The SG cannot generate code for non-partial classes.

---

## PRAG2001: Class Cannot Be Static or Abstract

**Error:** `[Configuration] class 'BookingOptions' cannot be static or abstract`

**Fix:** Remove `static` or `abstract`:

```csharp
// Wrong
[Configuration]
public static class BookingOptions { ... }

// Right
[Configuration]
public partial class BookingOptions { ... }
```

Options classes must be instantiable by the Microsoft Options framework.

---

## PRAG2050: [Required] with Default Value

**Warning:** `Property has [Required] but also has a default value`

**Fix:** Decide the intent:
- If the property **must** be configured: keep `[Required]`, use empty/zero as the default.
- If the property has a valid **default**: remove `[Required]`.

See [Common Mistakes #9](/modules/configuration-module/common-mistakes/#9-using-required-with-a-default-value) for details.

---

## Database Store: Schema Not Created

The database store throws SQL errors about missing tables.

### Checklist

1. **Is `AutoCreateSchema` enabled?** Default is `true`, but verify:

   ```csharp
   services.AddDatabaseConfigurationStore(options =>
   {
       options.AutoCreateSchema = true;  // default
   });
   ```

2. **Does the connection have CREATE TABLE permissions?** The schema manager creates tables with `CREATE TABLE IF NOT EXISTS` (or equivalent per dialect). The database user must have DDL permissions.

3. **Is the correct provider set?** Each provider has different DDL syntax. `DatabaseProvider.PostgreSql` generates PostgreSQL-specific SQL:

   ```csharp
   options.Provider = DatabaseProvider.PostgreSql;  // must match your database
   ```

---

## Database Store: Secret Decryption Fails

`GetSecretAsync` throws a cryptography exception.

### Possible Causes

**Wrong encryption key.** If you changed the key after secrets were encrypted, existing secrets cannot be decrypted.

**Truncated key.** The key must be exactly 32 bytes (256 bits), base64-encoded. A shorter key will cause authentication failures.

**Key from wrong source.** `DatabaseConfigurationOptions.EncryptionKey` takes precedence over the `PRAGMATIC_SECRET_KEY` environment variable. If both are set with different values, you may be encrypting with one and decrypting with the other.

**Fix:** Verify the key is consistent across all instances of the application:

```csharp
// Generate a new key
var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
// key length should be 44 characters (32 bytes in base64)
```

---

## Hot-Reload Not Working

Values change in the store but `IOptionsMonitor<T>` does not fire change callbacks.

### Checklist

1. **Are you using `IOptionsMonitor<T>`?** `IOptions<T>` does not support hot-reload. See [Common Mistakes #8](/modules/configuration-module/common-mistakes/#8-using-ioptionst-instead-of-ioptionsmonitort-with-hot-reload).

2. **Is the Pragmatic store bridged?** You need `AddPragmaticStore` in the configuration builder:

   ```csharp
   builder.Configuration.AddPragmaticStore(store, environmentProfile);
   ```

3. **Is the store's `WatchAsync` working?** The bridge subscribes to `WatchAsync()`. For the database backend, verify `EnableChangePolling` is `true` and `PollingInterval` is reasonable.

4. **Check the Debug output.** `PragmaticConfigurationProvider` writes reload failures to `System.Diagnostics.Debug`. Run in debug mode and check the output window.

---

## Azure Backend: Authentication Fails

`AddAzureAppConfigurationStore` or `AddAzureKeyVaultSecretStore` throws authentication errors.

### Checklist

1. **Is `DefaultAzureCredential` configured?** The Azure backend uses `DefaultAzureCredential`, which tries multiple credential sources in order: environment variables, managed identity, Azure CLI, Visual Studio, etc.

2. **For local development:** Ensure you are logged in with `az login` and your account has access to the App Configuration / Key Vault resource.

3. **For production:** Ensure the managed identity has the following roles:
   - App Configuration: `App Configuration Data Reader` (or `Data Owner` for write)
   - Key Vault: `Key Vault Secrets User` (or `Secrets Officer` for write)

4. **Connection string alternative:** If managed identity is not available, use the connection string:

   ```csharp
   options.AppConfigurationConnectionString = "Endpoint=...;Id=...;Secret=...";
   ```

---

## Tenant Override Not Applied

You set a tenant-specific value but `ConfigurationResolver` returns the base value.

### Checklist

1. **Is multi-tenancy enabled?**

   ```csharp
   services.AddPragmaticConfiguration(options =>
   {
       options.MultiTenant.Enabled = true;
   });
   ```

2. **Is `ITenantContext` resolved?** The `ConfigurationResolver` reads `ITenantContext` from DI. Verify that [Pragmatic.MultiTenancy](/modules/multi-tenancy/overview/) is registered and the tenant is resolved for the current request.

3. **Is `FallbackToBase` causing confusion?** When `true` (default), the resolver falls back to the base value if no tenant override exists. This is correct behavior -- but if you expected the tenant override to be applied and it is not, verify the value was set with the correct tenant ID.

---

## FAQ

### Can I use [Configuration] without the runtime stores?

Yes. The source generator and runtime stores are independent. Use `[Configuration]` with standard `appsettings.json` and `IOptions<T>` -- no `AddPragmaticConfiguration()` needed. The SG generates binding code that works with Microsoft's built-in configuration system.

### Can I use runtime stores without [Configuration]?

Yes. Register `AddPragmaticConfiguration()` and use `IConfigurationStore` / `ISecretStore` directly. Or bridge to `IConfiguration` with `AddPragmaticStore()` and bind `IOptions<T>` manually.

### How do I migrate from appsettings.json to a database store?

1. Register the database store before `AddPragmaticConfiguration()`.
2. Bridge with `AddPragmaticStore()`.
3. Seed the database with values from `appsettings.json`.
4. Both sources work simultaneously -- Microsoft Configuration layers them, with later sources overriding earlier ones.

### What happens if the database is down?

The `InMemoryConfigurationCacheStack` caches values for the configured TTL. During the cache window, reads succeed even if the database is unreachable. After the cache expires, reads will fail. For critical configuration, ensure your cache TTLs are appropriate and consider fallback to `appsettings.json` as a secondary source.

### Does the SG support nested options classes?

The SG binds to the section as a flat object. For nested configuration, use `[Configuration(SectionPath = "Parent:Child")]` or structure your `appsettings.json` to match the class hierarchy. Standard Microsoft Configuration binding supports nested objects natively.

---

## Getting Help

| Resource | Location |
|----------|----------|
| Concepts and architecture | [concepts.md](/modules/configuration-module/concepts/) |
| Getting started | [getting-started.md](/modules/configuration-module/getting-started/) |
| Store backends | [stores.md](/modules/configuration-module/stores/) |
| Common mistakes | [common-mistakes.md](/modules/configuration-module/common-mistakes/) |
| Module README | [../README.md](/modules/configuration-module/overview/) |
