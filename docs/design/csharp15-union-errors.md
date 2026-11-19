# C# 15 unions and closed hierarchies as the error type

Status: **decided, option A** (owner, 2026-11-19): unions only, read structurally with Roslyn 4.14; `closed`
hierarchies stay unsupported as an error type. The `IError` of a union is implemented (#865); the
per-case contract is #866. The study below was measured on 2026-11-18 with SDK 11.0.100 (Roslyn 5.12) and
SDK 10.0.404 (Roslyn 5.9), on Windows x64 (#837, part of #820).

One fact the study did not have: a union declaration is a `TypeDeclarationSyntax` of kind
`UnionDeclaration` in Roslyn 5.x, and Roslyn marks that syntax API experimental (`RSEXPERIMENTAL006`).
The generator names neither: it looks the kind up by name on the compiler that runs it, and reads the
union from its symbol.

## The question

C# 15 lets an application write an error set once:

```csharp
public union PlaceOrderError(NotFoundError, ConflictError, OrderLocked);
```

Today the same set is an arity: `DomainAction<Order, NotFoundError, ConflictError, OrderLocked>`, or
`Result<Order, E1, E2, E3>`. The constraint is the one the owner set on 2026-11-17: the public API is
the same on every target framework, and the adoption is additive. A C# 15 user may use a `union` or a
`closed` hierarchy as the error type and gets what a plain error type gets. Nothing changes for a C# 14
user.

The study answers three questions:
1. How the generator reads a C# 15 declaration, given that it compiles against Roslyn 4.14.
2. Whether the docs should point C# 15 users from the arities to the union form.
3. Whether `Pragmatic.Result` should implement the runtime's `UnionAttribute` / `IUnion`.

## What the compiler produces

A union is a value type. In metadata it is a `struct` marked `[System.Runtime.CompilerServices.Union]`
that implements `System.Runtime.CompilerServices.IUnion`. It has one public constructor per case, and an
`object Value` property that holds the active case, boxed if it is a struct.

A `closed` class is `abstract` in metadata, marked `[IsClosedType]`.

A union can be `partial`, implement interfaces and declare members. It compiles and runs on the 11
runtime:

```csharp
public partial union PlaceOrderError(NotFoundError, ConflictError, OrderLocked) : IError;
public partial union PlaceOrderError
{
    private IError Active => (IError)Value!;
    public string Code => Active.Code;
    public int StatusCode => Active.StatusCode;
    // Title, Description, both WriteExtensions: the same delegation
}
```

That is the whole bridge to `Pragmatic.Result`. `Result<TValue, TError>` requires `TError : IError`, so a
union that implements `IError` is already a valid `TError`, with no change to `Result`.

## What a generator compiled against Roslyn 4.14 can see

A probe generator built against `Microsoft.CodeAnalysis.CSharp` 4.14.0 reported what it could see of each
declaration:

| | union, in source | union, from a reference | `closed`, in source | `closed`, from a reference |
|---|---|---|---|---|
| `TypeKind` | `Struct` | `Struct` | `Class`, abstract | `Class`, abstract |
| `AllInterfaces` | `IUnion` | `IUnion` | — | — |
| `GetAttributes()` | nothing | `UnionAttribute` | nothing | **nothing** (`IsClosedType` is not surfaced) |
| one-parameter constructors | one per case | one per case | — | — |
| syntax | `UnionDeclarationSyntax`, kind 9082 | — | `closed` modifier | — |

The probe loaded and ran in both compilers: Roslyn 5.12 (SDK 11.0.100) and Roslyn 5.9 (SDK 10.0.404,
invoked through its own `dotnet.dll` with the shared compiler off). The repository itself builds on SDK
11.0.100 with the 4.14 generator.

So:
- **A union is readable with the API we compile against**, the same way from source and from a
  referenced module: a struct that implements `IUnion`, whose cases are the parameters of its
  one-parameter constructors.
- **A `closed` hierarchy is not.** Its derived types can be found by walking the compilation, but
  `closed` itself is visible only as a syntax token, and only in the declaring compilation. A host reads
  a module from metadata, where the generator cannot tell `closed` apart from `abstract`.

Roslyn 5.9 has the typed API: `ITypeSymbol.IsUnion`, `ITypeSymbol.IsClosed`,
`ITypeSymbol.GetClosedDerivedTypeInfo()` (`Microsoft.CodeAnalysis.Common` 5.9.0, XML documentation). It
is the compiler of SDK 10.0.404, not of the first SDK 10 releases.

NuGet can carry one generator per Roslyn version: `analyzers/dotnet/roslyn{major.minor}/cs/`.
`Microsoft.Extensions.Logging.Abstractions` 10.0.x ships `roslyn3.11`, `roslyn4.0` and `roslyn4.4`. That
the SDK picks the highest folder the running compiler satisfies was **not measured here** with a
package of our own.

## The prototype, over HTTP

The prototype was built in a scratch directory, not committed: a module and a host on `net11.0`,
`LangVersion` preview, referencing this repository's projects and the generator as it is on `main`. The
action:

```csharp
[DomainAction]
[Endpoint(HttpVerb.Post, "api/orders")]
public partial class PlaceOrder : DomainAction<OrderPlaced, PlaceOrderError> { … }
```

with `PlaceOrderError` the partial union above, `IError` written by hand. `OrderLocked` is an
application error with status 423.

| request | answer |
|---|---|
| `{"outcome":"missing"}` | **404** |
| `{"outcome":"taken"}` | **409** |
| `{"outcome":"locked"}` | **423**, `{"title":"Locked","status":423,"code":"ORDER_LOCKED"}` |
| `{"outcome":"none"}` | **201**, `{"number":"o-1"}` |

At run time every case answers with the status it has as a plain error type: the response is built from
`IError.StatusCode`, and the union delegates it.

The contract does not follow. The generated endpoint documents the union as one error type and falls back
to **400**: `ProducesResponseTypeMetadata` for 201, 400, 401, 403, 500. The control,
`DomainAction<OrderPlaced, NotFoundError, ConflictError, OrderLocked>` in the same module, documents
**404, 409 and 423**. OpenAPI, the generated client and every consumer of the endpoint manifest see the
same gap, because they read the same error list.

## Answers

**1. Reading C# 15 declarations.** There are two ways:

- **(a) Structural, on Roslyn 4.14 — recommended.** Unions are read by shape: a struct that implements
  `IUnion`, whose cases are the parameters of its one-parameter constructors. No new build, no new
  package layout, and it works in source and across a reference. `closed` is out: the API cannot see it
  in metadata.
- **(b) A second generator build against Roslyn 5.9, in `analyzers/dotnet/roslyn5.9/cs`**, beside the
  current one moved to `roslyn4.14`. It gets `IsUnion`, `IsClosed` and `GetClosedDerivedTypeInfo`, so
  `closed` hierarchies work too. The cost:
  - two builds of the generator and of `shared/SourceGen`, and generator tests on both;
  - the pack layout of `Pragmatic.SourceGenerator` changes;
  - the folder selection is to be verified;
  - users on SDK 10.0.1xx–10.0.3xx (Roslyn 5.0) get the 4.14 build, and with it no union support.

**2. The arities in the docs.** They stay the contract on every target. A C# 15 user would be pointed
to the union form once the generator documents it per case. Until then a union compiles and answers
correctly, but its OpenAPI says 400. Recommendation: one paragraph in `Pragmatic.Result`'s docs, written
with the follow-up that expands the cases, not before.

**3. `UnionAttribute` / `IUnion` in `Pragmatic.Result`.** No. The runtime marks them as not user-facing
yet. The union an application declares already gets both from the compiler. Nothing in `Pragmatic.Result`
is a union.

## What it would take

| piece | what | depends on |
|---|---|---|
| **IError for a union** (#865) | For a `partial union` whose cases all implement `IError`, emit the delegation above. Without `partial`, or with a case that is not an `IError`, report a diagnostic, instead of the `CS0315` the user gets today on `Result<T, TUnion>` with no explanation. | 1(a) |
| **Per-case contract** (#866) | Where the endpoints, the manifest and the client read an operation's error types, expand a union into its cases. Each case is documented with the status, OpenAPI response and client type it has today as a plain error type. Measured over HTTP like the table above, against the arity control. | 1(a) |
| **JSON** | A union in a generated `JsonSerializerContext`. | #838, item 3 |
| **`closed` hierarchies** | Only with 1(b). | the choice below |

Every piece is additive: each one acts only on an error type that implements `IUnion`, and a C# 14
application declares none, so its output stays today's. That is the property the follow-ups must pin with
their snapshots. This study changed no code, and the gate's snapshot and integration suites ran green
untouched.

The `CS0315` was measured in the prototype: `DomainAction<OrderPlaced, BareError>` with
`union BareError(NotFoundError, ConflictError)` and no `IError` reads "no boxing conversion from
`BareError` to `Pragmatic.Result.IError`".

## The choice for the owner

| option | gives | costs |
|---|---|---|
| **A. Unions only, structural (1a)** | union error sets with the same status, OpenAPI and client as an arity, from SDK 10 to 11 | two follow-ups in the generator; `closed` hierarchies stay unsupported as an error type |
| **B. A and a Roslyn 5.9 generator build (1b)** | A, plus `closed` hierarchies | the dual build, pack and test matrix above, kept for as long as SDKs older than 10.0.4xx are supported |
| **C. Nothing yet** | — | a union as `TError` keeps compiling only with a hand-written `IError`, and keeps documenting 400 |

Recommendation: **A**. Revisit B when the supported SDK floor reaches Roslyn 5.9, which removes the dual
build.
