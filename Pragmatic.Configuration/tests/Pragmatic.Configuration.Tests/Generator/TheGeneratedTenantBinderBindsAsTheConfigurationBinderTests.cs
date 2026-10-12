using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Configuration.Tests.Generator;

/// <summary>
///     The binder the generator emits sets each property to what <c>ConfigurationBinder.Bind</c> sets it to, and
///     refuses what it refuses with the same message (#139).
/// </summary>
/// <remarks>
///     <c>TenantOptions&lt;T&gt;</c> uses the generated binder in place of the reflective one when it is registered,
///     so any difference here is a tenant reading another value than it did before. Each case binds one raw value
///     through both, on a fresh instance, and compares the property (or the exception).
/// </remarks>
public class TheGeneratedTenantBinderBindsAsTheConfigurationBinderTests : ConfigurationGeneratorTestBase
{
    private const string Source = """
        using System;
        using Pragmatic.Configuration;

        namespace TestApp;

        public enum Mode { First, Second }

        [Flags]
        public enum Channels { None = 0, Mail = 1, Sms = 2 }

        [Configuration]
        public partial class ProbeOptions
        {
            public string Text { get; set; } = "default";
            public bool Flag { get; set; }
            public byte Byte { get; set; }
            public sbyte SByte { get; set; }
            public short Short { get; set; }
            public ushort UShort { get; set; }
            public int Int { get; set; } = 3;
            public uint UInt { get; set; }
            public long Long { get; set; }
            public ulong ULong { get; set; }
            public float Float { get; set; }
            public double Double { get; set; }
            public decimal Decimal { get; set; }
            public Guid Guid { get; set; }
            public TimeSpan Span { get; set; }
            public DateTime Date { get; set; }
            public DateTimeOffset Offset { get; set; }
            public Mode Mode { get; set; }
            public Channels Channels { get; set; }
            public int? NullableInt { get; set; } = 7;
            public Mode? NullableMode { get; set; } = Mode.Second;
            [Microsoft.Extensions.Configuration.ConfigurationKeyName("renamed")]
            public string Named { get; set; } = "";
        }
        """;

    private static readonly Lazy<(Type Options, Type Binder)> Generated = new(Compile);

