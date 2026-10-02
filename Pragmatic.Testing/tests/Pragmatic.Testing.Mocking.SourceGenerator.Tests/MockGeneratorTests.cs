using System.IO;
using System.Linq;
using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.Testing.Mocking;
using Xunit;

namespace Pragmatic.Testing.Mocking.SourceGenerator.Tests;

/// <summary>
///     Verifies the mock generator end to end, and — the part that matters for a generator — that
///     what it emits actually compiles against the real runtime types rather than against a
///     re-declared stand-in.
/// </summary>
public class MockGeneratorTests
{
    private const string Source = """
        using Pragmatic.Testing.Mocking;

        [assembly: GenerateMock<App.IClock>]
        [assembly: GenerateMock<App.IStore>]
        [assembly: GenerateMock<App.IScope>]
        [assembly: GenerateMock<App.IHandler<App.Evt>>]
        [assembly: GenerateMock<App.IDispatcher>]

        namespace App
        {
            public interface IClock
            {
                System.DateTimeOffset UtcNow { get; }
                System.TimeProvider GetTimeProvider();
            }

            public interface IStore
            {
                string? Name { get; set; }
                System.Threading.Tasks.ValueTask<string?> GetAsync(string key, System.Threading.CancellationToken ct);
                void Add(object entity);
            }

            // Inherits a member from another interface — Dispose belongs to IDisposable, and an
            // explicit implementation has to say so.
            public interface IScope : System.IDisposable
            {
                IStore Store { get; }
            }

            public sealed class Evt { }

            // `@event` is a keyword-named parameter, and ISymbol.Name gives back the bare keyword.
            // Also: a constrained, contravariant type parameter and an optional argument — the shape
            // of IDomainEventHandler.
            public interface IHandler<in TEvent>
                where TEvent : class
            {
                int Order => 0;
                System.Threading.Tasks.Task HandleAsync(TEvent @event, System.Threading.CancellationToken ct = default);
            }

            // Overload + generic overload on the same name, which is IDomainEventDispatcher's shape.
            // Declared in IDomainEventDispatcher's order — generic FIRST. Were declaration order to
            // decide who gets the plain name, the generic one would take it and the useful overload
            // would become DispatchAsync_2, breaking every test written against it.
            public interface IDispatcher
            {
                System.Threading.Tasks.Task DispatchAsync<TEvent>(TEvent e, System.Threading.CancellationToken ct);
                System.Threading.Tasks.Task DispatchAsync(System.Collections.Generic.IEnumerable<object> events, System.Threading.CancellationToken ct);
            }
        }
        """;

    private static (CSharpCompilation Compilation, GeneratorDriverRunResult Result) Run(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Threading.Tasks.dll")),
            // The real runtime: MockProperty, MockMethod, GenerateMockAttribute.
            MetadataReference.CreateFromFile(typeof(MockProperty<int>).Assembly.Location)
        };

        // Nullable enabled to match every project in this repository: an interface declaring `T?` on
        // an unconstrained parameter means something different without it.
        var compilation = CSharpCompilation.Create("MockGenTest", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MockGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);

