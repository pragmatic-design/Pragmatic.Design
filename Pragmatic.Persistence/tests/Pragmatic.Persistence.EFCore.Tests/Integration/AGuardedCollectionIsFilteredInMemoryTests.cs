using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary><see cref="AGuardedCollectionIsFilteredWhateverReadsIt" /> on EF's in-memory provider.</summary>
public sealed class AGuardedCollectionIsFilteredInMemoryTests : AGuardedCollectionIsFilteredWhateverReadsIt
{
    protected override TestDbContext CreateContext()
        => new(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"GuardedCollection_{Guid.NewGuid():N}")
            .Options);
}
