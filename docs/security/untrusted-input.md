# Parsers that read untrusted input

The code in this repository that turns bytes or text a caller controls into values: the targets a fuzzer
should reach first. A parser here that throws something other than its declared failure, hangs, or allocates
without bound on a malformed input is a denial of service.

Only code the framework owns is listed. ASP.NET Core's own parsing (headers, route templates, form bodies) and
`System.Text.Json`'s reader are out of scope: they are fuzzed upstream. What is in scope is ours on top of them —
the converters `System.Text.Json` calls, the binders the generator emits, and the native libraries.

## Reached from an HTTP request

| Entry point | Source | How a request reaches it |
|---|---|---|
| JSON converters of Pragmatic value types | `Pragmatic.Temporal.Json/Converters/` (dates, times, durations, periods, ranges, zoned date-times, cron), `Pragmatic.Internationalization.AspNetCore/Json/Converters/` (money, localized strings, country, currency, language and culture codes), `Pragmatic.Patch/Serialization/OptionalConverter.cs` | A request body is read by `ReadFromJsonAsync` with the generated metadata (`RequestDelegateRenderer`); every member of these types goes through its converter |
| Text parsing of the temporal types | `Pragmatic.Temporal/Types/*.Parsing.cs` (`IParsable` on `LocalDate`, `LocalTime`, `LocalDateTime`, `Duration`, `Period`) | A route or query parameter of an `IParsable` type is bound by parsing it (`BindKind.Parsable`, `BindKindClassifier`) |
| `[ComplexFilter]` query parameters | `QueryHandlerTemplate` (generator): `JsonSerializer.Deserialize` of the query-string value | A query endpoint with a complex filter parameter deserializes the raw query string as JSON |
| Grid adapters | `Pragmatic.Persistence/Query/Adapters/PrimeNGAdapter.cs`, `DevExpressAdapter.cs` (`Enum.Parse` of client values, field names resolved against the entity), and the `Apply(query, DevExpressLoadOptions)` / `Apply(query, PrimeNGLazyLoadEvent)` overloads `GridAdapterTemplate` generates for a `[GridAdapter]` | A grid endpoint passes the client grid's load options or lazy-load event, filter tree included (`DevExpressSearchPropertiesEndpoint` in the Showcase) |
| Culture from `Accept-Language` and the query string | `Pragmatic.Internationalization.AspNetCore/Middleware/I18nContextMiddleware.cs` → `CultureCode.TryFromString` (`CultureCode.Factories.cs`) | Every request, when the I18n middleware is on; the header is parsed by ASP.NET Core, the culture name by us |
| CSV | `Pragmatic.Documents.Csv/CsvReader.cs` | An application that imports an uploaded file |
| XLSX | `Pragmatic.Documents.Xlsx/XlsxReader.cs` (a ZIP of XML parts) | An application that imports an uploaded file |
| Images (native, Rust) | `pragmatic_image_decode`, `pragmatic_image_info`, `pragmatic_image_strip_exif` in `Pragmatic.Imaging/native/pragmatic-imaging/`; .NET side `ImagePipeline.Load` | An application that processes an uploaded image |
| PDF (native, Rust) | `pragmatic_pdf_merge`, `pragmatic_pdf_split`, `pragmatic_pdf_page_count` in `Pragmatic.Documents/native/pragmatic-pdf/`; .NET side `PdfOperations` | An application that merges, splits or counts the pages of an uploaded PDF |

## Reached from another process

| Entry point | Source | Who sends it |
|---|---|---|
| Agent frames | `Pragmatic.Agent.Protocol/FrameCodec.cs` (`TryDecode`, `ReadFrameAsync`: a big-endian length bounded by `MaxFrameSize`, 16 MB, then the payload through the wire format) | The peer on the agent connection |
| Broker messages | `IMessageTypeRegistry.Deserialize` (type name and JSON payload), `SqlQueueConsumer` headers | Another service, through the broker or the SQL queue |
| Error bodies of a remote API | `Pragmatic.Result/Serialization/ErrorJson.cs` (`ReadDiscriminator`, `Read`) | The server a generated client calls |

## Not untrusted by default

Templates (`Pragmatic.Documents.Templating/Expressions/ExpressionParser.cs`, `Pragmatic.Documents.Markup/PdxDocParser.cs`,
`PdxEmailParser.cs`) are written by the application's developers. They become untrusted input in an
application that lets its users author templates, and belong in the first table there.

`SignedId.TryDecode` and `ShortGuid.TryDecode` are called by application code, not by any binder the framework
emits; an application that takes them from a route reaches them from a request.

`pragmatic_doc_to_pdf` reads JSON the .NET side writes from its own document model, not from a caller.

## How this list was made

By searching the runtime sources (`src/`, generators excluded) for public parse, read, decode and load methods,
`JsonConverter<T>` implementations and the native libraries' exported functions, then following each to where a
request or a peer reaches it. A parser added later is not here until someone adds it; the fuzz harnesses
(#158) are written against this list.
