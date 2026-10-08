---
title: "Log call sites"
description: "A `[LoggerMessage]` method in a project that references `Pragmatic.SourceGenerator` gets its body from"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Logging/docs/call-sites.md
sidebar:
  order: 3
---
A `[LoggerMessage]` method in a project that references `Pragmatic.SourceGenerator` gets its body from
the Pragmatic generator, not from Microsoft's. The declaration does not change:

```csharp
using Microsoft.Extensions.Logging;
using Pragmatic.Privacy;

public static partial class OrderLog
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Receipt for {OrderId} sent to {Email}")]
    public static partial void ReceiptSent(ILogger logger, int orderId, [PersonalData(DataCategory.Contact)] string email);
}
```

## What it adds

- **A UTF-8 state.** Each call site gets a `readonly struct` that renders its message as UTF-8 and writes
  its properties into a `Utf8JsonWriter` without boxing a value. It still reaches the provider through
  `ILogger.Log<TState>`, and still reads as a list of key/value pairs, so ASP.NET Core, OpenTelemetry
  and any other provider keep working.
- **Masking at the call site.** A parameter marked `[PersonalData]` or `[NotLogged]` is written as
  `[redacted]` by the generated code, in the message, the structured property and every other view of
  the state. The value is not even handed to the state.
- **Usable from generated code.** The Pragmatic generator writes call sites into the types it generates
  (the message handler pipeline, the saga orchestrator, the attachment purge job), in the same pass.
  Microsoft's generator never sees another generator's output, so a `[LoggerMessage]` declared there
  would get no body and every call to it would be compiled away.

## How a project gets it

The generator's package declares a global using alias through MSBuild:

```xml
<Using Include="Pragmatic.Logging.CallSites.LoggerMessageAttribute" Alias="LoggerMessageAttribute" />
```

so `[LoggerMessage]` binds to `Pragmatic.Logging.CallSites.LoggerMessageAttribute`, which has the same
constructors and properties as Microsoft's. `<PragmaticLogCallSites>false</PragmaticLogCallSites>`
turns it off for a project.

Inside this repository a `ProjectReference` imports none of the package's props, so a project opts in
with `<PragmaticLogCallSites>true</PragmaticLogCallSites>` and `Directory.Build.targets` adds the same
alias. The reference applications under `examples/` set it, so they log the way an application that
installs the package does.

To hand one method to Microsoft's generator, write the attribute fully qualified:
`[Microsoft.Extensions.Logging.LoggerMessage(...)]`.

If the alias goes missing in a project that uses call sites (a `<Using Remove>`, a project importing the
props some other way), a `[LoggerMessage]` written by its simple name binds to Microsoft's attribute and
the masking silently stops. **PRAG2408** makes that a build error instead.

## The rules

The same as Microsoft's, so a call site moves between the two unchanged:

- a static method takes the logger as a parameter; an instance method finds it in a parameter, then in
  one `ILogger` field or property of the type or a base, then in one primary-constructor parameter;
- the level is the attribute's `Level`, or a `LogLevel` parameter;
- the first `Exception` parameter is the entry's exception;
- a placeholder matches a parameter by name, ignoring case and a leading `@`; a format (`{Amount:F2}`)
  is honoured, an alignment (`{Name,8}`) is reported;
- an event id left unset is derived from the event name, the same on every build.

What does not fit is reported where the method is written: PRAG2400–PRAG2407.

## How a provider writes it

The Pragmatic JSON provider writes a call site's line straight from the state into a reused buffer: no
`LogEntry`, no message string, no intermediate stream. The line is field for field the one the classic
path writes for the same call. A call whose arguments are numbers, strings, booleans, enums, dates,
GUIDs and the like, masked or not, **allocates nothing** at steady state.

That path is taken when nothing in the pipeline needs the entry materialized. Otherwise the provider
uses the classic path, with the same output:

| Condition | Why |
|---|---|
| Context enrichment on | The context properties are read into the entry |
| Advanced filters configured | Filters evaluate the entry |
| Pattern redaction (`EnableDataRedaction`) on | It works on rendered strings |
| An ambient scope is active | Scopes are written beside the properties by the classic path |
| An argument the state cannot write itself | See below |

## Arguments of an application type

An argument of an application type whose members declare `[PersonalData]` or `[NotLogged]` (on the type
itself or on something it owns) is written by a **generated UTF-8 writer**: the members the type's JSON
shape describes, with the mask at the declared paths, into the same `Utf8JsonWriter` the rest of the
state writes into. The call site stays self-contained and allocates nothing for it through the JSON
provider. Every view carries the masked rendering, the list view included, so a provider that knows
nothing of Pragmatic gets `{"reference":"C-42","email":"[redacted]",…}` rather than the object and its
`ToString()`. The line is the one the declared redactor writes for the same value.

The writer exists only where the generator can describe the type: the same rule as the generated JSON
context. A type with a dictionary member, a polymorphic or abstract type, or a member without a public
setter is left to the provider: the state says so (`IsSelfContained` is false), and the provider reads
the list view, where the declared redactor masks it.

An application type that declares nothing is also left to the provider. Only the runtime's own
formattable types write themselves, because an application type that formats itself could put a member
it declared personal into the line.

⚠️ A provider that knows nothing of Pragmatic calls the formatter and reads the list. A masked
**parameter** is masked there too, and so is a type with a generated writer. The members of an
application type **without** one are masked only by the Pragmatic providers, which apply the declared
redactor; a third-party provider writes that argument as its `ToString()` would.

## Diagnostics

| ID | Severity | What |
|---|---|---|
| PRAG2400 | Error | Not a non-generic `partial void` with by-value parameters |
| PRAG2401 | Error | A placeholder names no parameter |
| PRAG2402 | Warning | A parameter is not in the message |
| PRAG2403 | Error | No logger, or more than one |
| PRAG2404 | Error | No level |
| PRAG2405 | Warning | Two call sites of a type share an event id |
| PRAG2406 | Error | Malformed template, or an alignment |
| PRAG2407 | Error | A containing type is not `partial` |
| PRAG2408 | Error | `[LoggerMessage]` binds to Microsoft's where Pragmatic's was expected |
| PRAG2410 | Error | `[NotLogged]`/`[PersonalData]` on a parameter nothing reads |
