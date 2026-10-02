using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Adapters;
using Pragmatic.Persistence.Query.Attributes;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     Tests for the [GenerateGridBridge] source generation pipeline.
///     Verifies that entity classes produce a canonical GridFilterRequest → IQueryable bridge.
/// </summary>
public class GridBridgeGeneratorTests
{
    [Fact]
    public void SimpleEntity_GeneratesBridgeClass()
    {
        var source = """
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            [GenerateGridBridge]
            public class Order
            {
                public System.Guid Id { get; set; }
                [Filterable]
                public string Name { get; set; } = "";
                [Filterable]
                public decimal Amount { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "GridBridge");
        generated.Should().NotBeNull("GridBridge class should be generated");
        generated.Should().Contain("class OrderGridFilterBridge");
        generated.Should().Contain("static");
        generated.Should().Contain("ApplyCanonical");
    }

    [Fact]
    public void StringProperty_GeneratesStringFilterOperators()
    {
        var source = """
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            [GenerateGridBridge]
            public class Order
            {
                public System.Guid Id { get; set; }
                [Filterable]
                public string OrderNumber { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "GridBridge");
        generated.Should().NotBeNull();

        // String filter helper should support Contains, StartsWith, EndsWith, Equals, NotEquals
        generated.Should().Contain("OrderNumberPredicate");
        generated.Should().Contain("FilterOperator.Contains");
        generated.Should().Contain("FilterOperator.StartsWith");
        generated.Should().Contain("FilterOperator.EndsWith");
        generated.Should().Contain("FilterOperator.Equals");
        generated.Should().Contain("FilterOperator.NotEquals");
        generated.Should().Contain(".Contains(");
        generated.Should().Contain(".StartsWith(");
        generated.Should().Contain(".EndsWith(");
    }

    [Fact]
    public void ComparableProperty_GeneratesComparisonOperators()
    {
        var source = """
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            [GenerateGridBridge]
            public class Order
            {
                public System.Guid Id { get; set; }
                [Filterable]
                public decimal Amount { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "GridBridge");
        generated.Should().NotBeNull();

        // Comparable filter helper should support all comparison operators
        generated.Should().Contain("AmountPredicate");
        generated.Should().Contain("FilterOperator.GreaterThan");
        generated.Should().Contain("FilterOperator.GreaterOrEqual");
        generated.Should().Contain("FilterOperator.LessThan");
        generated.Should().Contain("FilterOperator.LessOrEqual");
    }

    [Fact]
    public void BoolProperty_GeneratesEqualityOnlyOperators()
    {
        var source = """
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            [GenerateGridBridge]
            public class Order
            {
                public System.Guid Id { get; set; }
                [Filterable]
                public bool IsActive { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "GridBridge");
        generated.Should().NotBeNull();

        // Bool filter should only support Equals/NotEquals
        generated.Should().Contain("IsActivePredicate");
        generated.Should().Contain("FilterOperator.Equals");
        generated.Should().Contain("FilterOperator.NotEquals");
    }

    [Fact]
    public void EnumProperty_GeneratesEqualityOnlyOperators()
    {
        var source = """
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            public enum OrderStatus { Draft, Confirmed, Cancelled }

            [GenerateGridBridge]
            public class Order
            {
                public System.Guid Id { get; set; }
                [Filterable]
                public OrderStatus Status { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "GridBridge");
        generated.Should().NotBeNull();

        generated.Should().Contain("StatusPredicate");
    }

    [Fact]
    public void MultiSort_GeneratesOrderedQueryable()
    {
        var source = """
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            [GenerateGridBridge]
            public class Order
            {
                public System.Guid Id { get; set; }
                [Filterable]
                public string Name { get; set; } = "";
                [Filterable]
                public decimal Amount { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "GridBridge");
        generated.Should().NotBeNull();

        // Multi-sort support
        generated.Should().Contain("IOrderedQueryable");
        generated.Should().Contain("OrderBy(");
        generated.Should().Contain("OrderByDescending(");
        generated.Should().Contain("ThenBy(");
        generated.Should().Contain("ThenByDescending(");
        generated.Should().Contain("SortDirection.Ascending");
    }

    [Fact]
    public void Paging_GeneratesSkipTake()
    {
        var source = """
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            [GenerateGridBridge]
            public class Order
            {
                public System.Guid Id { get; set; }
                [Filterable]
                public string Name { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "GridBridge");
        generated.Should().NotBeNull();

        // Paging
        generated.Should().Contain("request.Page");
        generated.Should().Contain("request.PageSize");
        generated.Should().Contain("Skip(");
        generated.Should().Contain("Take(");
    }

    [Fact]
    public void ExcludesInfrastructureProperties()
    {
        var source = """
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            [GenerateGridBridge]
            public class Order
            {
                public System.Guid Id { get; set; }
                [Filterable]
                public string Name { get; set; } = "";

                // Infrastructure — should be excluded
                [Filterable]
                public long PersistenceId { get; set; }
                [Filterable]
                public bool IsDeleted { get; set; }
                [Filterable]
                public System.DateTimeOffset? DeletedAt { get; set; }
                [Filterable]
                public string? DeletedBy { get; set; }
                [Filterable]
                public System.DateTimeOffset CreatedAt { get; set; }
                [Filterable]
                public string? CreatedBy { get; set; }
                [Filterable]
                public uint RowVersion { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "GridBridge");
        generated.Should().NotBeNull();

        // Should include business properties
        generated.Should().Contain("NamePredicate");

        // Should exclude infrastructure
        generated.Should().NotContain("PersistenceIdPredicate");
        generated.Should().NotContain("IsDeletedPredicate");
        generated.Should().NotContain("DeletedAtPredicate");
        generated.Should().NotContain("CreatedAtPredicate");
        generated.Should().NotContain("RowVersionPredicate");
    }

    [Fact]
    public void ExcludesCredentialAndAuthzProperties()
    {
        // The canonical bridge derives its field list from the scalar properties; credential columns
        // (PasswordHash/SecurityStamp) and the authorization shape (OwnerId/TenantId) must stay out,
        // or client-driven filter/sort becomes a boolean-oracle surface.
        var source = """
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            [GenerateGridBridge]
            public class Account
            {
                public System.Guid Id { get; set; }
                [Filterable]
                public string Name { get; set; } = "";

                // Sensitive — must never reach a client-driven grid
                [Filterable]
                public string PasswordHash { get; set; } = "";
                [Filterable]
                public string SecurityStamp { get; set; } = "";
                [Filterable]
                public System.Guid OwnerId { get; set; }
                [Filterable]
                public System.Guid TenantId { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "GridBridge");
        generated.Should().NotBeNull();

        // Business property still exposed
        generated.Should().Contain("NamePredicate");

        // Credentials and authorization columns withheld
        generated.Should().NotContain("PasswordHashPredicate");
        generated.Should().NotContain("SecurityStampPredicate");
        generated.Should().NotContain("OwnerIdPredicate");
        generated.Should().NotContain("TenantIdPredicate");
    }

    [Fact]
    public void ExcludesNavigationProperties()
    {
        var source = """
            using Pragmatic.Persistence.Query.Attributes;
            using System.Collections.Generic;

            namespace TestApp;

            [GenerateGridBridge]
            public class Order
            {
                public System.Guid Id { get; set; }
                [Filterable]
                public string Name { get; set; } = "";
                [Filterable]
                public System.Guid CustomerId { get; set; }
                [Filterable]
                public Customer Customer { get; set; }
                [Filterable]
                public ICollection<OrderLine> Lines { get; set; }
            }

            public class Customer { public System.Guid Id { get; set; } }
            public class OrderLine { public System.Guid Id { get; set; } }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "GridBridge");
        generated.Should().NotBeNull();

        // FK scalar included
        generated.Should().Contain("CustomerIdPredicate");

        // Reference nav excluded
        generated.Should().NotContain("CustomerPredicate");

        // Collection nav excluded
        generated.Should().NotContain("LinesPredicate");
    }

    [Fact]
    public void DateTimeProperty_GeneratesComparableFilter()
    {
        var source = """
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            [GenerateGridBridge]
            public class Event
            {
                public System.Guid Id { get; set; }
                [Filterable]
                public string Name { get; set; } = "";
                [Filterable]
                public System.DateTimeOffset StartDate { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "GridBridge");
        generated.Should().NotBeNull();

        generated.Should().Contain("StartDatePredicate");
        generated.Should().Contain("FilterOperator.GreaterThan");
        generated.Should().Contain("FilterOperator.LessThan");
    }

    [Fact]
    public void GeneratesCorrectHeader()
    {
        var source = """
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            [GenerateGridBridge]
            public class Order
            {
                public System.Guid Id { get; set; }
                [Filterable]
                public string Name { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "GridBridge");
        generated.Should().NotBeNull();
        generated.Should().Contain("// Pragmatic.SourceGenerator/Persistence");
        generated.Should().Contain("[GenerateGridBridge] on Order");
    }


    /// <summary>⚠️ The clause connector is read, so two clauses can be alternatives.</summary>
    /// <remarks>
    ///     <para>
    ///         Walking the clauses applying <c>query = ApplyXFilter(query, filter)</c> would compose in
    ///         AND whatever the clause says — so "the word is in the term <em>or</em> in its definition",
    ///         the first thing any grid's search box asks for, would come back as the intersection: no
    ///         error, a shorter list, and a reader who concludes the data is not there.
    ///     </para>
    ///     <para>
    ///         A <c>Where</c> already applied cannot be OR-ed with the next, which is why the bridge
    ///         builds predicates and joins them before a single <c>Where</c>.
    ///     </para>
    /// </remarks>
    [Fact]
    public void ClauseLogic_IsHonouredWhenJoiningPredicates()
    {
        var source = """
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            [GenerateGridBridge]
            public class Order
            {
                public System.Guid Id { get; set; }
                [Filterable]
                public string Name { get; set; } = "";
                [Filterable]
                public string Notes { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);
        var generated = GeneratorTestHelper.GetGeneratedSource(result, "GridBridge")!;

        generated.Should().Contain("GridPredicate.Or",
            "a clause declaring Or has to reach an OR");
        generated.Should().Contain("GridPredicate.And");
        generated.Should().Contain("pending = filter.Logic",
            "Logic describes the connector to the NEXT clause, so it is carried forward");

        // Not the applied-one-after-another shape, which composes in AND whatever the clause says.
        generated.Should().NotContain("query = NamePredicate(",
            "predicates are joined, not applied one after another");
    }

    /// <summary>⚠️ A withheld field is refused by name, not by silence.</summary>
    /// <remarks>
    ///     A denylist working purely by omission is indistinguishable from a typo: both fall through
    ///     the switch and leave the query unchanged, so a client cannot tell "you may not filter on
    ///     that" from "you spelled it wrong", and neither can a log.
    /// </remarks>
    [Fact]
    public void WithheldAndUnknownFields_AreRefusedSeparately()
    {
        var source = """
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            [GenerateGridBridge]
            public class Account
            {
                public System.Guid Id { get; set; }
                [Filterable]
                public string Name { get; set; } = "";
                [Filterable]
                public string PasswordHash { get; set; } = "";
                [Filterable]
                public System.Guid TenantId { get; set; }
            }
            """;

        var result = RunGenerator(source);
        var generated = GeneratorTestHelper.GetGeneratedSource(result, "GridBridge")!;

        generated.Should().Contain("GridFieldRejection.Withheld",
            "a field the entity has and the bridge withholds is answered as such");
        generated.Should().Contain("GridFieldRejection.Unknown",
            "and a field it does not have is answered differently");

        // ⚠️ The security property, unchanged: naming the column in a refusal is not the same as
        // filtering on it. There must still be no predicate that touches it.
        generated.Should().NotContain("PasswordHashPredicate",
            "a credential column gets no filter, only a refusal");
        generated.Should().NotContain("TenantIdPredicate");
    }

    /// <summary>The generated bridge compiles.</summary>
    /// <remarks>
    ///     Every other test in this file asserts on the <em>text</em> the generator produces. Text that
    ///     never went through a compiler is a claim about a string: a generator emitting code that does
    ///     not build could pass all of them.
    /// </remarks>
    [Fact]
    public void TheGeneratedBridge_Compiles()
    {
        var source = """
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            public enum Status { Draft, Sent }

            [GenerateGridBridge]
            public class Order
            {
                public System.Guid Id { get; set; }
                [Filterable]
                public string Name { get; set; } = "";
                [Filterable]
                public decimal Amount { get; set; }
                [Filterable]
                public bool IsActive { get; set; }
                [Filterable]
                public Status Status { get; set; }
                [Filterable]
                public System.DateTimeOffset PlacedAt { get; set; }
                [Filterable]
                public string TenantId { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);

        // The errors are in the message on purpose: "it does not compile" without saying why sends the
        // reader back to the generator with nothing to go on.
        var errors = string.Join("; ", GeneratorTestHelper.GetCompilationErrors(result)
            .Select(d => d.GetMessage()).Take(5));

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse(
            "the bridge covers a string, a comparable, a bool, an enum and a date, and all of them "
            + $"have to produce code that builds — {errors}");
    }

    /// <summary>
    ///     The bridge names what is declared, and nothing else.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Naming every scalar minus a fixed list of sensitive names would be a denylist, which
    ///         covers what somebody remembered to write in it and exposes the rest. The client chooses
    ///         the field name, and sorting or filtering on a column makes it talk without reading it:
    ///         <c>sortField=salary</c> gives the ordering, <c>equals X</c> answers whether the value is
    ///         X — and a column like <c>SourceRef</c> would be nameable with no way to take it out.
    ///     </para>
    ///     <para>
    ///         So the surface is declared: <c>[Filterable]</c> on the property says "the grid may name
    ///         this one". The sensitive denylist stays behind it as a backstop, which is what
    ///         <see cref="DeclaredButSensitive_IsStillWithheld" /> holds.
    ///     </para>
    /// </remarks>
    [Fact]
    public void PropertyWithNoDeclaration_IsAbsentFromTheBridge()
    {
        var source = """
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            [GenerateGridBridge]
            public class KnowledgeItem
            {
                public System.Guid Id { get; set; }

                [Filterable]
                public string Term { get; set; } = "";

                // Declares nothing: the grid must not be able to name it.
                public string SourceRef { get; set; } = "";
            }
            """;

        var generated = GeneratorTestHelper.GetGeneratedSource(RunGenerator(source), "GridBridge");

        generated.Should().NotBeNull("the entity carries [GenerateGridBridge]");
        generated!.Should().Contain("TermPredicate",
            "the declared one is there — otherwise this passes on a bridge that names nothing");
        generated.Should().NotContain("SourceRefPredicate");
    }

    /// <summary>
    ///     <c>[GridExclude]</c> takes out a property that is otherwise declared.
    /// </summary>
    /// <remarks>
    ///     Rendered twice, with and without the attribute, because the two halves are what make it a
    ///     proof: without the pair, "absent" would also hold on a bridge that never read
    ///     <c>[Filterable]</c> either, which is exactly the state this defect was in.
    /// </remarks>
    [Fact]
    public void GridExclude_RemovesAPropertyThatWouldOtherwiseBeThere()
    {
        const string entity = """
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            {0}
            [GenerateGridBridge]
            public class KnowledgeItem
            {{
                public System.Guid Id {{ get; set; }}

                [Filterable]
                public string Term {{ get; set; }} = "";

                [Filterable]
                public string SourceRef {{ get; set; }} = "";
            }}
            """;

        var without = GeneratorTestHelper.GetGeneratedSource(
            RunGenerator(string.Format(entity, "")), "GridBridge");
        var with = GeneratorTestHelper.GetGeneratedSource(
            RunGenerator(string.Format(entity, "[GridExclude(\"SourceRef\")]")), "GridBridge");

        without.Should().NotBeNull();
        with.Should().NotBeNull();

        without!.Should().Contain("SourceRefPredicate",
            "declared and not excluded, it is in the bridge — this is the half that makes the next "
            + "assertion mean something");
        with!.Should().NotContain("SourceRefPredicate");
        with.Should().Contain("TermPredicate", "and the exclusion takes out one property, not the lot");
    }

    /// <summary>
    ///     A declared property that is sensitive is still withheld: the denylist is a backstop, not the
    ///     policy.
    /// </summary>
    /// <remarks>
    ///     Both guards, in this order. Declaring <c>[Filterable]</c> on <c>TenantId</c> is a mistake an
    ///     author can make, and the framework's answer must not depend on their having read the
    ///     documentation about which names are reserved.
    /// </remarks>
    [Fact]
    public void DeclaredButSensitive_IsStillWithheld()
    {
        var source = """
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            [GenerateGridBridge]
            public class Account
            {
                public System.Guid Id { get; set; }

                [Filterable]
                public string Name { get; set; } = "";

                [Filterable]
                public string PasswordHash { get; set; } = "";

                [Filterable]
                public System.Guid TenantId { get; set; }
            }
            """;

        var generated = GeneratorTestHelper.GetGeneratedSource(RunGenerator(source), "GridBridge");

        generated.Should().NotBeNull();
        generated!.Should().Contain("NamePredicate");
        generated.Should().NotContain("PasswordHashPredicate",
            "declaring it does not make a credential column filterable");
        generated.Should().NotContain("TenantIdPredicate");
        generated.Should().Contain("GridFieldRejection.Withheld",
            "and it is refused by name, not by silence");
    }

    /// <summary>
    ///     The generator end of the <b>same</b> list the runtime adapters read.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The two guards were two hand-maintained copies of one list, each with a comment saying
    ///         so. This case names every entry and asserts the bridge withholds it; the runtime end is
    ///         <c>OneDenylistTests</c>, which sends the same names through a PrimeNG request. Neither
    ///         could be written as a statement about <em>the</em> list while there were two of them: it
    ///         would have been two tests that happen to agree, which is what the copies were.
    ///     </para>
    ///     <para>
    ///         The names live in <c>Pragmatic.Contracts.SensitiveGridFieldNames</c>, linked as source
    ///         into the generator and into <c>Pragmatic.Persistence</c>. Spelled out here rather than
    ///         read from it: a test that asks the list what is in the list passes whatever the list
    ///         says, including after someone empties it.
    ///     </para>
    /// </remarks>
    [Theory]
    [InlineData("Password")]
    [InlineData("PasswordHash")]
    [InlineData("PasswordSalt")]
    [InlineData("SecurityStamp")]
    [InlineData("ResetToken")]
    [InlineData("ResetTokenHash")]
    [InlineData("RefreshToken")]
    [InlineData("RefreshTokenHash")]
    [InlineData("ApiKey")]
    [InlineData("ApiKeyHash")]
    [InlineData("Secret")]
    [InlineData("SecretHash")]
    [InlineData("PrivateKey")]
    [InlineData("OwnerId")]
    [InlineData("TenantId")]
    [InlineData("AccessScopes")]
    [InlineData("PersistenceId")]
    [InlineData("RowVersion")]
    public void TheSameSensitiveNames_AreWithheldFromTheBridge(string column)
    {
        var source = $$"""
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            [GenerateGridBridge]
            public class Account
            {
                public System.Guid Id { get; set; }

                [Filterable]
                public string Name { get; set; } = "";

                [Filterable]
                public string {{column}} { get; set; } = "";
            }
            """;

        var generated = GeneratorTestHelper.GetGeneratedSource(RunGenerator(source), "GridBridge");

        generated.Should().NotBeNull();
        generated!.Should().Contain("NamePredicate", "the ordinary column is still there");
        generated.Should().NotContain($"{column}Predicate",
            "the generator and the runtime read one list, so what one withholds the other withholds");
    }

    private static SourceGenRunResult RunGenerator(string source)
    {
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source,
            GetReferences());
    }

    private static MetadataReference[] GetReferences()
    {
        return
        [
            GeneratorTestHelper.FromType<GenerateGridBridgeAttribute>(),
            GeneratorTestHelper.FromType<PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromType<FilterOperator>(),
            GeneratorTestHelper.FromType<SortDirection>(),
            GeneratorTestHelper.FromType<GridFilterRequest>(),
            GeneratorTestHelper.FromType<FilterClause>(),

            // ⚠️ Queryable and Expression were missing, and nothing noticed: every test here compared
            // the generated text and none of them compiled it, so a bridge referring to ThenBy and to
            // Expression<Func<,>> passed while being unbuildable in this compilation.
            GeneratorTestHelper.FromTypeAssembly(typeof(System.Linq.Queryable)),
            GeneratorTestHelper.FromTypeAssembly(typeof(System.Linq.Expressions.Expression)),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Persistence.Query.Adapters.GridFieldRejectedException)),
        ];
    }
}
