using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Actions.Extensions;
using Pragmatic.Result;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Pragmatic.Validation;
using Pragmatic.Validation.Types;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     A <c>VoidDomainAction</c>'s inputs are validated before its body runs, as a typed action's are (#627).
/// </summary>
/// <remarks>
///     <para>
///         <c>ValidationFilter</c> implemented the typed hook only, and the void one kept <c>IActionFilter</c>'s default, success: the
///         generated validator of a void action existed and nobody asked it. An application wrote a colour <c>"red"</c> into a
///         <c>[Regex]</c> hex field of a void action and was answered 204.
///     </para>
///     <para>
///         Measured on the generated code: the action, its validator and its invoker are the generator's, compiled and loaded, and the
///         filters are the ones <c>AddPragmaticActions</c> registers.
///     </para>
/// </remarks>
public class AVoidActionValidatesItsInputsTests : ActionsGeneratorTestBase
{
    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Result;
        using Pragmatic.Validation.Attributes;

        namespace TestApp.Branding;

        [DomainAction]
        public partial class PaintAction : VoidDomainAction
        {
            [Regex("^#[0-9a-fA-F]{6}$")]
            public string Colour { get; init; } = "";

            public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
            {
                Harness.Ran++;
                return Task.FromResult(VoidResult<IError>.Success());
            }
        }

        public static class Harness
        {
            public static int Ran;

            public static async Task<(IError? Error, int Ran)> PaintAsync(IServiceProvider services, string colour)
            {
                Ran = 0;
                var result = await new PaintAction.Invoker(services).InvokeAsync(new PaintAction { Colour = colour });
                return (result.IsFailure ? result.Error : null, Ran);
            }
        }
        """;

    [Fact]
    public async Task AnInputThatBreaksItsRule_IsRefused_AndTheBodyDoesNotRun()
    {
        var (error, ran) = await PaintAsync("red");

        error.Should().BeOfType<ValidationError>();
        ((ValidationError)error!).Issues.Select(issue => issue.MessageKey).Should().Contain("validation.regex");
        ran.Should().Be(0, "the body is the write, and an input its rules refuse never reaches it");
    }

    /// <summary>The control: a colour the rule accepts runs the body, so the refusal above is the rule's and not the pipeline's.</summary>
    [Fact]
    public async Task AnInputThatKeepsItsRule_RunsTheBody()
    {
        var (error, ran) = await PaintAsync("#ff0000");

        error.Should().BeNull();
        ran.Should().Be(1);
    }

    private static async Task<(IError? Error, int Ran)> PaintAsync(string colour)
    {
        MetadataReference[] references =
        [
            .. GetActionsAndEntityReferences(),
            GeneratorTestHelper.FromType<Pragmatic.Validation.Attributes.RegexAttribute>(),
            // The generated [Regex] check calls Regex.IsMatch, which lives in its own assembly.
            GeneratorTestHelper.FromTypeAssembly(typeof(System.Text.RegularExpressions.Regex))
        ];
        var result = GeneratorTestHelper.RunGenerator<Pragmatic.SourceGenerator.PragmaticSourceGenerator>(Source, references);
        GetCompilationErrors(result).Select(e => e.ToString()).Should().BeEmpty();
        GetGeneratedSourcesAsDictionary(result).Keys.Should().Contain(hint => hint.Contains("PaintAction") && hint.Contains("Valid"),
            "the void action has a generated validator: what is measured is whether anybody asks it");

        using var image = new MemoryStream();
        var emitted = result.OutputCompilation.Emit(image);
        emitted.Success.Should().BeTrue(string.Join("\n", emitted.Diagnostics));

        var loadContext = new AssemblyLoadContext("void-action-validation", isCollectible: true);
        try
        {
            image.Position = 0;
            var harness = loadContext.LoadFromStream(image).GetType("TestApp.Branding.Harness", throwOnError: true)!;

            var services = new ServiceCollection();
            services.AddSingleton(Options.Create(new ValidationOptions()));
            services.AddPragmaticActions(options =>
            {
                // The subject is validation: no caller, no permission catalogue and no policy registry here.
                options.EnablePermissionFilter = false;
                options.EnableResourceAuthorizationFilter = false;
                options.EnablePolicyFilter = false;
            });
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            var run = (Task<(IError?, int)>)harness.GetMethod("PaintAsync", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [scope.ServiceProvider, colour])!;
            return await run.ConfigureAwait(false);
        }
        finally
        {
            loadContext.Unload();
        }
    }
}
