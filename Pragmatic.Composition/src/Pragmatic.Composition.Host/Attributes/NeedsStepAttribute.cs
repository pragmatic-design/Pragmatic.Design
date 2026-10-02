using Pragmatic.Composition.Abstractions;

namespace Pragmatic.Composition.Attributes;

/// <summary>
///     Declares that a module requires a specific <see cref="IStartupStep" /> in the pipeline.
///     Applied to classes decorated with <see cref="ModuleAttribute" />.
/// </summary>
/// <typeparam name="TStep">The startup step type required by this module.</typeparam>
/// <remarks>
///     <para>
///         The source generator aggregates all <c>[NeedsStep&lt;T&gt;]</c> declarations from all modules,
///         deduplicates by type, orders by <see cref="IStartupStep.Order" />, and generates the pipeline.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class NeedsStepAttribute<TStep> : Attribute
    where TStep : IStartupStep
{
}
