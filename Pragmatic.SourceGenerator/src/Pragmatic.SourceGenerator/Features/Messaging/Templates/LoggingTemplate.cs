using System.Linq;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Messaging.Templates;

/// <summary>
///     A template whose generated type logs through its own <c>_logger</c> field.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The log methods are written out with a body, never as <c>[LoggerMessage] partial void</c>.
///         Microsoft's logging generator does not see the output of this generator, so such a method never
///         gets an implementation; a <c>partial void</c> without one is legal, and the compiler removes
///         every call to it, arguments included. That is how the handler pipeline and the saga
///         orchestrator once logged nothing without anything failing.
///     </para>
///     <para>
///         <c>LoggerMessage.Define</c> is what the logging generator would have emitted underneath: the
///         message template is parsed once into a static delegate, which checks <c>IsEnabled</c> itself and
///         neither boxes its arguments nor reflects.
///     </para>
/// </remarks>
internal abstract class LoggingTemplate : CSharpTemplate
{
    /// <summary>
    ///     Emits a static <c>LoggerMessage.Define</c> delegate and the private method that invokes it.
    /// </summary>
    /// <param name="name">The method's name, which the generated code calls.</param>
    /// <param name="level">A <c>LogLevel</c> member name.</param>
    /// <param name="message">The message template.</param>
    /// <param name="parameters">The method's parameters, in the order the call sites pass them.</param>
    /// <param name="messageOrder">
    ///     The parameter names in the order their placeholders appear in <paramref name="message" />:
    ///     the delegate binds by position, not by name.
    /// </param>
    /// <param name="exception">The name of a trailing exception parameter, if the method takes one.</param>
    protected void RenderLogMethod(
        string name,
        string level,
        string message,
        (string Type, string Name)[] parameters,
        string[] messageOrder,
        string? exception = null)
    {
        var types = messageOrder.Select(argument => parameters.First(p => p.Name == argument).Type).ToArray();
        var typeList = string.Join(", ", types);
        var field = "__" + char.ToLowerInvariant(name[0]) + name.Substring(1);
        var define = types.Length == 0 ? "LoggerMessage.Define" : $"LoggerMessage.Define<{typeList}>";
        var delegateArguments = types.Length == 0 ? "" : typeList + ", ";

        var signature = string.Join(", ", parameters.Select(p => $"{p.Type} {p.Name}"));
        if (exception is not null)
            signature += (signature.Length == 0 ? "" : ", ") + $"global::System.Exception {exception}";

        var callArguments = string.Concat(messageOrder.Select(argument => argument + ", "));

        AppendLine($"private static readonly global::System.Action<ILogger, {delegateArguments}global::System.Exception?> {field} =");
        AppendLine($"    {define}(LogLevel.{level}, new EventId(0, \"{name}\"), \"{message}\");");
        AppendLine();
        AppendLine($"private void {name}({signature})");
        AppendLine($"    => {field}(_logger, {callArguments}{exception ?? "null"});");
    }
}
