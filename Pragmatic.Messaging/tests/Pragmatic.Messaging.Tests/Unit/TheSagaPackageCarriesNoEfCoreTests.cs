using Pragmatic.Messaging.Saga;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Tests.Unit;

/// <summary>
///     The saga package is the engine-neutral half (#546): its contracts, model and in-memory repository. An application on the Sql or documents
///     engine references it for its sagas, so it must not bring EF Core with it; the EF Core repository and mapping are Pragmatic.Messaging.EFCore's.
/// </summary>
public class TheSagaPackageCarriesNoEfCoreTests
{
    [Fact]
    public void The_saga_assembly_references_no_EF_Core_assembly()
    {
        var references = typeof(ISaga<>).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").ToList();

        references.Where(name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)).Should().BeEmpty();
    }
}
