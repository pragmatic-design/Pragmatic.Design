using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Analyzers.Tests;

/// <summary>
///     PRAG0717–PRAG0721 — the declared visibility rules, and lifting a filter.
/// </summary>
/// <remarks>
///     The one that matters is PRAG0719: lifting a query filter without declaring the permission
///     that allows it. It sits on <c>[WithoutFilter&lt;T&gt;]</c>, which already existed and already
///     lifted filters — declaring a second attribute for the same job would have been a second
///     mechanism for a solved problem. The other two keep the declaration honest: a rule that filters
///     another entity, and a rule nothing can construct.
/// </remarks>
public class VisibilityRuleAnalyzerTests
{
    private const string Shims = """
        namespace System.Linq.Expressions { }
        namespace Pragmatic.Persistence.Query.Filters
        {
            public abstract class VisibilityRule<T> where T : class { }
        }
        namespace Pragmatic.Persistence.Entity
        {
            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class VisibleWhenAttribute<TRule> : System.Attribute where TRule : class { }
        }
        namespace Pragmatic.Persistence.Query.Filters
        {
            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class WithoutFilterAttribute<T> : System.Attribute where T : class { }
        }
        namespace Pragmatic.Authorization
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class RequirePermissionAttribute : System.Attribute
            {
                public RequirePermissionAttribute(string permission) { }
            }
        }
        namespace Pragmatic.Persistence.Query.Filters
        {
            public interface IQueryFilter { }

            public interface IQueryFilterToggle
            {
                System.IDisposable Disable<TFilter>();
                System.IDisposable Disable(System.Type filterType);
                System.IDisposable DisableVisibilityRule<TRule>() where TRule : class;
            }
        }
        namespace Pragmatic.Actions.Mutation
        {
            public abstract class Mutation<TEntity> { }
        }
        namespace App
        {
            using Pragmatic.Persistence.Query.Filters;

            public class Item { }
            public class Other { }

            public sealed class ConfirmedOnly : VisibilityRule<Item> { }
            public sealed class ForAnother : VisibilityRule<Other> { }
            public abstract class CannotBuild : VisibilityRule<Item> { }
            public sealed class NeedsAClock : VisibilityRule<Item>
            {
                public NeedsAClock(string now) { }
            }
            public sealed class PlainFilter : IQueryFilter { }

            [Pragmatic.Persistence.Entity.VisibleWhen<ActiveOnly>]
            public class Property { public bool IsActive { get; set; } public string Name { get; set; } }

            public sealed class ActiveOnly : VisibilityRule<Property>
            {
                public override System.Linq.Expressions.Expression<System.Func<Property, bool>>
                    ToExpression() => p => p.IsActive;
            }
        }
        """;

    private static async Task<string[]> RunAsync(string appSource, string id)
    {
        var tree = CSharpSyntaxTree.ParseText(
            Shims + "\n" + appSource, new CSharpParseOptions(LanguageVersion.Latest));
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll"))
        };

