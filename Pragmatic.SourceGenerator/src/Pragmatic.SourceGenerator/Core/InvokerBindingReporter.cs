using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     <c>PRAG0730</c> and <c>PRAG0731</c> for each <c>[FromCurrentUser]</c> property of an operation,
///     <c>PRAG0734</c> for each <c>[FromClock]</c> one.
/// </summary>
/// <remarks>
///     Reported for a query, an action and a mutation from here, so the three agree on what is wrong —
///     and before any gate on the invoker's output: a binding that cannot be generated is an error
///     whether or not an invoker is written, and one reported only beside its output would go silent
///     with it.
/// </remarks>
internal static class InvokerBindingReporter
{
    public static void Report(
        SourceProductionContext context,
        string typeName,
        IEnumerable<CurrentUserBindingModel> currentUser,
        IEnumerable<ClockBindingModel> clock)
    {
        foreach (var binding in currentUser)
        {
            if (!binding.OnlyTheInvokerSetsIt)
                context.ReportDiagnostic(Diagnostic.Create(
                    CurrentUserBindingDiagnostics.BoundPropertyIsSettable,
                    binding.Location?.ToLocation(), typeName, binding.PropertyName));

            if (binding.Problem is { } problem)
                context.ReportDiagnostic(Diagnostic.Create(
                    CurrentUserBindingDiagnostics.BindingCannotBeGenerated,
                    binding.Location?.ToLocation(), typeName, binding.PropertyName, problem));
        }

        foreach (var binding in clock)
        {
            if (binding.Problem is { } problem)
                context.ReportDiagnostic(Diagnostic.Create(
                    ClockBindingDiagnostics.ClockBindingCannotBeGenerated,
                    binding.Location?.ToLocation(), typeName, binding.PropertyName, problem));
        }
    }
}
