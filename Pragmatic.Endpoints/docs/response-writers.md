# Generated Response Writers

A generated endpoint writes its JSON response with a UTF-8 writer the generator emits for the response type,
straight into the response body: no serializer, no metadata lookup. The bytes are the ones `System.Text.Json`
would write for the same value under the host's options, and the endpoint falls back to the serializer
wherever the generator cannot promise that.

Nothing is declared. A response type gets a writer when the generator can describe every byte of it; the
handler answers through it when the host's options are the ones the generated entry point wrote.

## When the writer is used

Two conditions, one decided at compile time and one at run time.

| Condition | Decided | If it does not hold |
|-----------|---------|---------------------|
| The generator can reproduce the type (see below) | At compile time, per response type | The handler keeps `Results.Ok`/`Created`/`Json`, and `PRAG0555` (Info) says which member decided it |
| The host's `JsonOptions` are still the ones the generated entry point wrote | When the response is written | The response goes to the serializer, with the same status, `Location` and content type |
| Nothing else the host registered claims a type the writer writes | Once per writer, while the options stay the same | The response goes to the serializer |

These are run-time checks because the options are run-time configuration: an `IStartupStep`, or the
application's own `ConfigureHttpJsonOptions`, can change the naming policy, add a converter or replace the
resolver after the entry point ran. The entry point records what it wrote (`GeneratedJsonDefaults.Mark`),
and a response compares the live options with that record. Any setting that changes the bytes sends it
back to the serializer: that is the rule, not a degradation.

What the host registers besides is asked per writer, against the types it writes (its
`GeneratedJsonShape`, emitted beside it):

| Registered | A writer stays in use when |
|------------|----------------------------|
| A converter (Internationalization's for `Money`, Temporal's for its dates, an application's own) | it claims none of the writer's types; an enum is claimed by `JsonStringEnumConverter` or its own `[FastEnum]` converter |
| A modifier on the `PragmaticJsonOptions` seam | it was registered saying which types it touches (`AddModifier(modifier, touches)`), and touches none of the writer's; a modifier that does not say touches every type |
| A resolver put in the chain after the entry point (ASP.NET's OpenAPI schema context) | it answers for none of the writer's types |

Temporal registers its timezone modifier with the types that have a behavior registered
(`TemporalJsonModifier.Touches`), so a host with Temporal keeps the writers of every other type.

A value whose runtime type derives from the declared one also goes to the serializer, which writes an
`object` as its runtime type.

## What a writer reproduces

The host's response options: camelCase names (`JsonNamingPolicy.CamelCase`, so `URLPath` is `urlPath`), nulls
left out, enums by name with an undeclared value as its number, cycles ignored, ASP.NET's encoder (which leaves
non-ASCII text and `<`, `>`, `&` unescaped), and the infrastructure members (`TenantId`, `OwnerId`, change
tracking on an entity) left out where the host strips them.

From the type: public properties with a public getter, most derived first; `[JsonPropertyName]`,
`[JsonIgnore]` with its condition, `[JsonPropertyOrder]`; strings, booleans, every integer and floating-point
type, `decimal`, `char`, `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`, `Uri`,
`byte[]` (base64), enums, nullable value types, nested objects, any `IEnumerable<T>`, and dictionaries keyed by
a string or an integer.

## What keeps the serializer

Each of these is reported by `PRAG0555` on the endpoint, naming the type and the member:

- a `[JsonConverter]` on a member or a type, `[JsonExtensionData]`, `[JsonNumberHandling]`, a polymorphic type;
- a member typed `object`, `JsonElement` or anything else whose shape is decided at run time;
- a type that reaches itself (`IgnoreCycles` decides per instance what a writer would decide per type);
- a `[Flags]` enum, an enum that gives two names to one value or renames a member on the wire;
- a dictionary keyed by anything but a string or an integer;
- a type the generated JSON context could also describe and would describe differently from reflection, since
  which of the two the host uses depends on what it registered;
- a type the module's code cannot name.

A type the generator itself writes in the same compilation (the record a key-returning mutation answers with)
keeps the serializer without a diagnostic: there is nothing for the author to change.

## Seeing which writer answered

The `Pragmatic.Endpoints` meter counts `pragmatic.endpoints.json_responses`, tagged `writer=generated` or
`writer=serializer`, for every response of an endpoint that has a generated writer. A host whose options
stopped being the generated default keeps answering correctly and shows it here: the `serializer` share grows.

## Limits

- **The document is written whole, then flushed once.** The serializer flushes the body as it goes past a
  threshold; for a very large response the writer holds more of it in memory at once.
- **The ASP.NET encoder is assumed.** A host that sets another encoder answers through the serializer.
- **A type with a converter keeps the serializer at run time, not at compile time.** `Money` is a struct the
  generator can describe member by member, so a writer is generated; the host's converter for it is
  registered configuration, so the response asks and falls back. No diagnostic says so; the
  `serializer` share of the counter does.
- **Remote boundary endpoints and autocomplete endpoints** keep the serializer.

## Verified by

| Test | Proves |
|------|--------|
| `AResponseWriterWritesWhatTheHostSerializerWritesTests` | A writer's bytes equal `JsonSerializer`'s under ASP.NET's `JsonOptions` plus the entry point's settings, over every supported kind of member |
| `WhatAResponseWriterCoversTests` | What is refused and why, the infrastructure members, a list root, the camelCase against `JsonNamingPolicy.CamelCase` |
| `AGeneratedJsonResponseTests` | The writer is used exactly while the options are the marked ones and nothing else claims its types (converters, declared and undeclared modifiers, resolvers ahead of the entry point's); status, `Location` and content type either way; the counter |
| `AnsweringThroughTheGeneratedWriter` (Invoicing, container) | A real host answers through the writer, with the serializer's bytes; it refuses a writer of `Money`; the control host whose options changed answers through the serializer |
| `EveryResponseWriterWritesTheSerializersBytes` (Invoicing, TimeOff, Casework, Warehouse, Showcase, container) | Every writer the running host admits writes the serializer's bytes under that host's options, with every member filled and with every member at its default |
