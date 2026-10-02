using Xunit;
using CodeFixVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    Pragmatic.Abstractions.Analyzers.JsonContextCoverageAnalyzer,
    Pragmatic.Abstractions.CodeFixers.AddJsonSerializableCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Abstractions.CodeFixers.Tests;

public class AddJsonSerializableCodeFixTests
{
    private const string Usings = @"using Pragmatic.Messaging;
using System.Text.Json.Serialization;
";

    private const string Stub = @"
namespace Pragmatic.Messaging { public interface IMessageHandler<T> { } }
namespace System.Text.Json.Serialization
{
    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
    public sealed class JsonSerializableAttribute : System.Attribute { public JsonSerializableAttribute(System.Type type) { } }
    public abstract class JsonSerializerContext { }
}
";

    [Fact]
    public async Task Fix_AddsJsonSerializableToContext()
    {
        var testCode = Usings + Stub + @"
public class OrderPlaced { }
public class Other { }

[JsonSerializable(typeof(Other))]
public partial class AppJsonContext : JsonSerializerContext { }

public class {|PRAG2800:OrderPlacedHandler|} : IMessageHandler<OrderPlaced>
{
}";

        var fixedCode = Usings + Stub + @"
public class OrderPlaced { }
public class Other { }

[JsonSerializable(typeof(Other))]
[global::System.Text.Json.Serialization.JsonSerializableAttribute(typeof(OrderPlaced))]
public partial class AppJsonContext : JsonSerializerContext { }

public class OrderPlacedHandler : IMessageHandler<OrderPlaced>
{
}";

        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }
}
