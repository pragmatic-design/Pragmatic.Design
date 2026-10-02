using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Implements <c>RegisterRollUpRules</c>, the partial hook the repository registration declares, by
///     delegating to the public entry point of <see cref="RollUpRegistrationTemplate"/>.
/// </summary>
/// <remarks>
///     The hook is reachable only from <c>AddPragmaticPersistenceRepositories&lt;TDbContext&gt;()</c>,
///     which an application wiring persistence by hand calls itself and the Pragmatic host path does
///     not. Keeping it means both wirings register the same rules; the sentinel in the entry point is
///     what stops an application that does both from counting every delta twice. It is a separate
///     artifact because a file carries one namespace declaration, and the two live in different ones.
/// </remarks>
internal sealed class RollUpHookTemplate : CSharpTemplate
{
    private const string ServiceCollection =
        "global::Microsoft.Extensions.DependencyInjection.IServiceCollection";

    private readonly string _rootNamespace;

    public RollUpHookTemplate(string rootNamespace) => _rootNamespace = rootNamespace;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";

    public override Artifact RenderOutput() => new("_Infra.RollUp.Hook.g.cs", ToSourceText());

    public override void RenderFile()
    {
        AppendLine("namespace Pragmatic.Persistence.Generated;");
        AppendLine();
        AppendLine("public static partial class PragmaticPersistenceRegistration");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"static partial void RegisterRollUpRules({ServiceCollection} services)");
        IncreaseIndent();
        AppendLine($"=> global::{GeneratedRegistrationNames.RollUpRulesFqn(_rootNamespace)}(services);");
        DecreaseIndent();
        DecreaseIndent();
        AppendLine("}");
    }
}
