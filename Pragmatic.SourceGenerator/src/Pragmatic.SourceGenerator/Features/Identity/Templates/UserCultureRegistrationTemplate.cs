using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Identity.Models;

namespace Pragmatic.SourceGenerator.Features.Identity.Templates;

/// <summary>
///     Generates <c>_Infra.I18n.UserCulture.g.cs</c> — the <c>UseUserCulture()</c> an application calls
///     to turn the generated culture provider on.
/// </summary>
/// <remarks>
///     <para>
///         <b>Deliberately not auto-registered.</b> Every other generated registration in this codebase
///         is called by the host without being asked, and that is right for wiring that has no
///         alternative. This one has: the culture a user sees is a product decision, and an application
///         that resolves it from a header, a subdomain or a claim would suddenly find a database read
///         on the culture path of every request, outranking what it configured. So it follows the configuration
///         in three tiers as a module strategy — a <c>Use*()</c> on <c>IPragmaticBuilder</c>, off until the host says otherwise.
///     </para>
///     <para>
///         Scoped, not singleton. The provider reads the current user; a singleton capturing the first
///         request's user is the captive dependency that answers everyone with one person's preference.
///     </para>
/// </remarks>
internal sealed class UserCultureRegistrationTemplate : CSharpTemplate
{
    /// <summary>The generated class name.</summary>
    public const string ClassName = "PragmaticUserCultureRegistration";

    /// <summary>The generated method name — the one an application types.</summary>
    public const string MethodName = "UseUserCulture";

    private readonly string _providerFqn;
    private readonly string _resolverFqn;
    private readonly string _namespace;

    public UserCultureRegistrationTemplate(string providerFqn, string resolverFqn, string @namespace)
    {
        _providerFqn = providerFqn;
        _resolverFqn = resolverFqn;
        _namespace = @namespace;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Identity";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForAssembly("I18n", "UserCulture"), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");
        AddUsing("Pragmatic.Composition");

        AppendNamespace(_namespace);
        AppendLine();

        XmlSummary("Turns on the generated per-user culture provider.");

        Class(ClassName, RenderMethod,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderMethod()
    {
        XmlSummary(
            "Resolves the UI culture from the current user's PreferredCulture, at priority 200. " +
            "Off unless called: see the class remarks for why this one is opt-in.");
        XmlParam("builder", "The Pragmatic builder.");
        XmlReturns("The same builder, for chaining.");

        Method(MethodName, () =>
        {
            // The resolver too, not only the provider, and with TryAdd. The module registers it now,
            // like any of its services — but a container built without the module's
            // registration, as a test builds one, must not throw the first time the culture path runs,
            // and one built with it must not hold the resolver twice.
            AppendLine(
                "global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions" +
                $".TryAddScoped<global::{_resolverFqn}>(builder.Services);");
            AppendLine(
                "builder.Services.AddScoped<global::Pragmatic.Internationalization.Context.II18NConfigProvider, " +
                $"global::{_providerFqn}>();");
            AppendLine("return builder;");
        },
        "global::Pragmatic.Composition.IPragmaticBuilder",
        [
            new MethodParameter("global::Pragmatic.Composition.IPragmaticBuilder", "builder")
            {
                IsExtension = true
            }
        ],
        AccessModifier.Public,
        new MethodModifiers { IsStatic = true });
    }
}
