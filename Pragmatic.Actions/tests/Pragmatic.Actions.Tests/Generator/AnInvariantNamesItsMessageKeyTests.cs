using System;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     An <c>[Invariant]</c>'s refusal can be read in the caller's language, because the rule names the
///     key its message lives under.
/// </summary>
/// <remarks>
///     <para>
///         Every other message an application shows a user comes from a translation key — the
///         validation messages, the <c>Error</c> records, the document labels. Without a key named by
///         the rule, an <c>[Invariant]</c> sentence goes into <c>InvariantViolationError.Title</c>, and
///         the only key the resolver can derive is <c>error.invariant.violation</c> — one text for every
///         invariant in the application.
///     </para>
///     <para>
///         The choice would then be between losing <em>which</em> rule refused and sending an English
///         sentence to an Italian customer's client.
///     </para>
///     <para>
///         ⚠️ The key is an error key's <b>base</b>, as every other error's is: the localizer reads
///         <c>{key}.title</c> and <c>{key}.detail</c>. So it is a string, and not a constant of the
///         generated keys class the way a validation rule's <c>MessageKey</c> is — a base has no constant
///         to name, because in that class those two suffixes are the members and the base is the type
///         holding them.
///     </para>
/// </remarks>
public class AnInvariantNamesItsMessageKeyTests
{
    private const string EfCorePresence = """
        namespace Pragmatic.Persistence.EFCore
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class PragmaticDbContextAttribute : Attribute { }
        }
        """;

    /// <param name="orderRule">What the aggregate declares about itself.</param>
    /// <param name="lineRule">What the child declares — the same question one floor down.</param>
    private static string Source(string orderRule, string lineRule = "") => $$"""
        using System;
        using System.Collections.Generic;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;

        {{EfCorePresence}}

        namespace TestApp;

        public class SalesBoundary;

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order : IEntity
        {
            public string Reference { get; private set; } = "";
            public ICollection<LineItem> Lines { get; set; } = new List<LineItem>();
            {{orderRule}}
        }

        [Entity]
        [BelongsTo<SalesBoundary>]
        [PartOf<Order>]
        public partial class LineItem : IEntity
        {
            public decimal VatRate { get; set; }
            {{lineRule}}
        }

        [Mutation(Mode = MutationMode.Update, Internal = true)]
        public partial class WriteLineItemMutation : Mutation<LineItem>
        {
            public Guid Id { get; init; }
            public decimal VatRate { get; init; }
        }

        [Mutation(Mode = MutationMode.Update)]
        public partial class UpdateOrderMutation : Mutation<Order>
        {
            public Guid Id { get; init; }
            public string? Reference { get; init; }
            public List<WriteLineItemMutation> Lines { get; init; } = new();
        }
        """;

    private const string HasLines = """
        [Invariant("An order has at least one line", MessageKey = "validation.order.has_lines")]
        public bool HasLines() => Lines.Count > 0;
        """;

    private const string HasLinesWithNoKey = """
        [Invariant("An order has at least one line")]
        public bool HasLines() => Lines.Count > 0;
        """;

    private const string ChargesARateInUse = """
        [Invariant("The VAT rate must be one of 0, 4, 5, 10 or 22 per cent",
            MessageKey = "validation.line_item.unknown_vat_rate")]
        public bool ChargesARateInUse() => VatRate is 0m or 4m or 5m or 10m or 22m;
        """;

    [Fact]
    public void AKeyWrittenAsALiteral_ReachesTheRefusal()
        => Invoker(Source(HasLines)).Should().Contain("\"validation.order.has_lines\"",
            "the refusal carries the key beside the rule's name, and the resolver reads it like any other");

    /// <summary>The message stays: a host with no translation for the key answers the sentence.</summary>
    [Fact]
    public void TheMessage_IsStillCarried()
        => Invoker(Source(HasLines)).Should().Contain("An order has at least one line");

    /// <summary>The same question one floor down — a <c>[PartOf]</c> child's rule.</summary>
    [Fact]
    public void AChildsRule_CarriesItsOwnKey()
        => Invoker(Source(HasLinesWithNoKey, ChargesARateInUse)).Should()
            .Contain("\"validation.line_item.unknown_vat_rate\"");

    /// <summary>
    ///     The control, and what keeps this from becoming "every invariant gets a key": a rule that names
    ///     none is refused exactly as before, with its sentence and no key.
    /// </summary>
    [Fact]
    public void ARuleThatNamesNoKey_IsUnchanged()
    {
        var invoker = Invoker(Source(HasLinesWithNoKey));

        invoker.Should().Contain("An order has at least one line");
        invoker.Should().NotContain("validation.order.has_lines");
    }

    /// <summary>And the generated invoker compiles.</summary>
    [Fact]
    public void TheInvoker_Compiles()
    {
        var errors = Run(Source(HasLines, ChargesARateInUse)).OutputCompilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
                        && (d.Location.SourceTree?.FilePath ?? "").Contains("UpdateOrderMutation", StringComparison.Ordinal))
            .Select(d => d.ToString())
            .ToArray();

        errors.Should().BeEmpty(string.Join(" | ", errors));
    }

    private static string Invoker(string source) => InvokerOf(Run(source));

    private static string InvokerOf(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrderMutation.MutationInvoker")
           ?? throw new InvalidOperationException("no invoker was generated");

    private static SourceGenRunResult Run(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, References);

    private static readonly Microsoft.CodeAnalysis.MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<Pragmatic.Actions.Attributes.DomainActionAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Result.IError>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Result<,>)),
        GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Ensure.Ensure)),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.IEntity>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.SoftDeleteAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Mapping.Attributes.MapToAttribute<object>>(),
        GeneratorTestHelper.FromType<Pragmatic.Specification.Specification<object>>(),
        GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
        GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.IServiceCollection>(),
        GeneratorTestHelper.FromTypeAssembly(
            typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)),
        GeneratorTestHelper.FromType<Microsoft.Extensions.Logging.ILogger>()
    ];
}
