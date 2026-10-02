# Licensing

> **Preview — terms being finalised.** Pragmatic.Design is in public preview. The licensing
> **model** below is settled; the exact legal text of the source-available license is being
> reviewed before the first public release. This page is the authoritative plain-language summary.

Pragmatic.Design is **dual-licensed**, per package:

| Bucket | License | What it means |
|--------|---------|---------------|
| **Foundation, capabilities & tooling** | **MIT** | Free for everyone, forever — including production and commercial use, at any scale. |
| **Framework runtime (the value)** | **PolyForm Small Business 1.0.0** | Free for small businesses; a commercial license is required at scale (see threshold below). |

There is **no separate "open-core bait"**: the split is *functional*. The individual building
blocks are free; you pay only when you run the **composed framework runtime** at the scale of a
larger business.

## The threshold (PolyForm Small Business)

The framework-runtime packages use the standard, lawyer-drafted
[PolyForm Small Business 1.0.0](https://polyformproject.org/licenses/small-business/1.0.0/) license,
**adopted as-is**. Under it, you may use these packages **for free** — including in production and in
commercial products — as long as your company (with its affiliates) is a *small business*, i.e. it has:

- **less than US $1,000,000** in total annual gross revenue, **and**
- **fewer than 100** employees and contractors.

If you exceed **either** limit, you need a **commercial license** (below). This deliberately keeps the
framework free for individuals, students, open-source projects, and small companies, and asks larger
organisations — who extract value from it at scale — to pay their fair share. It is what sustains
full-time development.

## No lock-in

Pragmatic generates code **into your project** — that source is **yours** and stays compilable even
without a license to the generator. There is **no time-bomb and no conversion clause**: a version you
are entitled to use never stops working. You are paying for the framework, not renting your own code.

## Commercial license (at or above the threshold)

Larger organisations buy a **per-project commercial license** (not per-developer), offered in tiers:

| Tier | For |
|------|-----|
| **Base** | A single application/service composed with the framework. |
| **Medium** | A larger product line / multiple services. |
| **Advanced** | Organisation-wide use + priority support. |

Pricing is fixed per project (in the low thousands of euros), not per seat. Contact
**[pragmaticdesign.net](https://www.pragmaticdesign.net)** for a commercial license.

## Which packages are which

**MIT (free for everyone):** the foundation, the standalone capabilities, and the source generator —
each competes with a single-purpose OSS library and is free on its own.

- `Pragmatic.Result`, `Pragmatic.Ensure`, `Pragmatic.Abstractions`, `Pragmatic.Specification`
- `Pragmatic.Validation`, `Pragmatic.Mapping`, `Pragmatic.Caching`, `Pragmatic.Patch`
- `Pragmatic.Temporal`, `Pragmatic.Internationalization`, `Pragmatic.Resilience`, `Pragmatic.Configuration`
- `Pragmatic.Logging`, `Pragmatic.FeatureFlags`, `Pragmatic.Discovery`, `Pragmatic.Storage`
- `Pragmatic.SourceGenerator` (the compile-time engine — the value it generates lives in the runtime packages below)
- `Pragmatic.Testing` (+ its generators: assertions, generated mocks and comparers, the contract-test host)

**PolyForm Small Business (free for small businesses, commercial above the threshold):** the
domain-driven runtime and the composed-framework experience.

- `Pragmatic.Persistence` (+ `.EFCore`), `Pragmatic.Actions` (+ `.EFCore`)
- `Pragmatic.Endpoints` (+ `.AspNetCore`, `.OpenApi`), `Pragmatic.Composition` (+ `.Host`)
- `Pragmatic.Events` (+ `.EFCore`), `Pragmatic.Messaging` (+ transports), `Pragmatic.Jobs`, `Pragmatic.Migrations`
- `Pragmatic.Identity` (+ `.Local`, `.Jwt`, `.Persistence`), `Pragmatic.Authorization`, `Pragmatic.MultiTenancy`
- `Pragmatic.ControlPlane`, `Pragmatic.Client`
- Medium Blocks: `Pragmatic.Comments`, `Pragmatic.Tags`, `Pragmatic.Attachments`, `Pragmatic.Notes`
- Documents & media: `Pragmatic.Documents`, `Pragmatic.Imaging`, `Pragmatic.Email`, `Pragmatic.Notifications`
- Compliance: `Pragmatic.Audit`, `Pragmatic.Privacy`, `Pragmatic.Cryptography`, `Pragmatic.Redaction`, `Pragmatic.Incidents`
- Platform (preview): `Pragmatic.Agent`, `Pragmatic.Gateway`

Each published NuGet package includes the full text of its applicable license (MIT or PolyForm Small
Business). The per-package mapping is defined centrally in `Directory.Build.props`; this page is the
authoritative human-readable map.

## Contributions

By contributing you agree your contribution is licensed to the project under the same terms as the
package it targets (MIT for the MIT packages; PolyForm Small Business for the rest), and that the
maintainer may also offer it under the commercial license. See [CONTRIBUTING.md](../CONTRIBUTING.md).
