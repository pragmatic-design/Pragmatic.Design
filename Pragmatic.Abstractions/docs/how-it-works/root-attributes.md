# The root attributes: two marks that live above every folder

> **Scope**: the two `.cs` files sitting directly in `src/Pragmatic.Abstractions/`.
> `FastEnumAttribute` · `NotLoggedAttribute`, 85 lines together, both
> `sealed class … : Attribute;` with an empty body and no properties.
>
> **Not covered here**: `IRedactionMap`, the one runtime surface `[NotLogged]` produces, is
> documented with the rest of `Serialization/`; see [serialization](serialization.md). The
> generator features that read these attributes live in `Pragmatic.SourceGenerator` and are named,
> not opened.

For the member-by-member catalogue, see
[interfaces](../interfaces.md#25-top-level-attributes).

## Why they are at the root

Both are in namespace `Pragmatic`, not `Pragmatic.Abstractions`; the project sets
`<RootNamespace>Pragmatic</RootNamespace>`. Neither belongs to a feature folder because neither
belongs to a feature: they annotate types that could be anywhere in an application. An enum in a
billing module and a password field on a mutation have nothing in common except that the framework
wants to notice them, and a `using Pragmatic;` that most files already have is the cheapest way to
make the mark available.

They are also both **pure markers**: no constructor arguments, no properties, nothing to configure.
Everything either one does happens somewhere else, which is the part that repays reading.

---

## `[FastEnum]`: enum helpers without reflection

`[AttributeUsage(AttributeTargets.Enum)]`. Applied to an enum, the generator emits a static
`{Type}Extensions` class and an AOT-safe JSON converter.

**The enum's values must be distinct.** Every declared member becomes an arm of the same `switch`, so
an alias (two names on one numeric value, `Success = 0, Ok = 0`, which C# allows) emits duplicate
constant patterns and the generated file does not compile (`CS8510`, on `ToStringFast()` and
`IsDefined(value)`; the switches over the *name* are unaffected). No diagnostic precedes that, so
apply the attribute to enums whose members each carry their own value.

### The problem it addresses

The BCL `Enum` APIs (`ToString()`, `Enum.IsDefined`, `Enum.GetValues`) are reflection-based. Under
Native AOT and trimming that is a correctness problem before it is a speed one: the metadata those
calls need can be trimmed away, and `Enum.ToString()` boxes on the way. An enum that a hot path
formats on every request, or that Native AOT must serialize, needs the answers baked in at compile
time. That is all this attribute buys: the values are known when the code is compiled, so the
lookup can be too.

### What is actually emitted

Hint name `{Namespace}.{Type}.FastEnum.g.cs`, holding two top-level types.

**`{Type}Extensions`**, always:

| Member | Shape |
|---|---|
| `ToStringFast()` | `switch` over the declared values; falls back to `value.ToString()` for an undeclared value. |
| `IsDefined(value)` | `switch` on the enum value. |
| `IsDefined(string)` | `switch` on the name. |
| `TryParse(string?, out T)` | `switch` on the name. |
| `TryParse(string?, bool ignoreCase, out T)` | Case-insensitive overload; normalises with `ToUpperInvariant()` before matching. |
| `GetValues()` / `GetNames()` | Return cached `static readonly` arrays as `ReadOnlySpan<>`. |
| `Count` | `public static int Count => N;`, a static property, not a `const`, so not usable where C# requires a constant (a `case` label, an attribute argument, a parameter default). |

`GetValues`/`GetNames` returning a cached array rather than a collection expression in the body is
deliberate and commented in the template: an expression-bodied `=> new[] { … }` would heap-allocate
on **every** call despite the `ReadOnlySpan` return type, which is exactly the cost the feature
exists to avoid. `FastEnumExtensionsTemplateTests` pins the cached-array behaviour, so it cannot
silently regress.

**`{Type}JsonConverter : JsonConverter<{Type}>`**, also always: the second top-level class in the
same file. `Write` calls `ToStringFast()`; `Read` calls the case-insensitive `TryParse`.

**It is generated, not applied.** Nothing puts `[JsonConverter]` on the enum and nothing registers
it: the host's own JSON setup adds `JsonStringEnumConverter`, the framework one. To serialise
through the generated converter, say so: attribute the enum with
`[JsonConverter(typeof(MyEnumJsonConverter))]`, or add an instance to
`JsonSerializerOptions.Converters`.

Once it is in the path, `Write` is allocation-free for declared members. An undeclared value (a
cast from an integer that matches no member) falls back to `value.ToString()`, which is the only
reflective step left.

**Conditionally**, two more:

- `GetDisplayName()` when a member carries `[Display]` or `[Description]`.
- `GetI18nKey()` and `GetLocalizedName()` when the compilation references
  `Pragmatic.Internationalization`. This is detected by **assembly presence**, not opt-in: the
  transform looks up `ILocalizationProvider` by metadata name, so a project that references I18n gets
  the localized members on every `[FastEnum]` enum, and the generated file acquires
  `using Pragmatic.Internationalization.*`. Worth knowing before wondering where those members came
  from.

### One behaviour to be aware of

The generated members reason over the **declared** values only. For a `[Flags]` enum, `IsDefined`,
`TryParse` and `GetValues` therefore speak about the individual declared flags, not about
combinations of them. If you need `Read | Write` to be recognised as defined, that check is yours to
write.

### The pipeline

| Stage | File |
|---|---|
| Feature | `Features/FastEnum/FastEnumFeature.cs`: `ForAttributeWithMetadataName("Pragmatic.FastEnumAttribute")`, registered unconditionally |
| Transform | `Features/FastEnum/Transforms/FastEnumTransform.cs` |
| Model | `Features/FastEnum/Models/FastEnumModel.cs` |
| Template | `Features/FastEnum/Templates/FastEnumTemplate.cs` |

`FastEnum` is one of the features registered without a `DetectedFeatures` flag: there is no
additional package whose presence gates it.

The hint name is built with the **namespace included** rather than from the type name alone, and two
tests cover the case (`FastEnumExtensionsTemplateTests`, `FastEnumHintNameCollisionTests`). Two enums
with the same simple name in different namespaces would otherwise collide on one hint, a collision
Roslyn answers by discarding the generator's entire output with a warning, not an error.

In this repository the attribute is applied in the Showcase: `Showcase.Billing`'s `InvoiceStatus` and
`PaymentMethod`, and `Showcase.Booking`'s `ReservationStatus`.

---

## `[NotLogged]`: a mark, and an honest account of what it does

`[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]`. Applied to a property
that holds a secret: a password, a token, a card number.

**This attribute marks; it does not currently enforce.** That sentence is on the attribute itself and
it is the most important thing on this page, because the failure mode of assuming otherwise is
believing a secret is protected when it is not.

Concretely, `[NotLogged]`:

- does **not** rewrite your `_logger.Log*` calls;
- does **not** affect generated `ToString()`;
- does **not** flag OpenAPI schemas.

Keeping marked members out of hand-written log statements remains your responsibility.

### What the generator does with it

One place, and one only: message types. `MessageHandlerTransform` collects the types reachable from
a message that declare marked members (recording them under their **JSON** name, so
`[JsonPropertyName]` is respected) and `MessageRedactionMapTemplate` emits a
`GeneratedMessageRedactionMap : IRedactionMap` into `_Infra.Messaging.RedactionMap.g.cs`, registered
by `HandlerRegistrationTemplate`. The map is emitted only when at least one message type declares a
marked member. The walk is depth-limited: it stops at eight levels of nesting, so a marked member
below that is not collected.

No component consults that map today. Message auditing moved onto the framework audit trail, whose
entries carry no payload field, so at that point there is no serialized payload left to redact.
Nothing else in the framework reads the attribute.

### What actually redacts

Two mechanisms, both independent of this attribute:

| Mechanism | Where | How it decides |
|---|---|---|
| `PragmaticDataRedactor` | `Pragmatic.Logging` | Matches configured **property-name patterns**. |
| `PersonalDataRedactor` | Audit trail | Applied to what the trail stores. |

Pattern-based, in other words: a property called `Password` is redacted because of its name, not
because of an attribute.

### When you have personal data rather than a secret

For personal data with a declared category (and the erasure, retention and Article 30 machinery
behind it), use **`[PersonalData]`** from `Pragmatic.Privacy.Abstractions`, which is wired end to
end. `[NotLogged]` is the weaker, unenforced mark; if what you are protecting is regulated personal
data, `[PersonalData]` is the attribute you want.

## External references

Named here, described where they live:

- **`Pragmatic.SourceGenerator` → `Features/FastEnum/`**: the four-stage pipeline that turns
  `[FastEnum]` into the extensions class and the JSON converter.
- **`Pragmatic.SourceGenerator` → `Features/Messaging/`**: `MessageHandlerTransform`,
  `MessageRedactionMapTemplate` and `HandlerRegistrationTemplate`, the only reader of `[NotLogged]`.
- **`Pragmatic.Internationalization` → `ILocalizationProvider`**: its presence in the compilation is
  what adds `GetI18nKey()`/`GetLocalizedName()` to a `[FastEnum]` enum.
- **`Pragmatic.Logging` → `PragmaticDataRedactor`**: pattern-based redaction of log properties.
- **`Pragmatic.Privacy.Abstractions` → `[PersonalData]`**: the enforced attribute for personal data,
  with categories, erasure and retention behind it.
