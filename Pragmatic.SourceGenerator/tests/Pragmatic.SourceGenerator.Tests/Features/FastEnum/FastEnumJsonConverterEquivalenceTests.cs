using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.FastEnum;

/// <summary>
///     The generated <c>{Type}JsonConverter</c> must be a drop-in replacement for the
///     <c>JsonStringEnumConverter</c> the host entry point registers today — otherwise wiring it up
///     silently changes the wire format of every API that exposes an enum.
///     <para>
///         Each case runs the SAME operation against both option sets and compares the outcome:
///         the produced JSON / parsed value, or the exception type when it fails. Exception
///         <i>messages</i> are deliberately not compared — System.Text.Json appends
///         <c>Path | LineNumber | BytePositionInLine</c> to its own exceptions, which no external
///         converter can reproduce. What must match is the observable contract: same wire format,
///         same accept/reject decision.
///     </para>
/// </summary>
public class FastEnumJsonConverterEquivalenceTests(FastEnumGeneratedAssemblyFixture fixture)
    : IClassFixture<FastEnumGeneratedAssemblyFixture>
{
    /// <summary>
    ///     Every case is exercised: the theory data is derived from the operation table itself,
    ///     so an operation cannot be added without also being run.
    /// </summary>
    public static TheoryData<string> Cases
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var id in Operations.Keys)
                data.Add(id);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void GeneratedConverter_MatchesJsonStringEnumConverter(string caseId)
    {
        var operation = Operations[caseId];

        var system = Outcome(() => operation(fixture, fixture.SystemOptions));
        var generated = Outcome(() => operation(fixture, fixture.GeneratedOptions));

        generated.Should().Be(system,
            $"the generated converter must behave like JsonStringEnumConverter for '{caseId}'");
    }

    /// <summary>
    ///     The host registers both: the generated converters first, then JsonStringEnumConverter as the
    ///     catch-all. This is what makes the wiring additive — a [FastEnum] enum gets the generated
    ///     converter, every other enum keeps the framework one, and no enum loses its string form.
    /// </summary>
    [Fact]
    public void RegisteredFirst_TheGeneratedConverterWins_AndEveryOtherEnumKeepsTheFrameworkOne()
    {
        var options = new JsonSerializerOptions();
        foreach (var converter in fixture.GeneratedOptions.Converters)
            options.Converters.Add(converter);
        options.Converters.Add(new JsonStringEnumConverter());

        options.GetConverter(fixture.OrderStatus).GetType().Name
            .Should().Be("OrderStatusJsonConverter");

        // DayOfWeek carries no [FastEnum]: it must still round-trip as a string, not as a number.
        JsonSerializer.Serialize(DayOfWeek.Monday, options).Should().Be("\"Monday\"");
    }

    private static readonly Dictionary<string, Func<FastEnumGeneratedAssemblyFixture, JsonSerializerOptions, string>>
        Operations = new()
        {
            // ---- write, declared members -------------------------------------------------
            ["write Pending"] = (f, o) => Ser(o, f.OrderStatus, Value(f.OrderStatus, 0)),
            ["write Confirmed"] = (f, o) => Ser(o, f.OrderStatus, Value(f.OrderStatus, 1)),
            ["write Cancelled"] = (f, o) => Ser(o, f.OrderStatus, Value(f.OrderStatus, 2)),

            // The camelCase naming policy applies to property NAMES, never to enum values.
            ["write { status: Confirmed }"] = (f, o) => Ser(o, f.Wrapper, Wrapper(f, 1)),

            // ---- read, names -------------------------------------------------------------
            ["read \"Pending\""] = (f, o) => De(o, f.OrderStatus, "\"Pending\""),
            ["read \"pending\""] = (f, o) => De(o, f.OrderStatus, "\"pending\""),
            ["read \"PENDING\""] = (f, o) => De(o, f.OrderStatus, "\"PENDING\""),
            ["read \"pEnDiNg\""] = (f, o) => De(o, f.OrderStatus, "\"pEnDiNg\""),

            // ---- read, integers (allowIntegerValues: true is the JsonStringEnumConverter default)
            ["read 0"] = (f, o) => De(o, f.OrderStatus, "0"),
            ["read 1"] = (f, o) => De(o, f.OrderStatus, "1"),
            ["read 2"] = (f, o) => De(o, f.OrderStatus, "2"),
            ["read 99 (undeclared)"] = (f, o) => De(o, f.OrderStatus, "99"),
            ["read -1"] = (f, o) => De(o, f.OrderStatus, "-1"),
            ["read 1.5 (fractional)"] = (f, o) => De(o, f.OrderStatus, "1.5"),
            ["read 2147483648 (overflow)"] = (f, o) => De(o, f.OrderStatus, "2147483648"),

            // ---- read, numeric strings ---------------------------------------------------
            ["read \"1\""] = (f, o) => De(o, f.OrderStatus, "\"1\""),
            ["read \"99\""] = (f, o) => De(o, f.OrderStatus, "\"99\""),
            ["read \"-1\""] = (f, o) => De(o, f.OrderStatus, "\"-1\""),
            ["read \" 1 \""] = (f, o) => De(o, f.OrderStatus, "\" 1 \""),

            // ---- undeclared values and wrong tokens --------------------------------------
            ["write (OrderStatus)99"] = (f, o) => Ser(o, f.OrderStatus, Value(f.OrderStatus, 99)),
            ["write (OrderStatus)(-1)"] = (f, o) => Ser(o, f.OrderStatus, Value(f.OrderStatus, -1)),
            ["read \"Bogus\""] = (f, o) => De(o, f.OrderStatus, "\"Bogus\""),
            ["read null"] = (f, o) => De(o, f.OrderStatus, "null"),
            ["read true"] = (f, o) => De(o, f.OrderStatus, "true"),
            ["read {}"] = (f, o) => De(o, f.OrderStatus, "{}"),
            ["read []"] = (f, o) => De(o, f.OrderStatus, "[]"),
            ["read \"\""] = (f, o) => De(o, f.OrderStatus, "\"\""),

            // ---- [Flags] -----------------------------------------------------------------
            ["flags write None"] = (f, o) => Ser(o, f.Perm, Value(f.Perm, 0)),
            ["flags write Read|Write"] = (f, o) => Ser(o, f.Perm, Value(f.Perm, 3)),
            ["flags write (Perm)8"] = (f, o) => Ser(o, f.Perm, Value(f.Perm, 8)),
            ["flags write Read|(Perm)8"] = (f, o) => Ser(o, f.Perm, Value(f.Perm, 9)),
            ["flags read \"Read, Write\""] = (f, o) => De(o, f.Perm, "\"Read, Write\""),
            ["flags read \"Read,Write\""] = (f, o) => De(o, f.Perm, "\"Read,Write\""),
            ["flags read \"read, write\""] = (f, o) => De(o, f.Perm, "\"read, write\""),
            ["flags read \"Read\""] = (f, o) => De(o, f.Perm, "\"Read\""),
            ["flags read \"None\""] = (f, o) => De(o, f.Perm, "\"None\""),
            ["flags read \"Read, 2\""] = (f, o) => De(o, f.Perm, "\"Read, 2\""),
            ["flags read \"Read, Bogus\""] = (f, o) => De(o, f.Perm, "\"Read, Bogus\""),
            ["flags read \"3\""] = (f, o) => De(o, f.Perm, "\"3\""),
            ["flags read 3"] = (f, o) => De(o, f.Perm, "3"),
            ["flags read 0"] = (f, o) => De(o, f.Perm, "0"),

            // Round-trip on the converter's OWN output — the flags read path must understand
            // what the flags write path produced.
            ["flags roundtrip Read|Write"] = (f, o) => De(o, f.Perm, Ser(o, f.Perm, Value(f.Perm, 3))),

            // ---- [Flags] with no zero-valued member --------------------------------------
            // "no flags set" has no name here, so it must go out as a number, not as a name.
            ["bits write (Bits)0"] = (f, o) => Ser(o, f.Bits, Value(f.Bits, 0)),
            ["bits write A|B"] = (f, o) => Ser(o, f.Bits, Value(f.Bits, 3)),
            ["bits read 0"] = (f, o) => De(o, f.Bits, "0"),
            ["bits read \"A, B\""] = (f, o) => De(o, f.Bits, "\"A, B\""),

            // ---- byte underlying type ----------------------------------------------------
            ["byte write High"] = (f, o) => Ser(o, f.Level, Value(f.Level, 1)),
            ["byte write (Level)200"] = (f, o) => Ser(o, f.Level, Value(f.Level, 200)),
            ["byte read 1"] = (f, o) => De(o, f.Level, "1"),
            ["byte read 300 (out of range)"] = (f, o) => De(o, f.Level, "300"),
            ["byte read -1 (out of range)"] = (f, o) => De(o, f.Level, "-1"),

            // ---- nullable ----------------------------------------------------------------
            ["nullable write Pending"] = (f, o) => Ser(o, Nullable(f.OrderStatus), Value(f.OrderStatus, 0)),
            ["nullable read null"] = (f, o) => De(o, Nullable(f.OrderStatus), "null"),

            // ---- dictionary keys (ReadAsPropertyName / WriteAsPropertyName) --------------
            ["dict write declared key"] = (f, o) => Ser(o, Dict(f.OrderStatus), Dict(f.OrderStatus, 0)),
            ["dict write undeclared key"] = (f, o) => Ser(o, Dict(f.OrderStatus), Dict(f.OrderStatus, 99)),
            ["dict read keys"] = (f, o) => DeDict(o, Dict(f.OrderStatus), """{"Pending":1,"confirmed":2}"""),
            ["dict read numeric key"] = (f, o) => DeDict(o, Dict(f.OrderStatus), """{"99":1}"""),
            ["dict read bogus key"] = (f, o) => DeDict(o, Dict(f.OrderStatus), """{"Bogus":1}"""),
            ["dict write flags key"] = (f, o) => Ser(o, Dict(f.Perm), Dict(f.Perm, 3)),
            ["dict read flags key"] = (f, o) => DeDict(o, Dict(f.Perm), """{"Read, Write":1}""")
        };

    /// <summary>
    ///     Runs an operation and reduces it to a comparable string: the result, or the exception
    ///     type name when it throws.
    /// </summary>
    private static string Outcome(Func<string> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception ex)
        {
            return $"<{ex.GetType().Name}>";
        }
    }

    private static string Ser(JsonSerializerOptions options, Type type, object? value)
        => JsonSerializer.Serialize(value, type, options);

    private static string De(JsonSerializerOptions options, Type type, string json)
        => JsonSerializer.Deserialize(json, type, options)?.ToString() ?? "<null>";

    private static string DeDict(JsonSerializerOptions options, Type type, string json)
    {
        var dictionary = (IDictionary)JsonSerializer.Deserialize(json, type, options)!;
        var entries = new List<string>();
        foreach (DictionaryEntry entry in dictionary)
            entries.Add($"{entry.Key}={entry.Value}");
        return string.Join(",", entries);
    }

    private static object Value(Type enumType, int value) => Enum.ToObject(enumType, value);

    private static Type Nullable(Type enumType) => typeof(Nullable<>).MakeGenericType(enumType);

    private static Type Dict(Type keyType) => typeof(Dictionary<,>).MakeGenericType(keyType, typeof(int));

    private static object Dict(Type keyType, int key)
    {
        var dictionary = (IDictionary)Activator.CreateInstance(Dict(keyType))!;
        dictionary.Add(Value(keyType, key), 1);
        return dictionary;
    }

    private static object Wrapper(FastEnumGeneratedAssemblyFixture fixture, int status)
    {
        var wrapper = Activator.CreateInstance(fixture.Wrapper)!;
        fixture.Wrapper.GetProperty("Status")!.SetValue(wrapper, Value(fixture.OrderStatus, status));
        return wrapper;
    }
}
