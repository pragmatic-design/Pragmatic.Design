using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Diagnostics;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Actions.Templates;

namespace Pragmatic.SourceGenerator.Features.Actions;

/// <summary>
///     What the invoker writes from who is calling and when — the reports, and the <c>[LoadCurrentUser]</c>
///     field.
/// </summary>
internal static partial class ActionsFeature
{
    /// <summary>
    ///     PRAG0730/0731/0734 for the bound properties, as on a query; PRAG0451 for a
    ///     <c>[LoadCurrentUser]</c> that cannot be generated; PRAG0452 for a <c>ValidateLoaded</c> the invoker
    ///     would not call.
    /// </summary>
    private static void ReportBindings(
        SourceProductionContext context, string typeName, InvokerBindingsModel bindings,
        LoadedValidationModel loadedValidation)
    {
        InvokerBindingReporter.Report(context, typeName, bindings.CurrentUser, bindings.Clock);

        if (bindings.CurrentUserLoad is { Problem: { } problem } load)
            context.ReportDiagnostic(Diagnostic.Create(
                ActionsDiagnostics.CurrentUserCannotBeLoaded, load.Location?.ToLocation(), typeName, problem));

        foreach (var name in loadedValidation.Misshapen)
            context.ReportDiagnostic(Diagnostic.Create(
                ActionsDiagnostics.LoadedValidationMisshapen, loadedValidation.Location?.ToLocation(), typeName, name));
    }

    /// <summary>The field <c>[LoadCurrentUser]</c> promises, and the setter the invoker fills it through.</summary>
    private static void GenerateCurrentUserField(
        SourceProductionContext context, string typeName, string @namespace, string accessibility,
        InvokerBindingsModel bindings)
    {
        if (!bindings.LoadsTheUser)
            return;

        var artifact = new LoadCurrentUserTemplate(typeName, @namespace, accessibility, bindings.CurrentUserLoad!)
            .RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }
}
