// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>A problem with a <c>[LoggerMessage]</c> method, and the name it is about.</summary>
/// <param name="Kind">What is wrong.</param>
/// <param name="Subject">The placeholder, parameter or type concerned; the method name otherwise.</param>
internal sealed record LogCallSiteProblem(LogCallSiteProblemKind Kind, string Subject)
{
    /// <summary>Whether it stops the body from being generated.</summary>
    public bool IsBlocking => Kind != LogCallSiteProblemKind.ParameterNotInTemplate;
}
