using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Endpoints.Diagnostics;
using Pragmatic.Endpoints.Responses;
using Pragmatic.Serialization;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

/// <summary>
///     A generated JSON response is written by the generated writer exactly while the host's options are the ones
///     the generated entry point wrote, and by the serializer, with the same status and headers, otherwise.
/// </summary>
/// <remarks>
///     The writer under test writes <c>{"via":"writer"}</c>, which the serializer never would: the body says which
///     of the two wrote it.
/// </remarks>
public class AGeneratedJsonResponseTests
{
    private const string ByTheWriter = """{"via":"writer"}""";
    private const string ByTheSerializer = """{"title":"x"}""";

    private static readonly Action<Utf8JsonWriter, Card, JsonSerializerOptions> Writer = static (writer, _, _) =>
    {
        writer.WriteStartObject();
        writer.WriteString("via", "writer");
        writer.WriteEndObject();
    };

    [Fact]
    public async Task OptionsTheEntryPointWrote_AnswerThroughTheWriter()
    {
        var (status, contentType, body, _) = await Execute(new Card("x"), HostOptions());

        body.Should().Be(ByTheWriter);
        status.Should().Be(200);
        contentType.Should().Be("application/json; charset=utf-8");
    }

    [Fact]
    public async Task AConverterAddedAfterTheEntryPoint_SendsTheResponseToTheSerializer()
    {
        var options = HostOptions();
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<DayOfWeek>());

