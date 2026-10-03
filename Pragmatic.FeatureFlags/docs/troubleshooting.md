# Troubleshooting

Practical problem/solution guide for Pragmatic.FeatureFlags. Each section covers a common issue, the likely causes, and the fix.

---

## Flag Always Returns False

A flag is defined but `IsEnabledAsync` always returns `false`.

### Checklist

1. **Is the flag defined?** `GetDefinitionAsync` returns `null` for unknown flags, and `IsEnabledAsync` returns `false` for unknown flags. Verify the flag exists in the store:

   ```csharp
   var definition = await store.GetDefinitionAsync("new-checkout");
   if (definition is null)
       Console.WriteLine("Flag not defined in the store");
   ```

2. **Is the flag name spelled correctly?** Lookup is case-insensitive, but typos are not caught. `"new-checkout"` and `"new_checkout"` are different flags. Use `IFeatureFlag` types for compile-time safety.

3. **Is the global `Enabled` state `false`?** If no rules match, the global `Enabled` is the fallback. A flag with `Enabled = false` and no matching rules always returns `false`.

4. **Are rules matching the context?** Rules require the corresponding context property to be non-null:
   - `tenant` rule requires `context.TenantId`
   - `user` rule requires `context.UserId`
   - `plan` rule requires `context.Plan`
   - `environment` rule requires `context.Environment`
   - `property` rule requires the key in `context.Properties`

   If the context property is null, the rule is skipped (does not match).

5. **Is a deny rule matching before an allow rule?** Rules are first-match-wins. Check that a deny rule (with `Enabled = false`) is not matching before your allow rule.

6. **For percentage rules**: Is the percentage greater than 0? A value of `"0"` always returns false. Also verify the user/tenant ID produces a bucket within the target range -- use a different test user if needed.

---

## Flag Always Returns True

A flag is enabled for everyone, regardless of targeting rules.

### Checklist

1. **Is the global `Enabled` set to `true` with no deny rules?** If `Enabled = true` and no rules match, the fallback is `true`. Add specific deny rules before allow rules if needed.

2. **Is a broad allow rule matching before specific deny rules?** Remember: first match wins. Reorder rules so deny rules come first:

   ```csharp
   Rules =
   [
       new FeatureFlagRule { Type = "tenant", Values = ["blocked-tenant"], Enabled = false },  // first
       new FeatureFlagRule { Type = "percentage", Values = ["50"], Enabled = true },            // second
   ]
   ```

3. **Is the percentage set to 100?** A percentage rule with `Values = ["100"]` always matches.

---

## Percentage Rollout Not Working as Expected

The percentage rollout does not seem to match the expected distribution.

### Checklist

1. **Small sample size.** Deterministic hashing guarantees the same result per user, but with a small number of users, the distribution may not be uniform. Test with at least 100 distinct user IDs.

2. **Same user on different flags.** The hash seed includes the flag name. User "user-42" may be in the 10% bucket for `flag-a` but not for `flag-b`. This is by design -- different flags have independent distributions.

3. **No UserId or TenantId in context.** If both are null, `"anonymous"` is used as the seed. All requests without identity get the same bucket -- the percentage becomes all-or-nothing, not per-user.

4. **Percentage value parsing.** The value must be a valid integer string: `["20"]`. Non-numeric values cause the rule to be skipped.

---

## WatchAsync Not Emitting Changes

`WatchAsync` does not emit `FeatureFlagChange` events.

### Checklist