        var compilation = CSharpCompilation.Create(
            "VisibilityRuleTest", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new VisibilityRuleAnalyzer()));

        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();

        return diagnostics.Where(d => d.Id == id).Select(d => d.GetMessage()).ToArray();
    }

    [Fact]
    public async Task ARuleForAnotherEntity_ReportsPrag0717()
    {
        var messages = await RunAsync("""
            namespace App
            {
                [Pragmatic.Persistence.Entity.VisibleWhen<ForAnother>]
                public partial class Item2 { }
            }
            """, "PRAG0717");

        messages.Should().ContainSingle();
        messages[0].Should().Contain("ForAnother");
    }

    [Fact]
    public async Task AnAbstractRule_ReportsPrag0718()
    {
        var messages = await RunAsync("""
            namespace App
            {
                [Pragmatic.Persistence.Entity.VisibleWhen<CannotBuild>]
                public partial class Item { }
            }
            """, "PRAG0718");

        messages.Should().ContainSingle();
        messages[0].Should().Contain("CannotBuild");
    }

    /// <summary>
    ///     Lifting a filter without declaring the permission that allows it.
    /// </summary>
    /// <remarks>
    ///     A permission that is never asked for leaves no trace in any log, which is how the same
    ///     shape got through three times before. The attribute lifts either an entity's filters or one
    ///     named filter — both are a privilege, and this asks for the same thing of both.
    /// </remarks>
    [Fact]
    public async Task LiftingWithoutAPermission_ReportsPrag0719()
    {
        var messages = await RunAsync("""
            namespace App
            {
                [Pragmatic.Persistence.Entity.VisibleWhen<ConfirmedOnly>]
                public partial class Item { }

                [Pragmatic.Persistence.Query.Filters.WithoutFilter<ConfirmedOnly>]
                public partial class ReviewQueue { }
            }
            """, "PRAG0719");

        messages.Should().ContainSingle();
        messages[0].Should().Contain("ReviewQueue").And.Contain("ConfirmedOnly");
    }

    /// <summary>
    ///     The control for PRAG0719: with the permission, nothing is said.
    /// </summary>
    /// <remarks>
    ///     Without it the test above would also pass on an analyzer that reported every
    ///     <c>[WithoutFilter]</c>, which would make the diagnostic noise rather than a rule.
    /// </remarks>
    [Fact]
    public async Task LiftingWithAPermission_ReportsNothing()
    {
        var messages = await RunAsync("""
            namespace App
            {
                [Pragmatic.Persistence.Entity.VisibleWhen<ConfirmedOnly>]
                public partial class Item { }

                [Pragmatic.Authorization.RequirePermission("knowledge.read-unconfirmed")]
                [Pragmatic.Persistence.Query.Filters.WithoutFilter<ConfirmedOnly>]
                public partial class ReviewQueue { }
            }
            """, "PRAG0719");

        messages.Should().BeEmpty();
    }

    /// <summary>
    ///     Lifting an entity's whole filter set is the same privilege, and asks the same.
    /// </summary>
    /// <remarks>
    ///     The pre-existing form of the attribute. Naming an entity lifts everything it carries —
    ///     soft-delete and tenant included — so if anything needs a permission beside it, this does.
    /// </remarks>
    [Fact]
    public async Task LiftingAWholeEntity_AlsoNeedsAPermission()
    {
        var messages = await RunAsync("""
            namespace App
            {
                [Pragmatic.Persistence.Query.Filters.WithoutFilter<Item>]
                public partial class AdminSearch { }
            }
            """, "PRAG0719");

        messages.Should().ContainSingle();
        messages[0].Should().Contain("AdminSearch");
    }

    /// <summary>The control: a well-formed declaration says nothing at all.</summary>
    [Fact]
    public async Task AWellFormedDeclaration_ReportsNothing()
    {
        foreach (var id in new[] { "PRAG0717", "PRAG0718", "PRAG0719" })
        {
            var messages = await RunAsync("""
                namespace App
                {
                    [Pragmatic.Persistence.Entity.VisibleWhen<ConfirmedOnly>]
                    public partial class Item { }
                }
                """, id);

            messages.Should().BeEmpty($"a correct declaration must not raise {id}");
        }
    }

    /// <summary>
    ///     A rule the entity configuration cannot write <c>new TRule()</c> for.
    /// </summary>
    /// <remarks>
    ///     The constraint is not arbitrary and not about the container: a declared rule becomes an EF
    ///     Core named query filter, and its predicate is read once while the model is built. A rule
    ///     asking for a constructor argument has nowhere to get one, and a rule asking for the current
    ///     user could not be a model-level filter at all — the model is cached, so what it captured
    ///     would be served to everyone after the first request.
    /// </remarks>
    [Fact]
    public async Task ARuleWithConstructorArguments_ReportsPrag0718()
    {
        var messages = await RunAsync("""
            namespace App
            {
                [Pragmatic.Persistence.Entity.VisibleWhen<NeedsAClock>]
                public partial class Item { }
            }
            """, "PRAG0718");

        messages.Should().ContainSingle();
        messages[0].Should().Contain("NeedsAClock");
    }

    /// <summary>
    ///     <c>Disable&lt;TRule&gt;()</c> on a declared rule compiles and does nothing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The call reaches <c>DefaultQueryFilterProvider</c>, and a declared rule is not in it —
    ///         it is installed on the EF model, where the only way past is <c>IgnoreQueryFilters</c>.
    ///         So the scope is created, the query returns exactly the rows it would have returned
    ///         anyway, and nothing anywhere says so.
    ///     </para>
    ///     <para>
    ///         This is the shape that cost the most to find once already: the tenant rule was enforced
    ///         twice and <c>FilterMode.Background</c> lifted one of the two, so a job asking to read
    ///         across tenants read zero rows and reported success.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task DisablingARuleByType_ReportsPrag0720()
    {
        var messages = await RunAsync("""
            namespace App
            {
                [Pragmatic.Persistence.Entity.VisibleWhen<ConfirmedOnly>]
                public partial class Item { }

                public static class Caller
                {
                    public static void Read(Pragmatic.Persistence.Query.Filters.IQueryFilterToggle toggle)
                    {
                        using var scope = toggle.Disable<ConfirmedOnly>();
                    }
                }
            }
            """, "PRAG0720");

        messages.Should().ContainSingle();
        messages[0].Should().Contain("ConfirmedOnly").And.Contain("DisableVisibilityRule");
    }

    /// <summary>
    ///     The control for PRAG0720: disabling an ordinary filter is the supported thing to do.
    /// </summary>
    /// <remarks>
    ///     Without it the test above would also pass on an analyzer that reported every
    ///     <c>Disable&lt;T&gt;()</c>, which is the call the API exists for.
    /// </remarks>
    [Fact]
    public async Task DisablingAnOrdinaryFilter_ReportsNothing()
    {
        var messages = await RunAsync("""
            namespace App
            {
                public static class Caller2
                {
                    public static void Read(Pragmatic.Persistence.Query.Filters.IQueryFilterToggle toggle)
                    {
                        using var scope = toggle.Disable<PlainFilter>();
                    }
                }
            }
            """, "PRAG0720");

        messages.Should().BeEmpty();
    }

    /// <summary>
    ///     The <c>typeof</c> overload says the same thing and is caught the same way.
    /// </summary>
    [Fact]
    public async Task DisablingARuleByTypeof_ReportsPrag0720()
    {
        var messages = await RunAsync("""
            namespace App
            {
                [Pragmatic.Persistence.Entity.VisibleWhen<ConfirmedOnly>]
                public partial class Item { }

                public static class Caller3
                {
                    public static void Read(Pragmatic.Persistence.Query.Filters.IQueryFilterToggle toggle)
                    {
                        using var scope = toggle.Disable(typeof(ConfirmedOnly));
                    }
                }
            }
            """, "PRAG0720");

        messages.Should().ContainSingle();
    }

    /// <summary>
    ///     A mutation that writes the property its entity's rule keys on.
    /// </summary>
    /// <remarks>
    ///     The rule is an EF Core global query filter, so a row that stops satisfying it is invisible
    ///     to every query of the entity — an update included, because an update loads through the same
    ///     filtered query. Without <c>[WithoutFilter]</c> the operation that sets the flag back cannot
    ///     load the row it exists to fix.
    /// </remarks>
    [Fact]
    public async Task AMutationWritingTheKeyedProperty_ReportsPrag0721()
    {
        var messages = await RunAsync("""
            namespace App
            {
                [Pragmatic.Actions.Mutation.Mutation]
                public partial class ReactivateProperty : Pragmatic.Actions.Mutation.Mutation<Property>
                {
                    public bool IsActive { get; set; }
                }
            }
            """, "PRAG0721");

        messages.Should().ContainSingle();
        messages[0].Should().Contain("ReactivateProperty").And.Contain("IsActive").And.Contain("ActiveOnly");
    }

    /// <summary>
    ///     The control that keeps it from being noise: a mutation of the same entity that writes
    ///     something else says nothing.
    /// </summary>
    /// <remarks>
    ///     This is the whole reason it keys on the property rather than on the entity. Most mutations
    ///     of an entity carrying a rule only ever load rows that still satisfy it, and flagging them
    ///     all would make the diagnostic something people suppress instead of read.
    /// </remarks>
    [Fact]
    public async Task AMutationWritingSomethingElse_ReportsNothing()
    {
        var messages = await RunAsync("""
            namespace App
            {
                [Pragmatic.Actions.Mutation.Mutation]
                public partial class RenameProperty : Pragmatic.Actions.Mutation.Mutation<Property>
                {
                    public string Name { get; set; }
                }
            }
            """, "PRAG0721");

        messages.Should().BeEmpty();
    }

    /// <summary>With the opt-out declared, nothing is said.</summary>
    [Fact]
    public async Task AMutationThatLiftsTheRule_ReportsNothing()
    {
        var messages = await RunAsync("""
            namespace App
            {
                [Pragmatic.Actions.Mutation.Mutation]
                [Pragmatic.Persistence.Query.Filters.WithoutFilter<ActiveOnly>]
                public partial class ReactivateProperly : Pragmatic.Actions.Mutation.Mutation<Property>
                {
                    public bool IsActive { get; set; }
                }
            }
            """, "PRAG0721");

        messages.Should().BeEmpty();
    }
}
