using System.Reflection;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Documents.Xlsx.Tests;

/// <summary>
/// Writing and reading an XLSX are synchronous CPU work. An <c>…Async</c> wrapper over
/// <c>Task.Run</c> frees no thread on a server and adds a hop, so none is offered: a caller that
/// wants the work off its thread says so itself.
/// </summary>
public class XlsxWorkIsOfferedSynchronouslyTests
{
    [Theory]
    [InlineData(typeof(XlsxRenderer))]
    [InlineData(typeof(XlsxReader))]
    public void PublicMethods_ReturnNoTask(Type type)
    {
        var asynchronous = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => typeof(Task).IsAssignableFrom(m.ReturnType))
            .Select(m => m.Name);

        asynchronous.Should().BeEmpty();
    }
}
