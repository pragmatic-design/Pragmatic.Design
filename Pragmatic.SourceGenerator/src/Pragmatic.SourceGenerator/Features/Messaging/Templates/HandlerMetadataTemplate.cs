using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Composition;
using Pragmatic.SourceGenerator.Features.Messaging.Models;

namespace Pragmatic.SourceGenerator.Features.Messaging.Templates;

/// <summary>
///     Generates <c>_Metadata.MessageHandlers.g.cs</c> — assembly metadata
///     for Composition host aggregation.
/// </summary>
internal sealed class HandlerMetadataTemplate : CSharpTemplate
{
    private readonly ImmutableArray<MessageHandlerModel> _handlers;
    private readonly string _registrationNamespace;

    /// <param name="handlers">The message handlers, listed in the metadata for the host's topology.</param>
    /// <param name="registrationNamespace">
    ///     Where the registration is declared — the caller's answer, the same one the registration file
    ///     was written with, so the host calls a class that is there.
    /// </param>
    public HandlerMetadataTemplate(ImmutableArray<MessageHandlerModel> handlers, string registrationNamespace)
    {
        _handlers = handlers.IsDefault ? ImmutableArray<MessageHandlerModel>.Empty : handlers;
        _registrationNamespace = registrationNamespace;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Messaging";
    protected override string? TriggerInfo => $"{_handlers.Length} message handler(s)";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("MessageHandlers"),
        ToSourceText());

    /// <summary>
    ///     ⚠️ Emitted whenever the registration is, for an assembly with <b>no</b> handlers too — the
    ///     caller decides that with the registration's own predicate. A type registry alone, a request
    ///     handler alone or a middleware alone each need it: the host calls the registration only if the
    ///     assembly declares it here.
    /// </summary>
    protected override bool Validate() => _registrationNamespace.Length > 0;

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");
        AppendLine();

        // Build JSON metadata
        AppendLine($"[assembly: PragmaticMetadata(MetadataCategory.MessageHandlers, \"1.0\", \"\"\"");

        AppendLine("{");
        IncreaseIndent();
        AppendLine("\"generator\": \"Pragmatic.SourceGenerator/Messaging\",");
        AppendLine($"\"registrationMethod\": \"{GeneratedRegistrationNames.MessageHandlersFqn(_registrationNamespace)}\",");
        AppendLine("\"data\": {");
        IncreaseIndent();
        AppendLine($"\"handlerCount\": {_handlers.Length},");
        AppendLine("\"handlers\": [");
        IncreaseIndent();

        for (var i = 0; i < _handlers.Length; i++)
        {
            var handler = _handlers[i];
            var comma = i < _handlers.Length - 1 ? "," : "";
            var fqn = string.IsNullOrEmpty(handler.Namespace)
                ? handler.TypeName
                : $"{handler.Namespace}.{handler.TypeName}";

            AppendLine($"{{ \"type\": \"{fqn}\", \"messageType\": \"{handler.MessageTypeFqn}\", \"order\": {handler.Order} }}{comma}");
        }

        DecreaseIndent();
        AppendLine("]");
        DecreaseIndent();
        AppendLine("}");
        DecreaseIndent();
        AppendLine("}");
        AppendLine("\"\"\")]");
    }
}
