using Microsoft.CodeAnalysis.Testing;
using Xunit;
using CodeFixVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    Pragmatic.Persistence.Analyzers.EntityConstructorAnalyzer,
    Pragmatic.Persistence.CodeFixers.ReplaceNewEntityCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Persistence.CodeFixers.Tests;

public class ReplaceNewEntityCodeFixTests
{
    private const string EntityStub = @"
namespace Pragmatic.Persistence.Entity
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class EntityAttribute : System.Attribute { }

}
";

    [Fact]
    public async Task NewEntity_FixReplacesWithCreate()
    {
        // Product has a Create() factory — fix replaces new with Create()
        var testCode = EntityStub + @"
[Pragmatic.Persistence.Entity.Entity]
public partial class Product
{
    public static Product Create() => null;
}

class TestClass
{
    void Method()
    {
        var p = [|new Product()|];
    }
}";

        var fixedCode = EntityStub + @"
[Pragmatic.Persistence.Entity.Entity]
public partial class Product
{
    public static Product Create() => null;
}

class TestClass
{
    void Method()
    {
        var p = Product.Create();
    }
}";

        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    [Fact]
    public async Task NewEntity_WithObjectInitializer_NoFixOffered()
    {
        // The diagnostic still fires, but the fix must NOT rewrite to Create() — that would
        // silently drop the initializer. Code stays unchanged (no code action registered).
        var testCode = EntityStub + @"
[Pragmatic.Persistence.Entity.Entity]
public partial class Product
{
    public static Product Create() => null;
    public string Name { get; set; }
}

class TestClass
{
    void Method()
    {
        var p = [|new Product { Name = ""x"" }|];
    }
}";

        // No code action is registered, so the diagnostic persists in the fixed state and the
        // source is unchanged — fixed source equals the test source (markup and all).
        await CodeFixVerifier.VerifyCodeFixAsync(testCode, testCode);
    }
}