        return ((CSharpCompilation)updated, driver.GetRunResult());
    }

    private static string Generated(GeneratorDriverRunResult result, string fileNamePart) =>
        result.GeneratedTrees.FirstOrDefault(t => t.FilePath.Contains(fileNamePart))?.GetText().ToString() ?? "";

    [Fact]
    public void GeneratedCode_Compiles()
    {
        var (compilation, _) = Run(Source);

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToList();

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Interface_ProducesMockNamedAfterItWithoutTheI()
    {
        var (_, result) = Run(Source);

        Generated(result, "Mock.ClockMock").Should().Contain("public sealed class ClockMock : global::App.IClock");
        Generated(result, "Mock.StoreMock").Should().Contain("public sealed class StoreMock : global::App.IStore");
    }

    [Fact]
    public void Property_IsExposedPubliclyAndImplementedExplicitly()
    {
        var source = Generated(Run(Source).Result, "Mock.ClockMock");

        // configurable member, under the interface member's own name
        source.Should().Contain("MockProperty<global::System.DateTimeOffset> UtcNow { get; }");
        source.Should().Contain("\"IClock.UtcNow\"");
        // and the explicit implementation the system under test goes through
        source.Should().Contain("global::System.DateTimeOffset global::App.IClock.UtcNow");
        source.Should().Contain("get => UtcNow.Get();");
    }

    [Fact]
    public void SettableProperty_GetsBothAccessors()
    {
        var source = Generated(Run(Source).Result, "Mock.StoreMock");

        source.Should().Contain("get => Name.Get();");
        source.Should().Contain("set => Name.Set(value);");
    }

    [Fact]
    public void Method_MapsToTheMatchingRuntimeArity()
    {
        var source = Generated(Run(Source).Result, "Mock.StoreMock");

        // two parameters + a return type → MockMethod<T1, T2, TResult>.
        // Types are fully qualified without language aliases, which is what makes generated code
        // immune to a `using` or a local type shadowing `string`.
        source.Should().Contain(
            "MockMethod<global::System.String, global::System.Threading.CancellationToken, "
            + "global::System.Threading.Tasks.ValueTask<global::System.String?>> GetAsync");
        source.Should().Contain("=> GetAsync.Invoke(key, ct);");
    }

    [Fact]
    public void VoidMethod_UsesTheVoidArity()
    {
        var source = Generated(Run(Source).Result, "Mock.StoreMock");

        source.Should().Contain("MockVoidMethod<global::System.Object> Add");
        source.Should().Contain("=> Add.Invoke(entity);");
    }

    [Fact]
    public void ParameterlessMethod_UsesTheZeroArity()
    {
        var source = Generated(Run(Source).Result, "Mock.ClockMock");

        source.Should().Contain("MockMethod<global::System.TimeProvider> GetTimeProvider");
        source.Should().Contain("=> GetTimeProvider.Invoke();");
    }

    [Fact]
    public void InheritedMember_IsQualifiedWithTheInterfaceThatDeclaresIt()
    {
        var source = Generated(Run(Source).Result, "Mock.ScopeMock");

        // Dispose comes from IDisposable, not IScope: qualifying it with IScope does not compile.
        source.Should().Contain("global::System.IDisposable.Dispose()");
        source.Should().NotContain("global::App.IScope.Dispose()");
    }

    [Fact]
    public void ClosedGeneric_KeepsItsTypeArgumentInTheClassName()
    {
        var (_, result) = Run(Source);

        // Otherwise two instantiations of the same generic collide on one class name.
        Generated(result, "Mock.HandlerOfEvtMock")
            .Should().Contain("public sealed class HandlerOfEvtMock : global::App.IHandler<global::App.Evt>");
    }

    [Fact]
    public void ClosedGeneric_FullyQualifiesTheTypeArgumentInMembers()
    {
        var source = Generated(Run(Source).Result, "Mock.HandlerOfEvtMock");

        source.Should().Contain("global::App.Evt");
        source.Should().NotContain("(Evt ");
    }

    [Fact]
    public void OverloadedGenericAndNonGeneric_BothImplemented()
    {
        var source = Generated(Run(Source).Result, "Mock.DispatcherMock");

        source.Should().Contain("global::App.IDispatcher.DispatchAsync(");
        source.Should().Contain("global::App.IDispatcher.DispatchAsync<TEvent>(");
    }

    [Fact]
    public void GenericOverload_DoesNotMakeThePlainOneUnconfigurable()
    {
        var source = Generated(Run(Source).Result, "Mock.DispatcherMock");

        // Only the generic one is out of reach; the plain overload stays configurable — and it keeps
        // the unsuffixed name regardless of which was declared first.
        source.Should().Contain("DispatchAsync { get; }");
        source.Should().NotContain("DispatchAsync_2");
    }

    [Fact]
    public void TaskReturningMember_IsSeededWithACompletedTask()
    {
        var source = Generated(Run(Source).Result, "Mock.HandlerOfEvtMock");

        // Otherwise an unconfigured async member hands back null and the caller's await throws a
        // NullReferenceException from inside the code under test.
        source.Should().Contain(".Returns(global::System.Threading.Tasks.Task.CompletedTask)");
    }

    [Fact]
    public void ValueTaskReturningMember_IsNotSeeded()
    {
        var source = Generated(Run(Source).Result, "Mock.StoreMock");

        // A default ValueTask is already completed; seeding it would be noise.
        source.Should().Contain("GetAsync { get; } = new(\"IStore.GetAsync\");");
    }

    [Fact]
    public void SealedClass_ReportsPRAG2350AndGeneratesNothing()
    {
        const string source = """
            using Pragmatic.Testing.Mocking;

            [assembly: GenerateMock<App.Thing>]

            namespace App { public sealed class Thing { } }
            """;

        var (_, result) = Run(source);

        result.Diagnostics.Select(d => d.Id).Should().Contain("PRAG2350");
        result.GeneratedTrees.Should().BeEmpty();
    }

    /// <summary>
    ///     The Azure and Google Cloud SDKs ship their clients as classes with virtual members and no
    ///     interface, which is the whole reason a mock can derive from a class at all.
    /// </summary>
    [Fact]
    public void Class_DerivesAndOverridesItsVirtualMembers()
    {
        const string source = """
            using Pragmatic.Testing.Mocking;

            [assembly: GenerateMock<App.BlobClient>]

            namespace App
            {
                public class BlobClient
                {
                    public virtual string Name => "real";
                    public virtual int Upload(string content) => 0;
                }
            }
            """;

        var (compilation, result) = Run(source);
        var generated = Generated(result, "Mock.BlobClientMock");

        generated.Should().Contain("public sealed class BlobClientMock : global::App.BlobClient");
        // The override holds the member's own name, so the configurable member takes a suffix.
        generated.Should().Contain("MockProperty<global::System.String> NameSetup")
            .And.Contain("public override global::System.String Name")
            .And.Contain("public override global::System.Int32 Upload(");
        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
    }

    /// <summary>
    ///     Only virtual members can be intercepted. A non-virtual one must be left alone entirely:
    ///     overriding it does not compile, and exposing a configurable member for it would promise
    ///     an interception that never happens.
    /// </summary>
    [Fact]
    public void Class_NonVirtualMembers_AreLeftAlone()
    {
        const string source = """
            using Pragmatic.Testing.Mocking;

            [assembly: GenerateMock<App.Client>]

            namespace App
            {
                public class Client
                {
                    public virtual int Virtual() => 0;
                    public int NotVirtual() => 0;
                    public string Fixed => "x";
                }
            }
            """;

        var (compilation, result) = Run(source);
        var generated = Generated(result, "Mock.ClientMock");

        generated.Should().Contain("Virtual").And.NotContain("NotVirtual").And.NotContain("Fixed");
        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
    }

    [Fact]
    public void OverloadedMembers_EachGetTheirOwnConfigurableMember()
    {
        const string source = """
            using Pragmatic.Testing.Mocking;

            [assembly: GenerateMock<App.IOverloaded>]

            namespace App
            {
                public interface IOverloaded
                {
                    void Activate(string reason);
                    void Activate(string reason, int minutes);
                }
            }
            """;

        var (compilation, result) = Run(source);

        // Named by parameter count: two members cannot share a name, and skipping both would leave
        // an interface like IDatabase — which is nothing but overloads — impossible to configure.
        var mock = Generated(result, "Mock.OverloadedMock");
        mock.Should().Contain("Activate1 { get; }");
        mock.Should().Contain("Activate2 { get; }");

        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
    }

    [Fact]
    public void GenericMethod_ReportsPRAG2352ButStillCompiles()
    {
        const string source = """
            using Pragmatic.Testing.Mocking;

            [assembly: GenerateMock<App.ICache>]

            namespace App
            {
                public interface ICache
                {
                    // `T?` on an unconstrained parameter is the shape ICacheStack actually uses,
                    // and the one an explicit implementation cannot repeat verbatim.
                    T? Get<T>(string key);
                    System.Threading.Tasks.ValueTask<T?> GetAsync<T>(string key);
                }
            }
            """;

        var (compilation, result) = Run(source);

        result.Diagnostics.Select(d => d.Id).Should().Contain("PRAG2352");
        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("a generic member must still be implemented");
    }

    /// <summary>
    ///     A generic method whose return type is built from its own type parameter still compiles.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The member that stands in for a generic method is a property of the mock, not of the
    ///     method, so seeding it with a default that names <c>T</c> is a CS0246 on <c>T</c> — an error
    ///     inside a file the author cannot edit, and the whole mock is lost with it. Found the first
    ///     time a mocked interface grew such a method (<c>IReadRepository.RunAsync</c>): the
    ///     seed said <c>Task.FromResult&lt;IReadOnlyList&lt;TResult&gt;&gt;(default!)</c>.
    /// </remarks>
    [Fact]
    public void AGenericMethodReturningItsOwnTypeArgument_Compiles()
    {
        const string source = """
            using Pragmatic.Testing.Mocking;

            [assembly: GenerateMock<App.IReader>]

            namespace App
            {
                public interface IReader
                {
                    System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<T>> ReadAsync<T>(string key)
                        where T : class;
                }
            }
            """;

        var (compilation, _) = Run(source);

        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the mock's member cannot name the method's type parameter");
    }

    [Fact]
    public void DuplicateDeclaration_ReportsPRAG2353AndEmitsOnce()
    {
        const string source = """
            using Pragmatic.Testing.Mocking;

            [assembly: GenerateMock<App.IThing>]
            [assembly: GenerateMock<App.IThing>]

            namespace App { public interface IThing { int Value { get; } } }
            """;

        var (_, result) = Run(source);

        result.Diagnostics.Select(d => d.Id).Should().Contain("PRAG2353");
        result.GeneratedTrees.Should().HaveCount(1);
    }

    /// <summary>
    ///     SSH.NET's <c>IBaseClient</c> declares its events with a nullable handler. The template adds
    ///     <c>?</c> of its own, and the resulting <c>EventHandler&lt;T&gt;??</c> does not parse — taking
    ///     every member declared after it in the file down with it, which is how it was found: as eight
    ///     "does not implement" errors on members that were right there in the source.
    /// </summary>
    [Fact]
    public void NullableEventHandler_IsAnnotatedOnce()
    {
        const string source = """
            using System;
            using Pragmatic.Testing.Mocking;

            [assembly: GenerateMock<App.IPublisher>]

            namespace App
            {
                public interface IPublisher
                {
                    event EventHandler<EventArgs>? Raised;
                    string Name { get; }
                }
            }
            """;

        var (compilation, result) = Run(source);

        Generated(result, "Mock.PublisherMock").Should().NotContain("??");
        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the members after the event must survive");
    }

    /// <summary>
    ///     A <c>static abstract</c> member still has to be implemented by the class, but there is no
    ///     instance to configure it against — AWS's <c>IAmazonService</c> declares two, and every
    ///     <c>IAmazonS3</c> mock inherits them.
    /// </summary>
    /// <remarks>
    ///     The shape here is the only one reachable, and it took two failed attempts to find: an
    ///     interface with an <b>unimplemented</b> static abstract member cannot be a type argument
    ///     (CS8920), whether it declares the member or inherits it — so <c>[GenerateMock&lt;T&gt;]</c>
    ///     could not name it at all. What compiles is an interface further down the chain having
    ///     supplied the implementation, which is what the AWS SDK does.
    /// </remarks>
    [Fact]
    public void InheritedStaticAbstractMember_IsImplementedButNotConfigurable()
    {
        const string source = """
            using Pragmatic.Testing.Mocking;

            [assembly: GenerateMock<App.IFactory>]

            namespace App
            {
                public interface IHasStatics
                {
                    static abstract IHasStatics Create();
                }

                public interface IFactory : IHasStatics
                {
                    static IHasStatics IHasStatics.Create() => null!;
                    string Name { get; }
                }
            }
            """;

        var (compilation, result) = Run(source);
        var generated = Generated(result, "Mock.FactoryMock");

        generated.Should().Contain("static global::App.IHasStatics global::App.IHasStatics.Create()")
            .And.Contain("NotSupportedException");
        generated.Should().NotContain("MockMethod<global::App.IHasStatics> Create",
            "an interface with unimplemented static abstract members cannot be a type argument");
        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
    }

    [Fact]
    public void NameOverride_IsHonoured()
    {
        const string source = """
            using Pragmatic.Testing.Mocking;

            [assembly: GenerateMock<App.IThing>(Name = "FakeThing")]

            namespace App { public interface IThing { int Value { get; } } }
            """;

        var (_, result) = Run(source);

        Generated(result, "Mock.FakeThing").Should().Contain("public sealed class FakeThing : global::App.IThing");
    }
}
