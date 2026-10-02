# Support

## Where to go

| You have | Go to |
|---|---|
| A question — how to do something, why something behaves as it does | [Discussions → Q&A](https://github.com/pragmatic-design/Pragmatic.Design/discussions/categories/q-a) |
| An idea, or a feature you would like | [Discussions → Ideas](https://github.com/pragmatic-design/Pragmatic.Design/discussions/categories/ideas) |
| Something that does not work as documented | [An issue](https://github.com/pragmatic-design/Pragmatic.Design/issues/new/choose), with the bug template |
| A security vulnerability | Never a public issue: see [SECURITY.md](SECURITY.md) |
| A commercial license | [pragmaticdesign.net](https://www.pragmaticdesign.net) |

## Before you ask

- The [documentation](https://docs.pragmaticdesign.net) has a page per module, and the
  [diagnostics reference](https://docs.pragmaticdesign.net/reference/diagnostics/) explains every `PRAG`
  code with its fix — most compile errors are answered there.
- Most modules have a `docs/common-mistakes.md` and a `docs/troubleshooting.md`, which cover what goes
  wrong most often.
- The generated code is in `obj/` and is ordinary C#: reading it usually shows what the generator made of a
  declaration.

## What to include

The package versions, the .NET SDK version (`dotnet --version`), the smallest declaration that shows the
behaviour, and what you expected instead. A diagnostic's full text, with its `PRAG` code, saves a round
trip.

## What to expect

During the alpha Pragmatic.Design has a single maintainer. Questions are answered on a best-effort basis; security reports follow the timeline in [SECURITY.md](SECURITY.md); commercial licensees get the
support their agreement names.
