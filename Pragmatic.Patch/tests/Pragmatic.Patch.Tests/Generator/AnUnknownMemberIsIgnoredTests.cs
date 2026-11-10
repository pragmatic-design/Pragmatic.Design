using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Patch.Tests.Generator;

/// <summary>
///     A patch body naming a member the patch does not declare is read, and the member ignored, however the body arrives (#631).
/// </summary>
/// <remarks>
///     <para>
///         The generated converter skipped an unknown member with <c>reader.Skip()</c>, which throws on a reader that is not on the final
///         block. A body read from a stream (<c>ReadFromJsonAsync</c>, which is how an endpoint reads it) reaches the converter in
///         segments, so the same body that a string deserializes was a 400 over HTTP: <c>{"id": …}</c> on a patch without <c>Id</c>.
///     </para>
///     <para>
///         Measured on the generated converter, compiled and loaded, through <see cref="JsonSerializer.DeserializeAsync(Stream, Type, JsonSerializerOptions, CancellationToken)" />
///         with a one-byte buffer.
///     </para>
/// </remarks>
public class AnUnknownMemberIsIgnoredTests : PatchGeneratorTestBase
{
    private const string Source = """
        using Pragmatic.Patch.Attributes;

        namespace TestApp;

        public class Story
        {
            public string Title { get; set; } = "";
            public string? Note { get; set; }
        }

        [GeneratePatch<Story>]
        public partial record StoryPatch;
        """;

    private const string Body = """{"id":"3f2c1d9e-0000-0000-0000-000000000001","extra":{"nested":[1,2,{"deep":true}]},"note":"read"}""";

    [Fact]
    public async Task AnUnknownMember_InABodyReadInSegments_IsIgnored()
    {
        var note = await ReadNoteAsync(Body, bufferSize: 1);

        note.Should().Be("read", "the members after the unknown ones are still read");
    }

    /// <summary>
    ///     The way ASP.NET Core reads a request body on .NET 10: through a <see cref="System.IO.Pipelines.PipeReader" />. Here the
    ///     converter is handed a reader that is not on the final block even with the value buffered, and <c>Skip</c> throws.
    /// </summary>
    [Fact]
    public async Task AnUnknownMember_InABodyReadFromAPipe_IsIgnored()
    {
        var note = await ReadNoteAsync(Body, bufferSize: 1, throughAPipe: true);

        note.Should().Be("read", "the body an endpoint reads is the body a client sent");
    }

    /// <summary>The control: the whole body at once, the shape the converter was tested with, read before as now.</summary>
    [Fact]
    public async Task AnUnknownMember_InABodyReadAtOnce_IsIgnored()
    {
        var note = await ReadNoteAsync(Body, bufferSize: 64 * 1024);

        note.Should().Be("read");
    }

    [Fact]
    public void TheConverter_SkipsWithoutAssumingTheFinalBlock()
    {
        var converter = GetGeneratedSource(RunGenerator(Source), "StoryPatch.JsonConverter")!;

        converter.Should().NotContain("reader.Skip();", "Skip throws on a reader that is not on the final block");
        converter.Should().Contain("reader.TrySkip()");
    }

    private static async Task<string?> ReadNoteAsync(string body, int bufferSize, bool throughAPipe = false)
    {
        var result = RunGenerator(Source);
        HasCompilationErrors(result).Should().BeFalse();

        using var image = new MemoryStream();
        var emitted = result.OutputCompilation.Emit(image);
        emitted.Success.Should().BeTrue(string.Join("\n", emitted.Diagnostics));

        var loadContext = new AssemblyLoadContext("patch-unknown-member", isCollectible: true);
        try
        {
            image.Position = 0;
            var patchType = loadContext.LoadFromStream(image).GetType("TestApp.StoryPatch", throwOnError: true)!;

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
            var options = new JsonSerializerOptions { DefaultBufferSize = bufferSize };
            var patch = throughAPipe
                ? await JsonSerializer.DeserializeAsync(
                        System.IO.Pipelines.PipeReader.Create(stream, new System.IO.Pipelines.StreamPipeReaderOptions(bufferSize: bufferSize, minimumReadSize: 1)),
                        patchType, options)
                    .ConfigureAwait(false)
                : await JsonSerializer.DeserializeAsync(stream, patchType, options).ConfigureAwait(false);

            var note = patchType.GetProperty("Note", BindingFlags.Public | BindingFlags.Instance)!.GetValue(patch)!;
            return (string?)note.GetType().GetProperty("Value")!.GetValue(note);
        }
        finally
        {
            loadContext.Unload();
        }
    }
}
