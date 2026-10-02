# Pragmatic.Composition.RemoteBoundary.Samples

Single-process demo of the `[RemoteBoundary<TModule>]` HTTP wire protocol.

## What this shows

- `HttpActionInvoker<TAction, TResult>` calling a local server that hosts the `/_pragmatic/invoke` dispatcher
- Three scenarios:
  1. **Happy path** — `CalculateTaxAction` returns a `TaxQuote` for region `IT`
  2. **Typed error** — region `XX` returns `NotFoundError` preserved across the wire
  3. **Unknown action** — server rejects an unregistered action type with a dispatcher error

The server is in-process on `localhost:5390`; the client is the same process. In production a remote boundary points to a different host and is driven by `[RemoteBoundary<TModule>]` attributes — the generator emits the `HttpActionInvoker` per action.

## Run

```bash
dotnet run --project Pragmatic.Composition/samples/Pragmatic.Composition.RemoteBoundary.Samples
```

Expected output: three scenarios each printing the request, the response, and a verdict line.

## Full example

The attribute-driven pipeline with two real assemblies lives in the Showcase:

- `examples/showcase/src/Showcase.Billing.Host/` — standalone billing host
- `examples/showcase/src/Showcase.Host.Distributed/` — booking host that calls billing via remote boundary
