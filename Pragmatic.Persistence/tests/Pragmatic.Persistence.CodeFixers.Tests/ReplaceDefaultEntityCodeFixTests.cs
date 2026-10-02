using Microsoft.CodeAnalysis.Testing;
using Xunit;
using CodeFixVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    Pragmatic.Persistence.Analyzers.DoNotDefaultEntityAnalyzer,
    Pragmatic.Persistence.CodeFixers.ReplaceDefaultEntityCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Persistence.CodeFixers.Tests;

public class ReplaceDefaultEntityCodeFixTests
{
    private const string EntityStub = @"
namespace Pragmatic.Persistence.Entity
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class EntityAttribute : System.Attribute { }

}
";

    [Fact]
    public async Task DefaultExplicit_FixReplacesWithCreate()
    {
        var testCode = EntityStub + @"
[Pragmatic.Persistence.Entity.Entity]
public partial class Order
{
    public static Order Create() => null;
}

class TestClass
{
    void Method()
    {
        var order = [|default(Order)|];
    }
}";

        var fixedCode = EntityStub + @"
[Pragmatic.Persistence.Entity.Entity]
public partial class Order
{
    public static Order Create() => null;
}

class TestClass
{
    void Method()
    {
        var order = Order.Create();
    }
}";

        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    [Fact]
    public async Task DefaultLiteral_FixReplacesWithCreate()
    {
        var testCode = EntityStub + @"
[Pragmatic.Persistence.Entity.Entity]
public partial class Order
{
    public static Order Create() => null;
}

class TestClass
{
    void Method()
    {
        Order order = [|default|];
    }
}";

        var fixedCode = EntityStub + @"
[Pragmatic.Persistence.Entity.Entity]
public partial class Order
{
    public static Order Create() => null;
}

class TestClass
{
    void Method()
    {
        Order order = Order.Create();
    }
}";

        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }
}
