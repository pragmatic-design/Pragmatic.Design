using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.Composition.Attributes;
using Pragmatic.Internationalization.Context;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.I18n;

/// <summary>
///     A module may declare a dependency on the configured default culture.
/// </summary>
/// <remarks>
///     <para>
///         The resolver cannot be that dependency. <c>I18NConfigResolver</c> is registered by
///         <c>UseI18N</c> and carries no <c>[ProvidedByHost]</c>, so a <c>[Service]</c> that injects it
///         is refused at compile time — <c>PRAG1641</c>, "depends on 'I18NConfigResolver' which is not
///         registered" — although <c>I18NContextMiddleware</c> receives it by injection on every
///         request. The analyzer is right about the declaration and wrong about the world.
///     </para>
///     <para>
///         ⚠️ The remedy is a contract and not an attribute on the resolver: the resolver merges
///         providers by priority and validates the result, and what a module asks is one question —
///         "what language when nobody said?". A module that cannot ask writes a constant naming the
///         language its own translation file happens to be in, which is honest and silently diverges
///         the day the host is configured differently.
///     </para>
/// </remarks>
public class AModuleMayAskWhatTheHostWasConfiguredWithTests
{
    private const string NotRegistered = "PRAG1641";
    private const string LifetimeMismatch = "PRAG1642";

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<ServiceAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Result.IError>(),
        GeneratorTestHelper.FromType<IConfiguredCultures>(),
        GeneratorTestHelper.FromType<global::Microsoft.Extensions.DependencyInjection.IServiceCollection>()
    ];

    /// <summary>The setpoint: the contract is injectable from a module.</summary>
    [Fact]
    public void AServiceThatAsksForTheConfiguredCultures_IsNotReportedAsUnregistered()
    {
        var source = """
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Internationalization.Context;

            namespace TestApp.Letters;

            public interface IWriteALetter { }

            [Service]
            public class TheLetter : IWriteALetter
            {
                public TheLetter(IConfiguredCultures cultures) { }
            }
            """;

        var ids = IdsOf(GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, References));

        ids.Should().NotContain(NotRegistered,
            "UseI18N registers it, and the contract says so where the generator can read it");
    }

    /// <summary>
    ///     The lifetime is part of the declaration, and it is <b>Scoped</b>: a provider may be
    ///     per-request, so a singleton holding this would hold the first scope's answer for every scope
    ///     after it.
    /// </summary>
    /// <remarks>
    ///     The control that keeps the declaration honest. <c>[ProvidedByHost]</c> without a lifetime
    ///     buys silence from <c>PRAG1642</c> as well as from <c>PRAG1641</c> — a name the generator has
    ///     never heard of is exempt from both — so saying Scoped and being checked on it is the
    ///     difference between a declaration and an exemption.
    /// </remarks>
    [Fact]
    public void ASingletonThatHoldsThem_IsReportedAsCapturingAScopedContract()
    {
        var source = """
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Internationalization.Context;

            namespace TestApp.Letters;

            public interface IWriteALetter { }

            [Service(Lifetime = Lifetime.Singleton)]
            public class TheLetter : IWriteALetter
            {
                public TheLetter(IConfiguredCultures cultures) { }
            }
            """;

        var ids = IdsOf(GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, References));

        ids.Should().Contain(LifetimeMismatch,
            "the contract is registered per scope, and one instance would answer for every scope");
        ids.Should().NotContain(NotRegistered);
    }

    /// <summary>
    ///     And the control that says this is not "stop checking": the resolver behind it is still not a
    ///     module's dependency.
    /// </summary>
    /// <remarks>
    ///     Deliberate. <c>I18NConfigResolver</c> carries the provider merge and the validation; opening
    ///     it to modules would publish that as a contract, which is the thing this story chose not to
    ///     do. A module asking for it still gets an answer — the diagnostic naming it — instead of
    ///     nothing.
    /// </remarks>
    [Fact]
    public void TheResolverItself_IsStillNotAModulesDependency()
    {
        var source = """
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Internationalization.Context;

            namespace TestApp.Letters;

            public interface IWriteALetter { }

            [Service]
            public class TheLetter : IWriteALetter
            {
                public TheLetter(I18NConfigResolver resolver) { }
            }
            """;

        IdsOf(GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, References))
            .Should().Contain(NotRegistered);
    }

    private static string[] IdsOf(SourceGenRunResult result)
        => [.. result.Diagnostics.Select(d => d.Id).Distinct()];
}
