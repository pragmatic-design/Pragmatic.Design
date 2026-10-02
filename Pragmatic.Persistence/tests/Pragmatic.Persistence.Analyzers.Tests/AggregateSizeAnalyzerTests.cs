using Microsoft.CodeAnalysis.Testing;
using Xunit;
using AnalyzerVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Pragmatic.Persistence.Analyzers.AggregateSizeAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Persistence.Analyzers.Tests;

/// <summary>
///     Tests for <see cref="AggregateSizeAnalyzer"/> (PRAG0685): an [Entity] with more than 5 child
///     collections (collections whose element is itself an [Entity]) is flagged.
/// </summary>
public class AggregateSizeAnalyzerTests
{
    private const string EntityStub = @"
namespace Pragmatic.Persistence.Entity
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class EntityAttribute : System.Attribute { }

}

[Pragmatic.Persistence.Entity.Entity]
public partial class Child { public string Name { get; set; } }
";

    [Fact]
    public async Task EntityWithSixChildCollections_ReportsDiagnostic()
    {
        var test = EntityStub + @"
[Pragmatic.Persistence.Entity.Entity]
public partial class {|#0:Booking|}
{
    public System.Collections.Generic.List<Child> A { get; set; }
    public System.Collections.Generic.List<Child> B { get; set; }
    public System.Collections.Generic.List<Child> C { get; set; }
    public System.Collections.Generic.List<Child> D { get; set; }
    public System.Collections.Generic.List<Child> E { get; set; }
    public System.Collections.Generic.List<Child> F { get; set; }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(
            test,
            AnalyzerVerifier.Diagnostic("PRAG0685").WithLocation(0).WithArguments("Booking", 6));
    }

    [Fact]
    public async Task EntityWithFewChildCollections_NoDiagnostic()
    {
        var test = EntityStub + @"
[Pragmatic.Persistence.Entity.Entity]
public partial class Booking
{
    public System.Collections.Generic.List<Child> A { get; set; }
    public System.Collections.Generic.List<Child> B { get; set; }
    public System.Collections.Generic.List<Child> C { get; set; }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task PrimitiveCollectionsDoNotCount_NoDiagnostic()
    {
        // Collections of non-entities (primitives) are not child aggregates — not counted.
        var test = EntityStub + @"
[Pragmatic.Persistence.Entity.Entity]
public partial class Booking
{
    public System.Collections.Generic.List<string> A { get; set; }
    public System.Collections.Generic.List<string> B { get; set; }
    public System.Collections.Generic.List<int> C { get; set; }
    public System.Collections.Generic.List<int> D { get; set; }
    public System.Collections.Generic.List<int> E { get; set; }
    public System.Collections.Generic.List<int> F { get; set; }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NonEntity_NoDiagnostic()
    {
        var test = EntityStub + @"
public class NotAnEntity
{
    public System.Collections.Generic.List<Child> A { get; set; }
    public System.Collections.Generic.List<Child> B { get; set; }
    public System.Collections.Generic.List<Child> C { get; set; }
    public System.Collections.Generic.List<Child> D { get; set; }
    public System.Collections.Generic.List<Child> E { get; set; }
    public System.Collections.Generic.List<Child> F { get; set; }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }
}
