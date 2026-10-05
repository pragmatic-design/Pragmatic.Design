---
title: "Pragmatic.Redaction"
description: "What must not reach a log or an audit trail, removed in two complementary ways: by **shape** (the"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Redaction/README.md
sidebar:
  order: 0
  label: Overview
---
What must not reach a log or an audit trail, removed in two complementary ways: by **shape** (the
personal-data patterns worth recognising in free text, in one place) and by **declaration** (the members
a type marked `[NotLogged]` or `[PersonalData]`).

## Why it exists

Two subsystems that each carry their own pattern set drift apart: one lets a national identifier
(`123-45-6789`) or a card-verification value (`cvv: 123`) through in plaintext while the other catches
both. One set, shared by the logging pipeline and the audit trail, keeps them equal.

It matters most for the audit trail. A log rotates; **the audit trail is append-only and kept for
years**, so its redaction floor must never be the lower of the two.

## Use

```csharp
var safe = PersonalDataRedactor.Redact(text);
```

Covers e-mail, IBAN, long digit runs (payment cards, phone and account numbers), national identifiers,
card-verification values, bearer tokens, and secrets assigned by name (`password=`, `api_key:`).

## A floor, not a guarantee

Pattern matching cannot recognise a name, an address, or a sentence about someone's health. A caller
that puts a personal value into free text is relying on a net with holes. The point is that the obvious
shapes do not get through, not that nothing does.

## Dates are deliberately absent

A date is personal data only in context: a birth date is, `locked until 2026-08-01` is not, and no
pattern tells them apart. Redacting every date would empty the one field that explains why an entry
exists, which is how a redactor stops being used at all.

## Redaction by declaration

A pattern cannot see what has no shape: an internal identifier, a pricing coefficient, a token that
looks like any other string. For those the type says it: `[NotLogged]` (in `Pragmatic.Abstractions`)
or `[PersonalData]`, and the source generator emits an `IRedactionMap` per type, with the path to every
marked member, owned records and collections included. `DeclaredRedactor` walks those paths; nothing
reflects over the payload.

The value is serialized before it is masked, and it is written in camelCase, like every other complex
value the JSON log providers write. Under Native AOT the serialization needs metadata: when the assembly
emits a generated JSON context (`PublishAot` or `PragmaticGenerateJsonContext`), every type in its map is
in that context, and the map hands it to the redactor. A type the generator cannot describe (an `object`
member, a property without a public setter) has no metadata. Its value is then written as the mask,
whole, and counted in `DeclaredRedactor.ValuesWithoutMetadata`; the entry is never lost and nothing goes
out in clear.

In a Pragmatic host this is wired for you: when any map exists the generated startup calls
`AddDeclaredRedaction()`, which wraps the `ILoggerFactory` in a `RedactingLoggerFactory`, so every
provider, including one a test adds later, receives the masked value.

The two are governed differently on purpose. Pattern redaction is a heuristic, with false positives, and
sits behind the logging pipeline's `EnableRedaction` setting. **Declared redaction is not optional and
has no environment qualifier**: "never in the logs" does not mean "except on my machine".

## Where the patterns are used

`Pragmatic.Audit` uses `PersonalDataRedactor`. `Pragmatic.Logging` takes its e-mail and IBAN patterns
from here and **still carries the rest of its set on its own**: national identifiers, cards, and
credentials (AWS keys, GitHub tokens, Stripe keys, private keys), which are a different problem from
personal data and are not in scope here.

## Status

**Preview** within 1.0.0-alpha: the personal-data shapes recognised in free text, used by the logging
and audit modules and by the composed host. See the [roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

## Requirements

- .NET 10.0+

## License

Part of the [Pragmatic.Design](/modules/redaction/overview/) ecosystem. See [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.Redaction is licensed under the **PolyForm Small Business 1.0.0** license (free for small
businesses; commercial license above the threshold).
