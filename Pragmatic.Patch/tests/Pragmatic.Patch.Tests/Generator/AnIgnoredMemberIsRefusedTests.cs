using System.IO;
using System.IO.Pipelines;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Patch.Tests.Generator;

/// <summary>
///     A body naming a property <c>[PatchIgnore]</c> keeps out of the patch is refused, by name; a member the entity does not have at
///     all is ignored.
/// </summary>
/// <remarks>
///     <para>
///         The two used to look the same, and both were a 400: the converter passed over every member it did not declare with
///         <c>reader.Skip()</c>, which threw on the reader an endpoint hands it (#631). Making unknown members readable made the excluded
///         ones readable too, and a body renaming a <c>[LogicKey]</c> went on to apply the rest of itself while the rename vanished
///         without a word.
///     </para>
///     <para>
///         They are not the same. An <c>id</c> the front end repeats is noise. A property the author excluded because the domain forbids
///         correcting it is a request the endpoint will not carry out, and saying nothing about it tells the caller it was done.
///     </para>
///     <para>
///         Measured on the generated converter, compiled and loaded, read through a <see cref="PipeReader" /> as ASP.NET Core reads a
///         request body.
///     </para>
/// </remarks>
public class AnIgnoredMemberIsRefusedTests : PatchGeneratorTestBase
{
    private const string Source = """
        using Pragmatic.Patch.Attributes;

        namespace TestApp;

        public class Term
        {
            public string Word { get; set; } = "";
            public string? Definition { get; set; }
        }

        [GeneratePatch<Term>]
        [PatchIgnore(nameof(Term.Word))]
        public partial record CorrectTermPatch;
        """;

    [Theory]
    [InlineData("word")]
    [InlineData("Word")]
    public async Task ABodyNamingAnIgnoredProperty_IsRefused_NamingIt(string sent)
    {
        var read = await ReadAsync($$"""{"{{sent}}":"renamed","definition":"kept"}""");

        read.Error.Should().NotBeNull("the domain forbids correcting the word, so a body asking to is not carried out");
        read.Error!.Message.Should().Contain($"'{sent}'", "the caller is told which member of their body was refused, as they spelt it");
        read.Error.Message.Should().Contain("Term.Word");
    }

    /// <summary>The control: a member the entity does not have is ignored, and the rest of the body read.</summary>
    [Fact]
    public async Task ABodyNamingAnUnknownMember_IsRead()
    {
        var read = await ReadAsync("""{"id":"3f2c1d9e-0000-0000-0000-000000000001","definition":"kept"}""");

        read.Error.Should().BeNull();
        read.Definition.Should().Be("kept");
    }

    private static async Task<(JsonException? Error, string? Definition)> ReadAsync(string body)
    {
        var result = RunGenerator(Source);
        HasCompilationErrors(result).Should().BeFalse();

        using var image = new MemoryStream();
        var emitted = result.OutputCompilation.Emit(image);
        emitted.Success.Should().BeTrue(string.Join("\n", emitted.Diagnostics));

        var loadContext = new AssemblyLoadContext("patch-ignored-member", isCollectible: true);
        try
        {
            image.Position = 0;
            var patchType = loadContext.LoadFromStream(image).GetType("TestApp.CorrectTermPatch", throwOnError: true)!;

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
            var pipe = PipeReader.Create(stream, new StreamPipeReaderOptions(bufferSize: 1, minimumReadSize: 1));

            object? patch;
            try
            {
                patch = await JsonSerializer.DeserializeAsync(pipe, patchType, new JsonSerializerOptions()).ConfigureAwait(false);
            }
            catch (JsonException refused)
            {
                return (refused, null);
            }

            var definition = patchType.GetProperty("Definition", BindingFlags.Public | BindingFlags.Instance)!.GetValue(patch)!;
            return (null, (string?)definition.GetType().GetProperty("Value")!.GetValue(definition));
        }
        finally
        {
            loadContext.Unload();
        }
    }
}