    public static TheoryData<string, string, string> Cases => new()
    {
        { "Text", "Text", "" }, { "Text", "Text", "x" }, { "Text", "Text", " padded " },
        { "Flag", "Flag", "true" }, { "Flag", "Flag", "False" }, { "Flag", "Flag", " true " }, { "Flag", "Flag", "1" }, { "Flag", "Flag", "" },
        { "Byte", "Byte", "255" }, { "Byte", "Byte", "256" }, { "Byte", "Byte", "0xFF" }, { "Byte", "Byte", "-1" },
        { "SByte", "SByte", "-128" }, { "SByte", "SByte", "0xFF" }, { "SByte", "SByte", "#80" },
        { "Short", "Short", "-32768" }, { "Short", "Short", "&H7FFF" }, { "Short", "Short", "40000" },
        { "UShort", "UShort", "65535" }, { "UShort", "UShort", "-1" },
        { "Int", "Int", "42" }, { "Int", "Int", "-7" }, { "Int", "Int", " 42 " }, { "Int", "Int", "+5" },
        { "Int", "Int", "0x1F" }, { "Int", "Int", "0X1f" }, { "Int", "Int", "#ff" }, { "Int", "Int", "&h10" },
        { "Int", "Int", "0xFFFFFFFF" }, { "Int", "Int", "1.5" }, { "Int", "Int", "" }, { "Int", "Int", "  " },
        { "Int", "Int", "99999999999" }, { "Int", "Int", "1,000" }, { "Int", "Int", "0x" }, { "Int", "Int", "0xZZ" },
        { "UInt", "UInt", "0xFFFFFFFF" }, { "UInt", "UInt", "-1" },
        { "Long", "Long", "9223372036854775807" }, { "Long", "Long", "#7fffffffffffffff" },
        { "ULong", "ULong", "0xFFFFFFFFFFFFFFFF" }, { "ULong", "ULong", "18446744073709551616" },
        { "Float", "Float", "1.5" }, { "Float", "Float", "1e3" }, { "Float", "Float", "NaN" }, { "Float", "Float", "1,5" },
        { "Float", "Float", "" }, { "Float", "Float", " -0.25 " },
        { "Double", "Double", "-0.25" }, { "Double", "Double", "Infinity" }, { "Double", "Double", "1e400" }, { "Double", "Double", "abc" },
        { "Decimal", "Decimal", "1.10" }, { "Decimal", "Decimal", "1e2" }, { "Decimal", "Decimal", "" }, { "Decimal", "Decimal", "1,000.5" },
        { "Guid", "Guid", "6f9619ff-8b86-d011-b42d-00cf4fc964ff" }, { "Guid", "Guid", "{6f9619ff-8b86-d011-b42d-00cf4fc964ff}" },
        { "Guid", "Guid", "nope" }, { "Guid", "Guid", "" },
        { "Span", "Span", "00:01:30" }, { "Span", "Span", "1.02:03:04" }, { "Span", "Span", "90" }, { "Span", "Span", "x" }, { "Span", "Span", "" },
        { "Date", "Date", "2026-10-12" }, { "Date", "Date", "2026-10-12T10:00:00Z" }, { "Date", "Date", "10/12/2026" },
        { "Date", "Date", "" }, { "Date", "Date", "nope" },
        { "Offset", "Offset", "2026-10-12T10:00:00+02:00" }, { "Offset", "Offset", "" }, { "Offset", "Offset", "nope" },
        { "Mode", "Mode", "Second" }, { "Mode", "Mode", "second" }, { "Mode", "Mode", "1" }, { "Mode", "Mode", "42" },
        { "Mode", "Mode", " Second " }, { "Mode", "Mode", "Third" }, { "Mode", "Mode", "" },
        { "Channels", "Channels", "Mail, Sms" }, { "Channels", "Channels", "mail,sms" }, { "Channels", "Channels", "3" }, { "Channels", "Channels", "Mail, Fax" },
        { "NullableInt", "NullableInt", "" }, { "NullableInt", "NullableInt", "5" }, { "NullableInt", "NullableInt", "x" }, { "NullableInt", "NullableInt", " " },
        { "NullableMode", "NullableMode", "" }, { "NullableMode", "NullableMode", "first" },
        { "Named", "renamed", "v" }, { "Named", "RENAMED", "v" }, { "Named", "Named", "v" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Bind_OneValue_MatchesConfigurationBinder(string property, string key, string raw)
    {
        var (options, binder) = Generated.Value;
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { [key] = raw };

        var expected = Outcome(options, property, target =>
            new ConfigurationBuilder().AddInMemoryCollection(values).Build().Bind(target));
        var actual = Outcome(options, property, target =>
            Invoke(binder, target, name => values.TryGetValue(name, out var value) ? value : null));

        actual.Should().Be(expected);
    }

    [Fact]
    public void Bind_NoValues_LeavesEveryDefault()
    {
        var (options, binder) = Generated.Value;
        var target = Activator.CreateInstance(options)!;

        Invoke(binder, target, _ => null);

        Describe(options.GetProperty("Text")!.GetValue(target)).Should().Be("default");
        Describe(options.GetProperty("Int")!.GetValue(target)).Should().Be("3");
        Describe(options.GetProperty("NullableInt")!.GetValue(target)).Should().Be("7");
    }

    private static string Outcome(Type options, string property, Action<object> bind)
    {
        var target = Activator.CreateInstance(options)!;
        try
        {
            bind(target);
        }
        catch (InvalidOperationException e)
        {
            return $"throws {e.Message}";
        }

        return Describe(options.GetProperty(property)!.GetValue(target));
    }

    private static void Invoke(Type binder, object target, Func<string, string?> value)
    {
        try
        {
            binder.GetMethod("Bind")!.Invoke(Activator.CreateInstance(binder), [target, value]);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(e.InnerException);
        }
    }

    private static string Describe(object? value) => value switch
    {
        null => "null",
        DateTime date => $"{date:o} {date.Kind}",
        DateTimeOffset offset => offset.ToString("o", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static (Type, Type) Compile()
    {
        var result = RunGeneratorToCompile(Source);
        GetCompilationErrors(result).Should().BeEmpty("generated code must compile cleanly");

        using var stream = new MemoryStream();
        var emit = result.OutputCompilation.Emit(stream);
        emit.Success.Should().BeTrue("emit must succeed: " + string.Join("; ", emit.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.GetMessage())));

        var assembly = Assembly.Load(stream.ToArray());
        return (assembly.GetType("TestApp.ProbeOptions")!, assembly.GetType("TestApp.ProbeOptionsTenantBinder")!);
    }
}