1. **Which store are you using?** `InMemoryFeatureFlagStore` emits on any difference in the definition
   (`Enabled`, `Description`, rule count, or a rule's type/`Enabled`/values). `ConfigurationFeatureFlagStore`
   emits **only** on `Enabled` transitions: a rules-only edit in `appsettings.json` produces no event.

2. **Is the flag being defined for the first time?** The initial `Define()` does not emit a change (there is no "previous" state to compare against).

3. **Is the definition actually different?** Re-defining an identical definition emits nothing. Note that
   the comparison is element-wise on rule values, so `["10"] → ["20"]` does count as a change.

4. **Did the watcher start late, or restart?** Changes published while nobody is watching are held in a
   bounded buffer (64) and delivered to the first watcher that arrives. A watcher that stops and starts
   again is a *new* watcher: whatever it already consumed is gone, and anything published in the meantime
   went to the other active watchers, not to a shared backlog.

5. **Custom store implementation.** Custom `IFeatureFlagStore` implementations control their own notification semantics. The store may use polling, database notifications, or webhooks. Check the store documentation.

---

## DI Registration Conflicts

The wrong store implementation is resolved, or flags are not available.

### Checklist

1. **`AddPragmaticFeatureFlags()` uses `TryAdd`.** If you register a custom store **after** calling `AddPragmaticFeatureFlags()`, your store is ignored (the in-memory store was registered first). Register your store **before** or use the generic overload:

   ```csharp
   // Option 1: register before
   services.AddSingleton<IFeatureFlagStore, MyDatabaseStore>();
   services.AddPragmaticFeatureFlags();  // TryAdd won't replace

   // Option 2: generic overload (always replaces)
   services.AddPragmaticFeatureFlags<MyDatabaseStore>();
   ```

2. **SG auto-registration.** The SG registers `AddPragmaticFeatureFlags()` when it detects the package. If you also call it manually, the `TryAdd` in the default overload ensures no conflict. If you use the generic overload, it replaces the SG-registered store.

3. **Store lifetime.** The default registration is singleton. If your custom store needs scoped services (e.g., a scoped `DbContext`), register it as scoped -- but be aware that singleton consumers cannot inject a scoped store. Use `IServiceScopeFactory` in that case.

---

## Property Rules Not Matching

A property rule is defined but never matches.

### Checklist

1. **Check the Values format.** For property rules, `Values[0]` is the property key and `Values[1..]` are the allowed values. At least 2 values are required (key + at least one allowed value):

   ```csharp
   // Correct: key="region", allowed=["eu-west", "eu-east"]
   new FeatureFlagRule { Type = "property", Values = ["region", "eu-west", "eu-east"] }

   // Wrong: only 1 value -- interpreted as key with no allowed values, rule skipped
   new FeatureFlagRule { Type = "property", Values = ["region"] }
   ```

2. **Is the property in the context?** The property key must exist in `context.Properties`:

   ```csharp
   var context = new FeatureFlagContext
   {
       Properties = new Dictionary<string, string>
       {
           ["region"] = "eu-west"  // must match Values[0]
       }
   };
   ```

3. **Case sensitivity of property keys.** The property key lookup uses `TryGetValue` on the dictionary, which respects the dictionary's comparer. The default `Dictionary<string, string>` is case-sensitive. If your key is `"Region"` in the dictionary but `"region"` in the rule values, it will not match.

   The property **value** comparison is case-insensitive (handled by the evaluator), but the property **key** lookup depends on the dictionary comparer.

---

## Frequently Asked Questions

### Can I change a flag at runtime without redeploying?

Yes. That is the primary purpose of feature flags. With `InMemoryFeatureFlagStore`, call `Define()` to update a flag. With a database store, update the database record. The store implementation determines how changes propagate.

### Can I use feature flags without Pragmatic.MultiTenancy?

Yes. Feature flags have no dependency on multi-tenancy. Set `context.TenantId` to null (or use `FeatureFlagContext.Empty`) if you do not use multi-tenancy. Tenant-targeting rules will simply be skipped.

### How do I test feature flags in unit tests?

Use `InMemoryFeatureFlagStore` directly:

```csharp
var store = new InMemoryFeatureFlagStore();
store.Define(new FeatureFlagDefinition { Name = "my-flag", Enabled = true });

var result = await store.IsEnabledAsync("my-flag");
result.Should().BeTrue();
```

### How do I list all defined flags?

```csharp
var allFlags = await store.GetAllAsync(ct);
// Returns IReadOnlyList<FeatureFlagDefinition> ordered by name
```

### Is `FeatureFlagEvaluator` usable from custom stores?

No. `FeatureFlagEvaluator` is `internal` to the `Pragmatic.FeatureFlags` package. Custom stores must implement their own evaluation logic or load definitions and process rules manually. The evaluation algorithm is documented in [Concepts](concepts.md) for reference.

### What happens with concurrent flag updates?

`InMemoryFeatureFlagStore` uses `ConcurrentDictionary`, so concurrent `Define()` calls are thread-safe. The last write wins for the flag definition. Change notifications are emitted per `Define()` call.

---

## Getting Help

1. **Verify flag definitions.** Use `GetDefinitionAsync` and `GetAllAsync` to inspect the current state of flags in the store.
2. **Inspect the context.** Log the `FeatureFlagContext` before evaluation to verify all targeting dimensions are populated.
3. **Test with the evaluator.** Write unit tests against `InMemoryFeatureFlagStore` to verify rule behavior in isolation.
4. **Read the guides.** See [Concepts](concepts.md), [Getting Started](getting-started.md), [Stores](stores.md), and [Common Mistakes](common-mistakes.md).
