# Common Mistakes

These are the most common issues developers encounter when using Pragmatic.Configuration. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Forgetting `partial` on the Configuration Class

**Wrong:**

```csharp
[Configuration]
public class BookingOptions
{
    [Required]
    public string HotelName { get; set; } = "";
    public int MaxGuests { get; set; } = 10;
}
```

**Compile result:** `PRAG2000` error -- "[Configuration] class 'BookingOptions' must be declared as partial".

**Right:**

```csharp
[Configuration]
public partial class BookingOptions
{
    [Required]
    public string HotelName { get; set; } = "";
    public int MaxGuests { get; set; } = 10;
}
```

**Why:** The source generator emits extension methods and metadata into a partial class. Without `partial`, the compiler cannot merge the generated code with your class.

---

## 2. Registering Options Manually Instead of Using the SG

**Wrong:**

```csharp
// Hand-written boilerplate for every options class
services.AddOptions<BookingOptions>()
    .Bind(configuration.GetSection("Booking"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

services.AddOptions<PaymentOptions>()
    .Bind(configuration.GetSection("Payment"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

**Right:**

```csharp
// One line registers ALL [Configuration] options in the assembly
services.AddMyAppConfiguration(configuration);
```

**Why:** The source generator produces a per-assembly aggregator method that calls every individual `Add*Options` method. This eliminates boilerplate and ensures you never forget to register a new options class. When you add a new `[Configuration]` class, it is automatically included in the aggregator.

---

## 3. Forgetting to Call the Assembly Aggregator

**Wrong:**

```csharp
[StartupStep]
public class AppStartupStep : IStartupStep
{
    public void ConfigureServices(IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        // Added BookingOptions manually, forgot PaymentOptions and NotificationOptions
        services.AddBookingOptions(config);
    }
}
```

**Right:**

```csharp
[StartupStep]
public class AppStartupStep : IStartupStep
{
    public void ConfigureServices(IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        // Registers ALL [Configuration] classes in this assembly
        services.AddMyAppConfiguration(config);
    }
}
```

**Why:** The per-assembly aggregator (`Add{Prefix}Configuration`) is the intended entry point. Using individual `Add*Options` methods defeats the purpose of the SG -- you are back to manually tracking each options class.

---

## 4. Wrong Section Path in appsettings.json

**Wrong:**

```csharp
[Configuration]  // Inferred section: "Booking"
public partial class BookingOptions
{
    public string HotelName { get; set; } = "";
}
```

```json
{
  "BookingOptions": {
    "HotelName": "Grand Hotel"
  }
}
```

**Runtime result:** `HotelName` is empty string (the default) because the section `"Booking"` does not exist in the JSON.

**Right:**

```json
{
  "Booking": {
    "HotelName": "Grand Hotel"
  }
}
```

**Why:** The section path is inferred by removing the `Options` suffix. `BookingOptions` binds to `"Booking"`, not `"BookingOptions"`. If you need a specific path, use `[Configuration(SectionPath = "BookingOptions")]` explicitly.

---

## 5. Storing Secrets in IConfigurationStore Instead of ISecretStore

**Wrong:**

```csharp
await configStore.SetAsync("PaymentGateway:ApiKey", "sk_live_abc123");
// Secret stored in plaintext in pragmatic_config table
```

**Right:**

```csharp
// Use ISecretStore -- encrypted at rest in pragmatic_secrets table
var apiKey = await secretStore.GetSecretAsync("PaymentGateway:ApiKey");
```

**Why:** `IConfigurationStore` stores values in plaintext. `ISecretStore` uses AES-256-GCM encryption at rest (database backend) or Azure Key Vault (Azure backend). Secrets stored in `IConfigurationStore` are visible to anyone with database access.

For the database backend, secrets in `pragmatic_secrets` are encrypted with a key that never touches the database. Configuration in `pragmatic_config` is plaintext (and audited).

---

## 6. Registering Backend Store After AddPragmaticConfiguration

**Wrong:**

```csharp
// In-memory store registered first via TryAdd
services.AddPragmaticConfiguration();

// Database store registration is ignored -- TryAdd already registered in-memory
services.AddDatabaseConfigurationStore(options => { ... });
```

**Right:**

```csharp
// Database store registered first -- takes precedence
services.AddDatabaseConfigurationStore(options =>
{
    options.Provider = DatabaseProvider.PostgreSql;
    options.AutoCreateSchema = true;
});

