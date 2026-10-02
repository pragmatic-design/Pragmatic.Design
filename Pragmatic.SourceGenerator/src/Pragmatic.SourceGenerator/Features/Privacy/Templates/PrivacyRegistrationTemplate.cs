using System.Collections.Generic;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Privacy.Models;

namespace Pragmatic.SourceGenerator.Features.Privacy.Templates;

/// <summary>
///     Generates <c>_Infra.Privacy.Registration.g.cs</c> — the one call that puts this assembly's
///     generated privacy adapters into DI.
/// </summary>
/// <remarks>
///     <para>
///         Without this the whole feature is inert. <c>SubjectAccessService</c>,
///         <c>ErasureOrchestrator</c> and <c>ProcessingRegisterBuilder</c> each take an
///         <c>IEnumerable&lt;…&gt;</c> and do exactly what an empty enumerable asks for: an access
///         request returns nothing, an erasure erases nothing, and both report success.
///     </para>
///     <para>
///         <c>TryAddEnumerable</c> per descriptor, not <c>AddScoped</c>: calling the registration twice
///         — a module referenced by two hosts in one process, a test that builds the container again —
///         would otherwise double every source, and two sources with the same category is an exception
///         at collect time, not a duplicate row.
///     </para>
/// </remarks>
internal sealed class PrivacyRegistrationTemplate : CSharpTemplate
{
    /// <summary>The generated class name, shared with whatever tells the host to call it.</summary>
    public const string ClassName = "PragmaticPrivacyRegistration";

    /// <summary>The generated method name, shared with whatever tells the host to call it.</summary>
    public const string MethodName = "AddGeneratedPrivacyAdapters";

    private readonly IReadOnlyList<PrivacyAdapterRegistration> _adapters;
    private readonly string? _activitySourceFqn;
    private readonly string _namespace;

    public PrivacyRegistrationTemplate(
        IReadOnlyList<PrivacyAdapterRegistration> adapters, string? activitySourceFqn, string @namespace)
    {
        _adapters = adapters;
        _activitySourceFqn = activitySourceFqn;
        _namespace = @namespace;
    }

    /// <summary>The <c>Namespace.Class.Method</c> the host calls for this assembly's adapters.</summary>
    public static string FqnFor(string @namespace)
        => string.IsNullOrEmpty(@namespace)
            ? ClassName + "." + MethodName
            : @namespace + "." + ClassName + "." + MethodName;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Privacy";

    protected override bool Validate() => _adapters.Count > 0 || _activitySourceFqn is not null;

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForAssembly("Privacy", "Registration"), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");
        AddUsing("Microsoft.Extensions.DependencyInjection.Extensions");

        AppendNamespace(_namespace);
        AppendLine();

        XmlSummary("Registers the privacy adapters this assembly's [PersonalData] classifications produce.");

        Class(ClassName, RenderMethod,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderMethod()
    {
        XmlSummary(
            "Contributes this assembly's personal-data sources, erasure steps and processing activities " +
            "to the data-subject services.");
        XmlParam("services", "The service collection.");
        XmlReturns("The service collection, for chaining.");

        Method(MethodName, RenderBody,
            "global::Microsoft.Extensions.DependencyInjection.IServiceCollection",
            [
                new MethodParameter(
                    "global::Microsoft.Extensions.DependencyInjection.IServiceCollection", "services")
                {
                    IsExtension = true
                }
            ],
            AccessModifier.Public,
            new MethodModifiers { IsStatic = true });
    }

    private void RenderBody()
    {
        // The services that consume everything below. Contributing sources to a container where nothing
        // resolves them is the same defect one level up: TryAdd throughout, so a second module calling
        // this adds nothing, and the file only exists when the runtime package is referenced.
        AppendLine("global::Pragmatic.Privacy.PrivacyServiceCollectionExtensions.AddPrivacy(services);");
        AppendLine();

        foreach (var adapter in _adapters.OrderBy(a => a.SourceFqn, StringComparer.Ordinal))
        {
            Comment($"{adapter.EntityFullTypeName}");
            RenderEnumerable("Scoped", "global::Pragmatic.Privacy.IPersonalDataSource", adapter.SourceFqn);
            RenderEnumerable("Scoped", "global::Pragmatic.Privacy.IErasureStep", adapter.ErasureStepFqn);
            AppendLine();
        }

        if (_activitySourceFqn is { } activities)
        {
            Comment("Declarations about the code, so one instance for the process.");
            RenderEnumerable("Singleton", "global::Pragmatic.Privacy.IProcessingActivitySource", activities);
            AppendLine();
        }

        AppendLine("return services;");
    }

    /// <summary>
    ///     One <c>TryAddEnumerable</c> line.
    /// </summary>
    /// <remarks>
    ///     Scoped for the two that hold a <c>DbContext</c>: a singleton capturing a scoped context is the
    ///     captive dependency that keeps one connection alive for the life of the process.
    /// </remarks>
    private void RenderEnumerable(string lifetime, string contract, string implementation)
        => AppendLine(
            "services.TryAddEnumerable(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor" +
            $".{lifetime}<{contract}, global::{implementation}>());");
}
