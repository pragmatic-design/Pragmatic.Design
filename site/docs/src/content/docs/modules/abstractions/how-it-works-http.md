---
title: "Http: what a request may bring in, and where a request may go out"
description: "> **Scope**: `src/Pragmatic.Abstractions/Http/`, 3 files."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Abstractions/docs/how-it-works/http.md
sidebar:
  order: 14
---
> **Scope**: `src/Pragmatic.Abstractions/Http/`, 3 files.
> `MaxBodySizeMetadata` · `OutboundUrlGuard` · `OutboundUrlVerdict`
>
> **Not covered here**: the `[MaxBodySize]` attribute that feeds the metadata lives in
> `Pragmatic.Endpoints`, and the pipeline step that enforces it lives in
> `Pragmatic.Composition.Host`; both are named below, neither is opened here.

For the member-by-member catalogue, see [interfaces](/modules/abstractions/interfaces/#24-http). This document is
about how the pieces fit together.

## Two halves of one concern

The folder holds the two directions in which an HTTP application is exposed to something it does
not control. Inbound: a body whose size the caller chooses. Outbound: a destination whose address
someone else supplied. Neither half depends on the other; they share a folder because they share
the reason for existing.

Both types are plain data or pure functions with no ASP.NET dependency, which is what lets them
sit in Abstractions at all, and lets the enforcement live in the host where the pipeline is.

| Type | Namespace |
|---|---|
| `MaxBodySizeMetadata` | `Pragmatic.Http` |
| `OutboundUrlGuard`, `OutboundUrlVerdict` | `Pragmatic.Abstractions.Http` |

## `MaxBodySizeMetadata`: a limit that travels with the endpoint

A `sealed record MaxBodySizeMetadata(long MaxBytes)`, and nothing else. Its value is entirely in
where it is attached and when it is read.

The chain has three links, in three assemblies:

1. **You write** `[MaxBodySize(bytes)]` (`Pragmatic.Endpoints/Attributes/MaxBodySizeAttribute.cs`).
2. **The generator emits a call**, not a type: one line of the form
   `{builder}.WithMetadata(new global::Pragmatic.Http.MaxBodySizeMetadata({bytes}L));` in each of
   the four endpoint handler templates:
   `EndpointHandlerTemplate.Configuration.cs`, `MutationHandlerTemplate.Configuration.cs`,
   `QueryHandlerTemplate.Configuration.cs`, `DomainActionHandlerTemplate.Configuration.cs`.
3. **The host reads it** in `Pragmatic.Composition.Host/Steps/RequestLimitsStep.cs`, through
   `Metadata.GetMetadata<MaxBodySizeMetadata>()`.

### Why endpoint metadata rather than configuration

Because the limit is a property of the operation, not of the deployment. An avatar upload and a
JSON command have nothing to say to each other about how many bytes are reasonable, and a single
server-wide ceiling has to be set for the largest of them.

Attaching it to the endpoint also buys the override rule for free: ASP.NET's `GetMetadata<T>`
returns the **last** entry, so an attribute on the endpoint wins over one applied to a group. That
is the behaviour a reader expects, and it is inherited rather than implemented.

### Why the step runs at order 55

`RequestLimitsStep.Order` is `55`, between routing at `50` and anything that touches the body.
Routing must have run first, or there is no endpoint whose metadata to read; the body must not have
been consumed yet, or the limit has already been paid for in bandwidth and memory.

The endpoint value is not the only source: the step falls back to
`Pragmatic:RequestLimits:MaxBodySizeBytes` from configuration, so an application can set a global
default and let `[MaxBodySize]` override it per operation. It is a default and not a ceiling: an
endpoint that declares a larger value gets the larger value. With neither present, no limit is
applied and the server's own default stands.

Inside the middleware there are two enforcement paths, because a client can be wrong in two ways. A
declared `Content-Length` above the limit is answered with 413 before `next()` is ever called, which
also means the limit holds on test servers, whose transport does not necessarily honour the body-size
feature. Then, where the transport exposes `IHttpMaxRequestBodySizeFeature` as writable, the step
sets it, so a request that understates or omits its length is cut off by the server as the bytes
actually arrive.

## `OutboundUrlGuard`: refusing a destination inside your own network

A static class with two entry points and an enum of verdicts, for the case where an application
fetches, posts to, or subscribes a URL that a user, a tenant, or an externally-edited configuration
file supplied.

The attack it addresses is positional: the attacker chooses the address, the server contributes its
place on the network. `http://169.254.169.254/` from a laptop is nothing; from inside a cloud VM it
is the metadata endpoint handing out credentials. The same holds for an internal admin service whose
only protection was the assumption that nothing could reach it.

### What each entry point actually checks

| Member | What it does |
|---|---|
| `Inspect(string?, bool allowHttp = false)` | Parses, then defers to the `Uri` overload. |
| `Inspect(Uri?, bool allowHttp = false)` | Scheme, embedded credentials, and (only if the host is a **literal address**) the address ranges. A DNS name is not resolved here. |
| `InspectResolvedAsync(Uri?, bool, CancellationToken)` | Everything the synchronous overload does, then resolves the host and checks every address it answers with. |

The synchronous overload is therefore a check on the *shape* of a URL; the address check for a named
host requires the async one.

Three decisions are worth naming:

**A verdict enum, not a bool.** `Allowed`, `NotAnAbsoluteUrl`, `SchemeNotAllowed`, `CredentialsInUrl`,
`ResolvesToInternalAddress`, `HostCouldNotBeResolved`. The distinctions matter because the response
differs: a wrong scheme is a typo worth showing the user, an address resolving inward is a probe
worth recording. A bool would collapse a mistake and an attempt into the same event.

**Any address, not all.** A name that answers with one public and one private address is refused.
Accepting it would leave the connection free to pick the private one, which is the entire trick.

**Failure to resolve is a refusal.** A host that cannot be shown safe does not pass. Defaulting to
"allow" on error is how a check of this kind stops working without anyone noticing.

`CredentialsInUrl` deserves its own line: `https://real-host@attacker/` reads as the real host to a
person and resolves to the attacker's host for a machine, so the presence of user info is refused
outright rather than parsed around.

### What counts as internal

Loopback, `0.0.0.0/8`, `10/8`, `172.16/12`, `192.168/16`, link-local `169.254/16` (which is also the
cloud metadata endpoint), carrier-grade NAT `100.64/10`, and everything from `224` up. IPv4-mapped
IPv6 addresses are unmapped first, so `::ffff:127.0.0.1` is not a way past the IPv4 branch. On the
IPv6 side: link-local, site-local, multicast, `::`, and unique-local `fc00::/7`, the last computed
by hand because the BCL exposes no property for it.

### The limitation, stated by the code itself

The remarks on `OutboundUrlGuard` say it plainly, and repeating it here is the point rather than a
caveat: **this cannot close SSRF on its own.** Checking a name means resolving it, and the
resolution that decides the connection is the one the HTTP stack performs a moment later: a name
can answer publicly here and privately there (DNS rebinding). This removes the easy cases. A
deployment that must actually hold uses an egress proxy or a network policy, where the decision is
made at connect time.

Where the set of legitimate hosts *is* known (`hooks.slack.com`, a fixed partner API), an allowlist
is strictly stronger and should be preferred. The guard is the fallback for when no such list can be
written.

## External references

Named here, described where they live:

- **`Pragmatic.Endpoints` → `MaxBodySizeAttribute`**: the authoring surface. The attribute and the
  metadata record are deliberately in different packages: the attribute belongs with endpoints, the
  metadata with the ASP.NET-free abstractions both the generator and the host can reference.
- **`Pragmatic.Composition.Host` → `RequestLimitsStep`**: the startup step, `Order = 55`, that turns
  the metadata into a 413 or a server-side body limit.
- **`Pragmatic.SourceGenerator` → `Features/Endpoints/Templates/`**: the four handler templates that
  emit the `WithMetadata` call.