// In-memory store skipped via TryAdd -- database store already registered
services.AddPragmaticConfiguration();
```

**Why:** `AddPragmaticConfiguration()` uses `TryAddSingleton` for default stores. If a store is already registered, `TryAdd` skips it. If you call `AddPragmaticConfiguration()` first, the in-memory store is registered and the database store registration is silently ignored. Register your backend store **before** calling `AddPragmaticConfiguration()`.

---

## 7. Missing Encryption Key for Database Secret Store

**Wrong:**

```csharp
services.AddDatabaseConfigurationStore(options =>
{
    options.Provider = DatabaseProvider.PostgreSql;
    // No EncryptionKey set
});

services.AddDatabaseSecretStore();
// Throws InvalidOperationException at runtime:
// "Secret encryption key not configured"
```

**Right:**

```csharp
services.AddDatabaseConfigurationStore(options =>
{
    options.Provider = DatabaseProvider.PostgreSql;
    options.EncryptionKey = "base64-encoded-32-byte-key";
    // Or set PRAGMATIC_SECRET_KEY environment variable
});

services.AddDatabaseSecretStore();
```

**Why:** The database secret store uses AES-256-GCM encryption, which requires a 32-byte key. The key is resolved from `DatabaseConfigurationOptions.EncryptionKey` first, then falls back to the `PRAGMATIC_SECRET_KEY` environment variable. If neither is set, the application throws at startup when the secret store is first resolved from DI.

Generate a key:
```csharp
var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
```

---

## 8. Using IOptions<T> Instead of IOptionsMonitor<T> with Hot-Reload

**Wrong:**

```csharp
public class BookingService(IOptions<BookingOptions> options)
{
    // This value is captured once at startup -- never updated
    public int MaxGuests => options.Value.MaxGuests;
}
```

**Right:**

```csharp
public class BookingService(IOptionsMonitor<BookingOptions> options)
{
    // This value reflects the latest configuration from any backend
    public int MaxGuests => options.CurrentValue.MaxGuests;
}
```

**Why:** `IOptions<T>` reads once at startup and never changes. `IOptionsMonitor<T>` subscribes to change notifications via `IOptionsChangeTokenSource<T>`. When `PragmaticConfigurationProvider` detects a change via `WatchAsync()`, it triggers `OnReload()`, which updates `IOptionsMonitor<T>` but not `IOptions<T>`.

| Pattern | Updates on hot-reload? |
|---------|----------------------|
| `IOptions<T>` | No |
| `IOptionsSnapshot<T>` | Yes, per-request |
| `IOptionsMonitor<T>` | Yes, immediately |

---

## 9. Using `[Required]` with a Default Value

**Wrong:**

```csharp
[Configuration]
public partial class BookingOptions
{
    [Required]
    public string HotelName { get; set; } = "Default Hotel";
    // Warning PRAG2050: [Required] with default value
}
```

**Right -- if the property truly needs a default:**

```csharp
[Configuration]
public partial class BookingOptions
{
    // No [Required] -- the default is intentional
    public string HotelName { get; set; } = "Default Hotel";
}
```

**Right -- if the property must be configured:**

```csharp
[Configuration]
public partial class BookingOptions
{
    [Required]
    public string HotelName { get; set; } = "";
    // Empty string is the "unset" sentinel -- [Required] catches it
}
```

**Why:** `[Required]` means "this must be explicitly configured." A non-empty default value means "use this if not configured." These intentions conflict. The SG emits warning `PRAG2050` to flag this contradiction. Decide which intention is correct and remove the other.

---

## 10. Hardcoding Tenant Overrides Instead of Using the Store

**Wrong:**

```csharp
public int GetMaxGuests(string tenantId)
{
    return tenantId switch
    {
        "acme" => 200,
        "contoso" => 50,
        _ => _options.CurrentValue.MaxGuests
    };
}
```

**Right:**

```csharp
// Set tenant override in the store (once, via admin UI or migration)
await store.SetAsync("Booking:MaxGuests", "200", tenantId: "acme");

// Read via ConfigurationResolver -- automatically resolves tenant override
var maxGuests = await resolver.ResolveAsync("Booking:MaxGuests");
```

**Why:** Hardcoded tenant overrides require code changes and deployments. Store-based overrides are dynamic, auditable (the database backend records every change on the shared audit trail), and managed through admin interfaces. The `ConfigurationResolver` handles cascade resolution automatically -- tenant -> environment -> base.
