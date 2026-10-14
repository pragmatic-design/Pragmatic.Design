using System.Reflection;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Documents.Docx.Tests;

/// <summary>
/// Rendering a DOCX is synchronous CPU work. An <c>…Async</c> wrapper over <c>Task.Run</c> frees no
/// thread on a server and adds a hop, so none is offered: a caller that wants the work off its
/// thread says so itself.
/// </summary>
public class DocxWorkIsOfferedSynchronouslyTests
{
    [Fact]
    public void PublicMethods_ReturnNoTask()
    {
        var asynchronous = typeof(DocxRenderer).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => typeof(Task).IsAssignableFrom(m.ReturnType))
            .Select(m => m.Name);

        asynchronous.Should().BeEmpty();
    }
}
