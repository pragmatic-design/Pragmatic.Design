using Pragmatic.SourceGen;

namespace Pragmatic.Client.SourceGenerator.Templates;

/// <summary>
///     Emits <c>Add{Boundary}Client(baseUrl, configure)</c>: the DI registration that binds the generated
///     interface to its HTTP implementation through <c>IHttpClientFactory</c>.
/// </summary>
internal sealed class ClientRegistrationTemplate(string clientNamespace, string boundary) : CSharpTemplate
{
    protected override string? GeneratorName => "Pragmatic.Client.SourceGenerator";

    protected override string? SourceInfo => $"API manifest for {boundary}";

    public override Artifact RenderOutput() =>
        new($"{boundary}ClientExtensions.g.cs", ToSourceText());

    protected override bool Validate() => boundary.Length > 0;

    public override void RenderFile()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");
        AppendNamespace(clientNamespace);
        AppendLine();

        Class($"{boundary}ClientExtensions", RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderBody()
    {
        XmlSummary($"Registers <see cref=\"I{boundary}Client\"/> against <paramref name=\"baseUrl\"/>.");
        XmlParam("services", "The service collection.");
        XmlParam("baseUrl", "Base address of the API.");
        XmlParam("configure", "Optional additional configuration of the underlying HttpClient.");

        Method(
            $"Add{boundary}Client",
            RenderRegistration,
            "IServiceCollection",
            parameters:
            [
                new MethodParameter("IServiceCollection", "services") { IsExtension = true },
                new MethodParameter("string", "baseUrl"),
                new MethodParameter("Action<HttpClient>", "configure", nullable: true) { DefaultValue = "null" }
            ],
            accessModifier: AccessModifier.Public,
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderRegistration()
    {
        // A lambda argument closes with "});", which Block() cannot express — its modifier is emitted
        // before the brace, not after it. Written out by hand, as the shared docs prescribe for this shape.
        AppendLine($"services.AddHttpClient<I{boundary}Client, {boundary}HttpClient>(client =>");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("client.BaseAddress = new Uri(baseUrl);");
        AppendLine("configure?.Invoke(client);");
        DecreaseIndent();
        AppendLine("});");
        AppendLine("return services;");
    }
}
