using System.Collections.Immutable;
using System.Linq;
using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Actions.Templates;
using Pragmatic.SourceGenerator.Features.Actions.Transforms;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

/// <summary>
///     Regression tests for two real codegen bugs in the Actions feature:
///     <list type="bullet">
///         <item>BUG 1 — permission + policy registries collided on the same hint name (CS8785).</item>
///         <item>BUG 2 — boundary overloads emitted unqualified enum default values (CS0103).</item>
///     </list>
/// </summary>
public class ActionsCodegenRegressionTests
{
    // BUG 1: when an assembly has BOTH a [RequirePermission] action AND a [RequirePolicy<T>] action,
    // both registries are emitted for the SAME namespace. If both rendered the hint
    // "_Infra.Actions.<namespace>.g.cs" (ignoring the namespacePrefix slot), the second
    // AddSource would throw CS8785 (duplicate hintName) and abort the whole compile.
    // The distinguishing registry name lives in the rendered slot, so the hints differ.
    [Fact]
    public void PermissionAndPolicyRegistries_SameAssembly_RenderDistinctHintNames()
    {
        const string assemblyNamespace = "MyApp.Security";

        var permissionTemplate = new PermissionRequirementRegistryTemplate(
            ImmutableArray.Create(new PermissionRequirementRegistryTemplate.PermissionEntry(
                "global::MyApp.Security.CreateOrder",
                ImmutableArray.Create("orders.create"),
                RequireAll: true)),
            assemblyNamespace);

        var policyTemplate = new PolicyRegistryTemplate(
            ImmutableArray.Create(new PolicyRegistryTemplate.PolicyEntry(
                "global::MyApp.Security.DeleteOrder",
                "global::MyApp.Security.AdminPolicy")),
            assemblyNamespace);

        var permissionHint = permissionTemplate.RenderOutput().HintName;
        var policyHint = policyTemplate.RenderOutput().HintName;

        permissionHint.Should().NotBe(policyHint,
            "the two registries must use distinct hint names or the generator aborts with CS8785");
        permissionHint.Should().Contain("PermissionRequirementRegistry");
        policyHint.Should().Contain("PolicyRegistry");
    }

    // BUG 2: an action input property with an enum-typed default (e.g. = DeliveryPriority.Standard)
    // is copied verbatim into the boundary unwrapped overload, which lives in a different namespace.
    // An unqualified "DeliveryPriority.Standard" fails with CS0103, so the default must be
    // fully qualified to match the (already fully-qualified) parameter type.
    [Fact]
    public void ParseInputProperties_EnumDefault_IsFullyQualified()
    {
        const string source =
            "namespace MyApp.Shipping {\n" +
            "  public enum DeliveryPriority { Standard = 0, Express = 1 }\n" +
            "  public sealed class ScheduleShipmentRequest {\n" +
            "    public string Address { get; set; } = \"\";\n" +
            "    public DeliveryPriority Priority { get; set; } = DeliveryPriority.Standard;\n" +
            "  }\n" +
            "}\n";

        var properties = ParseInputProperties(source, "MyApp.Shipping.ScheduleShipmentRequest");

        var priority = properties.Single(p => p.Name == "Priority");
        priority.DefaultValueSyntax.Should().Be("global::MyApp.Shipping.DeliveryPriority.Standard");
    }

    [Fact]
    public void ParseInputProperties_NullableEnumDefault_IsFullyQualified()
    {
        const string source =
            "namespace MyApp.Shipping {\n" +
            "  public enum DeliveryPriority { Standard = 0, Express = 1 }\n" +
            "  public sealed class ScheduleShipmentRequest {\n" +
            "    public DeliveryPriority? Priority { get; set; } = DeliveryPriority.Express;\n" +
            "  }\n" +
            "}\n";

        var properties = ParseInputProperties(source, "MyApp.Shipping.ScheduleShipmentRequest");

        var priority = properties.Single(p => p.Name == "Priority");
        priority.DefaultValueSyntax.Should().Be("global::MyApp.Shipping.DeliveryPriority.Express");
    }

