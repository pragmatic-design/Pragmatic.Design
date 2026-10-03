# Documentation

Where to start, by what you need. When a document and the code disagree, the code is right.

## Start here

1. The root [README](../README.md): what the framework is and what it generates.
2. The `README.md` of the module you need, and its `docs/` folder.
3. A guide under [`howto/`](howto/) for a concrete task.

Every module has its own `README.md` with an overview, a quick start and its status; most have a
`docs/` folder with concepts, a getting-started guide, common mistakes and troubleshooting. The same
pages are published on the documentation site.

## Guides

| Task | Guide |
|---|---|
| Authentication and authorization | [`howto/authentication-authorization.md`](howto/authentication-authorization.md) |
| Native AOT and trimming | [`howto/aot-and-trimming.md`](howto/aot-and-trimming.md) |
| Serving the documentation the generator emits | [`howto/consuming-generated-docs.md`](howto/consuming-generated-docs.md) |
| A diagnostic you do not understand | [`howto/diagnostics-troubleshooting.md`](howto/diagnostics-troubleshooting.md), and every ID in [`diagnostics.md`](diagnostics.md) |
| Schema changes on concurrent branches | [`howto/migrations-concurrent-development.md`](howto/migrations-concurrent-development.md) |
| Hardening a deployment | [`howto/security-hardening.md`](howto/security-hardening.md) |
| A tour of the Showcase | [`howto/showcase-walkthrough.md`](howto/showcase-walkthrough.md) |
| Testing the packages from a local feed | [`howto/local-nuget-server.md`](howto/local-nuget-server.md) |
| Working in this repository | [`howto/monorepo-structure.md`](howto/monorepo-structure.md), [`howto/ide-tips.md`](howto/ide-tips.md) |

## Reference

| | |
|---|---|
| The rules a change follows | [`CONVENTIONS.md`](CONVENTIONS.md) |
| Every `PRAG` diagnostic, generated from the descriptors | [`diagnostics.md`](diagnostics.md) |
| How a change is verified, and how to read the gate | [`TESTING.md`](TESTING.md) |
| Security: OWASP Top 10 coverage and the threat model | [`security/`](security/) |
| Licensing: which package is under which licence | [`LICENSING.md`](LICENSING.md) |
| Status of the release, and what moves before 1.0 | [`ROADMAP.md`](ROADMAP.md) |

## The source generator

| | |
|---|---|
| Overview | [`../Pragmatic.SourceGenerator/README.md`](../Pragmatic.SourceGenerator/README.md) |
| Contributor documentation | [`../Pragmatic.SourceGenerator/docs/`](../Pragmatic.SourceGenerator/docs/) |
| The template API (`CSharpTemplate`) | [`../shared/SourceGen/README.md`](../shared/SourceGen/README.md) |
| Testing generator output | [`../shared/SourceGen/Testing/README.md`](../shared/SourceGen/Testing/README.md) |

## Examples

The [Showcase](../examples/showcase/) runs three bounded contexts end to end. The reference
applications grow one level of scale each: [Time off](../examples/time-off/README.md),
[Invoicing](../examples/invoicing/README.md), [Casework](../examples/casework/README.md) and
[Warehouse](../examples/warehouse/README.md). [Conformance](../examples/conformance/README.md) exercises
every shape the framework accepts.
