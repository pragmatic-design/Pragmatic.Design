using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     Tests for Invoker and Registration generation.
/// </summary>
public class InvokerGeneratorTests : ActionsGeneratorTestBase
{
    private const string CommonUsings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;
        """;

    [Fact]
    public void DomainAction_GeneratesInvokerWithCorrectBaseClass()
    {
        var source = CommonUsings + """

            namespace TestApp;

            public interface IOrderRepository { }

            [DomainAction]
            public partial class PlaceOrder : DomainAction<string>
            {
                private IOrderRepository _orderRepository;

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("done"));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "Invoker");
        generated.Should().NotBeNull();
        generated.Should().Contain("class Invoker");
        generated.Should().Contain("DomainActionInvoker<");
        generated.Should().Contain("InjectDependencies");
        generated.Should().Contain("IServiceProvider serviceProvider");
    }

    [Fact]
    public void StandaloneRegistration_RegistersInvokerInterface_ForStandaloneResolution()
    {
        // The standalone AddActions() must register the IDomainActionInvoker<T,R> INTERFACE
        // (not just the concrete nested Invoker) so a standalone consumer that only calls AddActions()
        // — e.g. a standalone Endpoints app without the host generator — can resolve the invoker.
        var source = CommonUsings + """

            namespace TestApp;

            [DomainAction]
            public partial class GetThing : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var registration = GetGeneratedSource(result, "Actions.Registration");
        registration.Should().NotBeNull();
        // Interface → concrete Invoker, so IDomainActionInvoker<GetThing, Guid> resolves.
        registration.Should().Contain("AddScoped<global::Pragmatic.Actions.Invoker.IDomainActionInvoker<");
        registration.Should().Contain("global::TestApp.GetThing");
        registration.Should().Contain(".Invoker>();");
    }

    [Fact]
    public void VoidDomainAction_GeneratesInvokerWithVoidBaseClass()
    {
        var source = CommonUsings + """

            namespace TestApp;

            public interface IEmailService { }

            [DomainAction]
            public partial class SendEmail : VoidDomainAction
            {
                private IEmailService _emailService;

                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "Invoker");
        generated.Should().NotBeNull();
        generated.Should().Contain("class Invoker");
        generated.Should().Contain("VoidDomainActionInvoker<");

        // IVoidDomainActionInvoker registration is in the aggregate registration, not the invoker file
        var registration = GetGeneratedSource(result, "Actions.Registration");
        registration.Should().NotBeNull();
        registration.Should().Contain("IVoidDomainActionInvoker<");
    }

    [Fact]
    public void StandaloneRegistration_WithRequirePermission_RegistersGeneratedRegistry()
    {
        // The standalone registration must register the generated permission registry —
        // otherwise AddPragmaticActions()'s empty default wins (TryAddSingleton) and
        // [RequirePermission] silently fails open.
        var source = CommonUsings + """

            namespace TestApp;

            [DomainAction]
            [Pragmatic.Authorization.RequirePermission("orders.create")]
            public partial class CreateOrder : DomainAction<string>
            {
                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("done"));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var registration = GetGeneratedSource(result, "Actions.Registration");
        registration.Should().NotBeNull();
        registration.Should().Contain("IPermissionRequirementRegistry");
        registration.Should().Contain("GeneratedPermissionRequirementRegistry");
        // Registered with AddSingleton so it wins over the empty TryAddSingleton default.
        registration.Should().Contain("AddSingleton<global::Pragmatic.Actions.Pipeline.IPermissionRequirementRegistry");
    }

    [Fact]
    public void Action_WithNoDependencies_GeneratesInvokerWithEmptyInjectDependencies()
    {
        var source = CommonUsings + """

            namespace TestApp;

            [DomainAction]
            public partial class SimpleAction : DomainAction<string>
            {
                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("done"));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "Invoker");
        generated.Should().NotBeNull();
        generated.Should().Contain("class Invoker");
        generated.Should().Contain("InjectDependencies");
    }

    [Fact]
    public void Invoker_RegistrationUsesNestedInvokerDirectly()
    {
        var source = CommonUsings + """

            namespace TestApp;

            public interface IOrderRepository { }

            [DomainAction]
            public partial class PlaceOrder : DomainAction<string>
            {
                private IOrderRepository _orderRepository;

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("done"));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        // No per-action DI extension class is generated
        var invoker = GetGeneratedSource(result, "Invoker");
        invoker.Should().NotBeNull();
        invoker.Should().Contain("class Invoker");
        invoker.Should().NotContain("PlaceOrderInvokerExtensions");

        // Aggregate registration registers directly with AddScoped
        var registration = GetGeneratedSource(result, "Actions.Registration");
        registration.Should().NotBeNull();
        registration.Should().Contain("PlaceOrder.Invoker");
        registration.Should().Contain("AddScoped");
    }

    [Fact]
    public void MultipleActions_GenerateAggregateRegistration()
    {
        var source = CommonUsings + """

            namespace TestApp.Actions;

            public interface IOrderRepository { }
            public interface IEmailService { }

            [DomainAction]
            public partial class PlaceOrder : DomainAction<string>
            {
                private IOrderRepository _orderRepository;

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("done"));
            }

            [DomainAction]
            public partial class CancelOrder : VoidDomainAction
            {
                private IEmailService _emailService;

                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "Actions.Registration");
        generated.Should().NotBeNull();
        generated.Should().Contain("AddTestAppActions");
        generated.Should().Contain("PlaceOrder.Invoker");
        generated.Should().Contain("CancelOrder.Invoker");
        generated.Should().Contain("AddScoped");
    }

    // =========================================================================
    // Tests — Boundary Inference → IUnitOfWork generation
    // =========================================================================

    /// <summary>
    ///     GetBelongsToFromType has to recognise Pragmatic.Persistence.Entity.BelongsToAttribute&lt;T&gt;,
    ///     the form entities use. Checking only the non-generic attribute leaves the generated invoker
    ///     without _unitOfWork even though the entity is properly annotated — so EF changes are never saved.
    /// </summary>
    [Fact]
    public void ActionWithIRepository_WhereEntityHasPersistenceBelongsTo_GeneratesUnitOfWork()
    {
        var source = CommonUsings + """

            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Repository;

            namespace TestApp;

            public class OrderBoundary { }

            // Entity uses Pragmatic.Persistence.Entity.BelongsToAttribute<T> — NOT the Actions one
            [BelongsTo<OrderBoundary>]
            public class Order : IEntity
            {
                public Guid Id { get; }
            }

            [DomainAction]
            public partial class PlaceOrder : DomainAction<Guid>
            {
                // IRepository<Order> → generator should infer boundary from Order's [BelongsTo]
                private IRepository<Order> _orders = null!;

                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }
            """;

        var result = RunGeneratorWithEntities(source);

        var generated = GetGeneratedSource(result, "Invoker");
        generated.Should().NotBeNull();
        generated.Should().Contain("_unitOfWork",
            because: "boundary was inferred from IRepository<Order> where Order has [BelongsTo<OrderBoundary>] from Pragmatic.Persistence.Entity");
        generated.Should().Contain("IUnitOfWork");
        generated.Should().Contain("SaveChangesAsync");
        generated.Should().Contain("OrderBoundary",
            because: "keyed IUnitOfWork should be scoped to the correct boundary type");
    }

    /// <summary>
    ///     An explicit <c>[BelongsTo&lt;T&gt;]</c> on the action still produces the keyed
    ///     <c>_unitOfWork</c>.
    /// </summary>
    /// <remarks>
    ///     The attribute is the one in <c>Pragmatic.Persistence.Entity</c>; there is no second copy
    ///     under the same simple name, so this is the only path there is.
    /// </remarks>
    [Fact]
    public void ActionWithExplicitBelongsTo_GeneratesUnitOfWork()
    {
        var source = CommonUsings + """

            using Pragmatic.Actions.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            public class SalesBoundary { }

            [DomainAction]
            [BelongsTo<SalesBoundary>]
            public partial class CreateOrder : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
            }
            """;

        // RunGeneratorWithEntities includes Pragmatic.Persistence references needed
        // because the generated invoker references IUnitOfWork from that assembly.
        var result = RunGeneratorWithEntities(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "Invoker");
        generated.Should().Contain("_unitOfWork");
        generated.Should().Contain("SalesBoundary");
        generated.Should().Contain("SaveChangesAsync");
    }

    /// <summary>
    ///     Actions with only IReadRepository dependencies (no write repo) should NOT get
    ///     a _unitOfWork — reads don't need a persistence boundary.
    /// </summary>
    [Fact]
    public void ActionWithOnlyIReadRepository_DoesNotGenerateUnitOfWork()
    {
        var source = CommonUsings + """

            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Repository;

            namespace TestApp;

            public class CatalogBoundary { }

            [BelongsTo<CatalogBoundary>]
            public class Product : IEntity
            {
                public Guid Id { get; }
            }

            [DomainAction]
            public partial class GetProduct : DomainAction<string>
            {
                // IReadRepository only — should NOT trigger UnitOfWork generation
                private IReadRepository<Product> _products = null!;

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("ok"));
            }
            """;

        var result = RunGeneratorWithEntities(source);

        var generated = GetGeneratedSource(result, "Invoker");
        generated.Should().NotBeNull();
        generated.Should().NotContain("_unitOfWork",
            because: "read-only access should not produce a UnitOfWork — only write repositories trigger boundary inference");
        generated.Should().NotContain("SaveChangesAsync");
    }

    [Fact]
    public void ActionWithErrorTypes_GeneratesCorrectInvokerGenericParams()
    {
        var source = CommonUsings + """

            namespace TestApp;

            public class ValidationError : IError
            {
                public string Code => "VALIDATION";
                public int StatusCode => 422;
            }

            public interface IOrderRepository { }

            [DomainAction]
            public partial class PlaceOrder : DomainAction<string, ValidationError>
            {
                private IOrderRepository _orderRepository;

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("done"));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "Invoker");
        generated.Should().NotBeNull();
        // The invoker should use DomainActionInvoker<PlaceOrder, string> (return type, not error type)
        generated.Should().Contain("DomainActionInvoker<");

        // IDomainActionInvoker registration is in the aggregate registration, not the invoker file
        var registration = GetGeneratedSource(result, "Actions.Registration");
        registration.Should().NotBeNull();
        registration.Should().Contain("IDomainActionInvoker<");
    }
}
