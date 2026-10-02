// Pragmatic.SourceGenerator - Composition - Pipeline Step Registration Template

using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Generates PragmaticDependencies.PipelineSteps.g.cs with AddPipelineSteps() method.
/// </summary>
internal sealed class PipelineStepRegistrationTemplate : CSharpTemplate
{
    private readonly string _namespacePrefix;
    private readonly ImmutableArray<StartupModel> _pipelineSteps;

    public PipelineStepRegistrationTemplate(string namespacePrefix, ImmutableArray<StartupModel> pipelineSteps)
    {
        _namespacePrefix = namespacePrefix;
        _pipelineSteps = pipelineSteps;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Composition";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForAssembly("Composition", "PipelineSteps"),
            ToSourceText());
    }

    public override void RenderFile()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");
        AddUsing("Pragmatic.Composition.Abstractions");

        var ns = string.IsNullOrEmpty(_namespacePrefix) ? "Pragmatic" : _namespacePrefix;
        AppendNamespace(ns);
        AppendLine();

        Class("PragmaticDependencies", RenderClassBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true, Partial = true });
    }

    private void RenderClassBody()
    {
        XmlSummary("Registers all [StartupStep] classes as IStartupStep services.");

        var parameters = new List<MethodParameter>
        {
            new("IServiceCollection", "services")
        };

        Method("AddPipelineSteps", RenderMethodBody, "void", parameters,
            AccessModifier.Public,
            new MethodModifiers { IsStatic = true });
    }

    private void RenderMethodBody()
    {
        if (_pipelineSteps.Length == 0)
        {
            Comment("No [StartupStep] classes found");
            return;
        }

        Comment($"Register {_pipelineSteps.Length} startup step(s)");

        foreach (var step in _pipelineSteps.OrderBy(s => s.Priority).ThenBy(s => s.FullTypeName))
            AppendLine($"services.AddSingleton<IStartupStep, {step.FullTypeName}>();");
    }
}