    [Fact]
    public void ParseInputProperties_NonEnumDefault_IsLeftVerbatim()
    {
        const string source =
            "namespace MyApp.Shipping {\n" +
            "  public sealed class ScheduleShipmentRequest {\n" +
            "    public int Retries { get; set; } = 3;\n" +
            "  }\n" +
            "}\n";

        var properties = ParseInputProperties(source, "MyApp.Shipping.ScheduleShipmentRequest");

        var retries = properties.Single(p => p.Name == "Retries");
        retries.DefaultValueSyntax.Should().Be("3");
        retries.DefaultIsCompileTimeConstant.Should().BeTrue(
            "the control: a literal is a default the parameter list can carry");
    }

    /// <summary>
    ///     An initialiser that is not a constant yields no default at all.
    /// </summary>
    /// <remarks>
    ///     The syntax is copied verbatim into the boundary overload's parameter list, and a property
    ///     initialiser is an expression while a parameter default must be a compile-time constant.
    ///     <c>= []</c> on a <c>List&lt;string&gt;</c> — the ordinary way to write "starts empty" —
    ///     produced <c>List&lt;string&gt; aliases = []</c> and broke the build with CS1736, inside a
    ///     generated file the author cannot edit. Found by declaring one on a mutation.
    /// </remarks>
    [Fact]
    public void ParseInputProperties_CollectionExpressionDefault_IsNotCarriedIntoTheSignature()
    {
        const string source =
            "using System.Collections.Generic;\n" +
            "namespace MyApp.Shipping {\n" +
            "  public sealed class ScheduleShipmentRequest {\n" +
            "    public List<string> Tags { get; set; } = [];\n" +
            "  }\n" +
            "}\n";

        var properties = ParseInputProperties(source, "MyApp.Shipping.ScheduleShipmentRequest");

        var tags = properties.Single(p => p.Name == "Tags");
        tags.DefaultValueSyntax.Should().Be("[]",
            "the request body record still needs it — there it is a property initialiser and legal");
        tags.DefaultIsCompileTimeConstant.Should().BeFalse(
            "and the boundary overload must not write it after '=', which is CS1736");
    }

    /// <summary>The same for an object creation, which is the other common way to write it.</summary>
    [Fact]
    public void ParseInputProperties_NewExpressionDefault_IsNotCarriedIntoTheSignature()
    {
        const string source =
            "using System.Collections.Generic;\n" +
            "namespace MyApp.Shipping {\n" +
            "  public sealed class ScheduleShipmentRequest {\n" +
            "    public List<string> Tags { get; set; } = new();\n" +
            "  }\n" +
            "}\n";

        var properties = ParseInputProperties(source, "MyApp.Shipping.ScheduleShipmentRequest");

        var tags = properties.Single(p => p.Name == "Tags");
        tags.DefaultValueSyntax.Should().Be("new()");
        tags.DefaultIsCompileTimeConstant.Should().BeFalse();
    }

    /// <summary>
    ///     Compiles the source and reads the input properties off the named type.
    /// </summary>
    /// <remarks>
    ///     The compilation travels with the symbol because the extraction asks it what an initializer
    ///     evaluates to and what its names bind to — the symbol alone cannot answer either, which is
    ///     how an initializer resolved through a <c>using</c> reached a generated file that could not
    ///     bind it.
    /// </remarks>
    private static ImmutableArray<ActionPropertyModel> ParseInputProperties(
        string source, string fullyQualifiedTypeName)
    {
        var (symbol, compilation) = CompileAndGetType(source, fullyQualifiedTypeName);
        return InputPropertyHelpers.ParseInputProperties(symbol, compilation);
    }

    private static (INamedTypeSymbol Symbol, Compilation Compilation) CompileAndGetType(
        string source, string fullyQualifiedTypeName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
        };

        var compilation = CSharpCompilation.Create(
            "InputPropertiesTestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        var symbol = compilation.GetTypeByMetadataName(fullyQualifiedTypeName);
        symbol.Should().NotBeNull("type {0} should compile", fullyQualifiedTypeName);
        return (symbol!, compilation);
    }
}