        (await Execute(new Card("x"), options)).Body.Should().Be(ByTheSerializer);
    }

    [Fact]
    public async Task ANamingPolicyChangedAfterTheEntryPoint_SendsTheResponseToTheSerializer()
    {
        var options = HostOptions();
        options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;

        (await Execute(new Card("x"), options)).Body.Should().Be(ByTheSerializer);
    }

    [Fact]
    public async Task OptionsTheEntryPointNeverMarked_AnswerThroughTheSerializer()
        => (await Execute(new Card("x"), HostOptions(mark: false))).Body.Should().Be(ByTheSerializer);

    /// <summary>
    ///     A converter for a type the writer writes, registered before the entry point as the framework's modules
    ///     register theirs: the serializer would use it, so the writer is not.
    /// </summary>
    [Fact]
    public async Task AConverterForATypeTheWriterWrites_SendsTheResponseToTheSerializer()
    {
        var options = HostOptions(before: o => o.Converters.Add(new CardConverter()));

        (await Execute(new Card("x"), options)).Body.Should().Be("""{"card":"converted"}""");
    }

    /// <summary>The control: a converter for a type the writer never writes leaves the writer in place.</summary>
    [Fact]
    public async Task AConverterForAnotherType_LeavesTheWriterInPlace()
    {
        var options = HostOptions(before: o => o.Converters.Add(new JsonStringEnumConverter<DayOfWeek>()));

        (await Execute(new Card("x"), options)).Body.Should().Be(ByTheWriter);
    }

    /// <summary>
    ///     An enum is written by name as <c>JsonStringEnumConverter</c> writes it: a converter of the application's
    ///     claiming it first would write something else.
    /// </summary>
    [Fact]
    public async Task AnEnumClaimedByAnotherConverter_SendsTheResponseToTheSerializer()
    {
        var enumShape = new GeneratedJsonShape([typeof(Card)], [typeof(DayOfWeek)], [null], needsInfrastructureExclusion: false);

        (await Execute(new Card("x"), HostOptions(), shape: enumShape)).Body.Should().Be(ByTheWriter);

        var claimed = HostOptions(before: o => o.Converters.Add(new JsonStringEnumConverter<DayOfWeek>(JsonNamingPolicy.KebabCaseLower)));
        (await Execute(new Card("x"), claimed, shape: enumShape)).Body.Should().Be(ByTheSerializer);
    }

    /// <summary>The enum's generated <c>[FastEnum]</c> converter, which the shape names, claims it as expected.</summary>
    [Fact]
    public async Task AnEnumClaimedByItsFastEnumConverter_LeavesTheWriterInPlace()
    {
        var fastEnum = new JsonStringEnumConverter<DayOfWeek>();
        var enumShape = new GeneratedJsonShape([typeof(Card)], [typeof(DayOfWeek)], [fastEnum.GetType()], needsInfrastructureExclusion: false);
        var options = HostOptions(before: o => o.Converters.Add(fastEnum));

        (await Execute(new Card("x"), options, shape: enumShape)).Body.Should().Be(ByTheWriter);
    }

    [Fact]
    public async Task AModifierOnTheSeam_SendsTheResponseToTheSerializer()
    {
        var options = HostOptions(seam: new PragmaticJsonOptions().AddModifier(static _ => { }));

        (await Execute(new Card("x"), options)).Body.Should().Be(ByTheSerializer);
    }

    /// <summary>
    ///     A modifier that says which types it touches, as Temporal's does: the writer of a type it leaves alone stays
    ///     in use, and the writer of one it touches does not.
    /// </summary>
    [Fact]
    public async Task AModifierThatSaysWhatItTouches_DecidesPerWriter()
    {
        var elsewhere = HostOptions(seam: new PragmaticJsonOptions().AddModifier(static _ => { }, static t => t == typeof(DayOfWeek)));
        (await Execute(new Card("x"), elsewhere)).Body.Should().Be(ByTheWriter);

        var here = HostOptions(seam: new PragmaticJsonOptions().AddModifier(static _ => { }, static t => t == typeof(Card)));
        (await Execute(new Card("x"), here)).Body.Should().Be(ByTheSerializer);
    }

    /// <summary>
    ///     A resolver ASP.NET puts ahead of the entry point's, as OpenAPI puts its schema context: one that knows nothing
    ///     of the writer's types changes nothing, one that answers for them would write them its own way.
    /// </summary>
    [Fact]
    public async Task AResolverAheadOfTheEntryPoints_DecidesByWhatItAnswersFor()
    {
        var unrelated = HostOptions();
        unrelated.SerializerOptions.TypeInfoResolverChain.Insert(0, new AnswersFor(typeof(DayOfWeek)));
        (await Execute(new Card("x"), unrelated)).Body.Should().Be(ByTheWriter);

        var answering = HostOptions();
        answering.SerializerOptions.TypeInfoResolverChain.Insert(0, new AnswersFor(typeof(Card)));
        (await Execute(new Card("x"), answering)).Body.Should().Be(ByTheSerializer);
    }

    [Fact]
    public async Task AWriterThatStripsInfrastructure_IsUsedOnlyByAHostThatStripsIt()
    {
        (await Execute(new Card("x"), HostOptions(excludesInfrastructure: false), needsExclusion: true)).Body
            .Should().Be(ByTheSerializer);

        (await Execute(new Card("x"), HostOptions(excludesInfrastructure: true), needsExclusion: true)).Body
            .Should().Be(ByTheWriter);
    }

    /// <summary>
    ///     A value of a type derived from the one the writer was planned for: the serializer writes an
    ///     <c>object</c> as its runtime type, so the derived members go out, and the writer would drop them.
    /// </summary>
    [Fact]
    public async Task AValueOfADerivedType_IsWrittenByTheSerializerAsItsRuntimeType()
    {
        var options = HostOptions();
        var response = new GeneratedJsonResponse<Shape>(new Circle { Name = "c", Radius = 2 }, 200, null,
            static (writer, _, _) => writer.WriteStringValue("writer"),
            new GeneratedJsonShape([typeof(Shape), typeof(string)], [], [], needsInfrastructureExclusion: false));

        (await Run(response, options)).Body.Should().Be("""{"radius":2,"name":"c"}""");
    }

    /// <summary>
    ///     The writer is handed the very options the host answers with, which is what it writes a member typed
    ///     <c>object</c> with (#130).
    /// </summary>
    [Fact]
    public async Task TheWriter_IsGivenTheHostsOwnOptions()
    {
        var options = HostOptions();
        JsonSerializerOptions? given = null;
        var response = new GeneratedJsonResponse<Card>(new Card("x"), 200, null,
            (writer, _, received) =>
            {
                given = received;
                writer.WriteStartObject();
                writer.WriteEndObject();
            },
            CardShape(needsExclusion: false));

        await Run(response, options);

        ReferenceEquals(given, options.SerializerOptions).Should().BeTrue();
    }

    [Fact]
    public async Task ACreated_CarriesTheStatusAndTheLocationEitherWay()
    {
        var byTheWriter = await Execute(new Card("x"), HostOptions(), statusCode: 201, location: "/cards/1");
        var bySerializer = await Execute(new Card("x"), HostOptions(mark: false), statusCode: 201, location: "/cards/1");

        byTheWriter.Body.Should().Be(ByTheWriter);
        bySerializer.Body.Should().Be(ByTheSerializer);

        foreach (var answer in new[] { byTheWriter, bySerializer })
        {
            answer.Status.Should().Be(201);
            answer.Location.Should().Be("/cards/1");
            answer.ContentType.Should().Be("application/json; charset=utf-8");
        }
    }

    [Fact]
    public async Task AnotherStatus_IsKeptEitherWay()
    {
        var byTheWriter = await Execute(new Card("x"), HostOptions(), statusCode: 202);
        var bySerializer = await Execute(new Card("x"), HostOptions(mark: false), statusCode: 202);

        byTheWriter.Status.Should().Be(202);
        byTheWriter.Body.Should().Be(ByTheWriter);
        bySerializer.Status.Should().Be(202);
        bySerializer.Body.Should().Be(ByTheSerializer);
    }

    /// <summary>The answer is kept once the options are read-only, and it is still the right one.</summary>
    [Fact]
    public async Task OptionsMadeReadOnly_KeepTheirAnswer()
    {
        var options = HostOptions();
        options.SerializerOptions.MakeReadOnly();

        (await Execute(new Card("x"), options)).Body.Should().Be(ByTheWriter);
        (await Execute(new Card("x"), options)).Body.Should().Be(ByTheWriter);
    }

    [Fact]
    public async Task EachResponse_IsCountedUnderTheWriterThatWroteIt()
    {
        var counted = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (ReferenceEquals(instrument, EndpointResponseMetrics.JsonResponses))
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
                if (tag.Key == EndpointResponseMetrics.WriterTag)
                    lock (counted) counted.Add((string)tag.Value!);
        });
        listener.Start();

        await Execute(new Card("x"), HostOptions());
        await Execute(new Card("x"), HostOptions(mark: false));

        // Other tests run in parallel and are counted too: what this one wrote is among them.
        lock (counted)
        {
            counted.Should().Contain("generated");
            counted.Should().Contain("serializer");
        }
    }

    /// <summary>
    ///     An encoder set after the entry point: ASP.NET's own is the relaxed one, and the writer escapes as it does.
    /// </summary>
    [Fact]
    public async Task AnEncoderChangedAfterTheEntryPoint_SendsTheResponseToTheSerializer()
    {
        var options = HostOptions();
        options.SerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default;

        (await Execute(new Card("x"), options)).Body.Should().Be(ByTheSerializer);
    }

    /// <summary>The host's options, configured as the generated entry point configures them.</summary>
    private static JsonOptions HostOptions(
        bool mark = true, bool excludesInfrastructure = false, Action<JsonSerializerOptions>? before = null, PragmaticJsonOptions? seam = null)
    {
        var json = new JsonOptions();
        seam ??= new PragmaticJsonOptions();

        // What other modules configured before the entry point did, as Internationalization and Temporal do.
        before?.Invoke(json.SerializerOptions);

        json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        json.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        json.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        json.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        json.SerializerOptions.TypeInfoResolver = seam.Build().TypeInfoResolver;

        if (mark)
            GeneratedJsonDefaults.Mark(json.SerializerOptions, seam, excludesInfrastructure);

        return json;
    }

    /// <summary>What the writer under test writes, as the generator would describe it.</summary>
    private static GeneratedJsonShape CardShape(bool needsExclusion = false)
        => new([typeof(Card), typeof(string)], [], [], needsExclusion);

    private static Task<Answer> Execute(
        Card value, JsonOptions options, int statusCode = 200, string? location = null, bool needsExclusion = false,
        GeneratedJsonShape? shape = null)
        => Run(new GeneratedJsonResponse<Card>(value, statusCode, location, Writer, shape ?? CardShape(needsExclusion)), options);

    private static async Task<Answer> Run(IResult result, JsonOptions options)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddLogging()
                .AddSingleton(Options.Create(options))
                .BuildServiceProvider(),
        };
        var body = new MemoryStream();
        context.Response.Body = body;

        await result.ExecuteAsync(context).ConfigureAwait(false);

        return new Answer(
            context.Response.StatusCode,
            context.Response.ContentType,
            Encoding.UTF8.GetString(body.ToArray()),
            context.Response.Headers.Location.ToString());
    }

    private sealed record Answer(int Status, string? ContentType, string Body, string Location);

    public sealed record Card(string Title);

    public class Shape
    {
        public string Name { get; init; } = "";
    }

    public sealed class Circle : Shape
    {
        public int Radius { get; init; }
    }

    /// <summary>A resolver that answers for one type, through the default resolver, and for nothing else.</summary>
    private sealed class AnswersFor(Type answered) : IJsonTypeInfoResolver
    {
        private readonly DefaultJsonTypeInfoResolver _inner = new();

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
            => type == answered ? _inner.GetTypeInfo(type, options) : null;
    }

    /// <summary>A converter of the application's for a type the writer writes.</summary>
    private sealed class CardConverter : JsonConverter<Card>
    {
        public override Card Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, Card value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("card", "converted");
            writer.WriteEndObject();
        }
    }
}
