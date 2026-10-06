using System.Collections.Generic;
using Pragmatic.SourceGenerator.Features.Logging.Templates;
using Pragmatic.SourceGenerator.Features.Logging.Transforms;

namespace Pragmatic.SourceGenerator.Features.Messaging.Templates;

/// <summary>
///     A template whose generated type logs through its own <c>_logger</c> field.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The log methods are written out with a body, never as <c>[LoggerMessage] partial void</c>. A
///         generator never sees another generator's output, its own included, so such a method would get
///         no implementation; a <c>partial void</c> without one is legal, and the compiler removes every
///         call to it, arguments included. That is how the handler pipeline and the saga orchestrator once
///         logged nothing without anything failing.
///     </para>
///     <para>
///         Each method is a Pragmatic call site, written in the same pass as the type around it: a state
///         struct that renders its message as UTF-8 and writes its properties without boxing, reached by
///         the Pragmatic providers without an entry.
///     </para>
/// </remarks>
internal abstract class LoggingTemplate : LogCallSiteTemplateBase
{
    private readonly HashSet<string> _stateNames = new(System.StringComparer.Ordinal);

    /// <summary>Emits a private log method and the state it logs.</summary>
    /// <param name="name">The method's name, which the generated code calls.</param>
    /// <param name="level">A <c>LogLevel</c> member name.</param>
    /// <param name="message">The message template; its placeholders name the parameters, ignoring case.</param>
    /// <param name="parameters">The method's parameters, in the order the call sites pass them.</param>
    /// <param name="exception">The name of a trailing exception parameter, if the method takes one.</param>
    protected void RenderLogMethod(
        string name,
        string level,
        string message,
        (string Type, string Name)[] parameters,
        string? exception = null)
    {
        var callSite = GeneratedLogCallSite.Create(name, "private", "_logger", level, message, parameters, exception);
        RenderCallSite(callSite, StateName(callSite, _stateNames));
    }
}
