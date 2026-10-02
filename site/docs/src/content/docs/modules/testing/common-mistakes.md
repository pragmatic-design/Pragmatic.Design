---
title: "Common Mistakes"
description: "**Wrong:**"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Testing/docs/common-mistakes.md
sidebar:
  order: 4
---
## 1. Reusing the shared client for a denial test

**Wrong:**

```csharp
// The shared contract client carries X-User-Permissions: *
var response = await Client.GetAsync("/api/invoices/" + id);
response.ShouldBeForbidden();   // fails: the caller has every permission
```

**Right:**

```csharp
using var request = new HttpRequestMessage(HttpMethod.Get, "/api/invoices/" + id);
request.AsUser("no-perm-user");          // empty permission header overrides the client default
var response = await Client.SendAsync(request);
response.ShouldBeRejected();
```

**Why:** `HttpClient` applies its `DefaultRequestHeaders` to every request that does not already carry
them. Setting identity on the *request* is what makes an underprivileged caller actually
underprivileged. `AsUser` on a request always writes the permission header — empty when you pass no
permission — precisely so the default cannot leak in.

---

## 2. Reading `ShouldBeRejected` as "returned 403"

**Wrong assumption:** a green `_WithoutRequiredPermission_IsRejected` proves the authorization filter
ran and denied the call.

**Right understanding:** it proves the caller did not succeed. Any 4xx satisfies it — including a 400
from body binding and a 404 from an entity lookup, both of which happen before authorization on some
endpoints.

**Why:** requiring a literal 403 would fail on endpoints that legitimately reject earlier. When you need
the stronger guarantee, assert it explicitly:

```csharp
response.ShouldHaveStatus(HttpStatusCode.Forbidden);
```

---

## 3. Expecting `ShouldNotBeForbidden` to mean "succeeded"

**Wrong:**

```csharp
var response = await Client.SendAsync(request);
response.ShouldNotBeForbidden();
var dto = await response.Content.ReadFromJsonAsync<GuestDto>();   // may be null: a 500 passes the assertion
```

**Right:**

```csharp
response.ShouldBeOk();
```

**Why:** `ShouldNotBeForbidden` fails only on 401 and 403. A 404 passes — which is intended, since the
generated contract uses a random id — but so does a 500. Use it for the authorization question only.

---

## 4. Forgetting the collection fixture

**Symptom:** every generated test throws

```
PragmaticContractHost.Client was not set. The consumer must define a collection fixture for the
'PragmaticContractTests' collection that boots the app and assigns it.
```

**Why:** the generated classes deliberately have no per-class setup — they read a client the consumer
supplies once. Define the fixture as shown in [Getting Started](/modules/testing/getting-started/).

---

## 5. Expecting contract tests for an unimplemented endpoint

**Symptom:** an endpoint exists but no contract test was generated for it.

**Why:** endpoints whose body is still `throw Behavior.Pending()` are flagged
`[assembly: PendingContract]` by the app's source generator and skipped — a not-yet-implemented
endpoint has no contract to hold it to. Implement the body and the tests appear on the next build.

Also check the endpoint actually requires a permission: authorization contracts are only generated for
endpoints that declare one, and rejection tests are skipped for list/search GETs, which data-scope
instead of rejecting.

---

## 6. Testing more than one application in the same test process

**Symptom:** tests interfere; the client points at the wrong app.

**Why:** `PragmaticContractHost.Client` is a single static, set once by the collection fixture. That is
what lets the generated classes avoid per-class wiring, but it means one application under test per test
assembly. Split into separate test projects if you need two.

⚠️ **One application made of several services is a different thing, and it is supported**: register a
client per boundary with `PragmaticContractHost.UseClientFor("Intake", …)` and each half of the
generated suite goes to the host that owns it — see
[Contract tests](/modules/testing/contract-tests/#an-application-that-is-more-than-one-service). What is not supported
is two unrelated applications, because the statics are one set per process.

---

## 7. Reading the generated classes as the list of what is covered

**Symptom:** the suite looks complete, and an operation nobody contracts is invisible.

**Why:** the generated classes report what was written. Nothing in them reports what was not, so an
operation that got no contract at all simply is not there — and an absence has no name. Read
`ContractCoverage.Operations`, and pin `ContractCoverage.Uncovered` in a test of your own:
[Contract tests](/modules/testing/contract-tests/#what-was-not-generated-and-why).

⚠️ The commonest reason is an operation with **no `[RequirePermission]`**: nothing refuses it, so an
authorization contract would have no refusal to assert. Measured on a real application where four
operations were in that state, and reading the generated suite could not have told anybody.
