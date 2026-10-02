using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Pragmatic.Result.EntityFrameworkCore.Tests;

public class QueryableResultExtensionsTests : IDisposable
{
    private readonly TestDbContext _context;

    public QueryableResultExtensionsTests()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new TestDbContext(options);

        // Seed test data
        _context.TestEntities.AddRange(
            new TestEntity { Id = 1, Name = "Entity1" },
            new TestEntity { Id = 2, Name = "Entity2" },
            new TestEntity { Id = 3, Name = "Entity3" });
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    #region FirstOrDefaultAsResultAsync

    [Fact]
    public async Task FirstOrDefaultAsResultAsync_WithExistingEntity_ReturnsSuccess()
    {
        var result = await _context.TestEntities
            .Where(e => e.Id == 1)
            .FirstOrDefaultAsResultAsync("TestEntity").ConfigureAwait(true);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Entity1");
    }

    [Fact]
    public async Task FirstOrDefaultAsResultAsync_WithNoMatch_ReturnsNotFoundError()
    {
        var result = await _context.TestEntities
            .Where(e => e.Id == 999)
            .FirstOrDefaultAsResultAsync("TestEntity").ConfigureAwait(true);

        result.IsFailure.Should().BeTrue();
        result.Error.EntityType.Should().Be("TestEntity");
    }

    [Fact]
    public async Task FirstOrDefaultAsResultAsync_WithPredicate_ReturnsMatchingEntity()
    {
        var result = await _context.TestEntities
            .FirstOrDefaultAsResultAsync(e => e.Name == "Entity2", "TestEntity").ConfigureAwait(true);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(2);
    }

    [Fact]
    public async Task FirstOrDefaultAsResultAsync_WithPredicateNoMatch_ReturnsNotFoundError()
    {
        var result = await _context.TestEntities
            .FirstOrDefaultAsResultAsync(e => e.Name == "NonExistent", "TestEntity").ConfigureAwait(true);

        result.IsFailure.Should().BeTrue();
    }

    #endregion

    #region SingleOrDefaultAsResultAsync

    [Fact]
    public async Task SingleOrDefaultAsResultAsync_WithExistingEntity_ReturnsSuccess()
    {
        var result = await _context.TestEntities
            .Where(e => e.Id == 1)
            .SingleOrDefaultAsResultAsync("TestEntity").ConfigureAwait(true);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Entity1");
    }

    [Fact]
    public async Task SingleOrDefaultAsResultAsync_WithNoMatch_ReturnsNotFoundError()
    {
        var result = await _context.TestEntities
            .Where(e => e.Id == 999)
            .SingleOrDefaultAsResultAsync("TestEntity").ConfigureAwait(true);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task SingleOrDefaultAsResultAsync_WithMultipleMatches_ThrowsException()
    {
        // This should throw because SingleOrDefault expects 0 or 1 elements
        Func<Task> act = async () => await _context.TestEntities
            .SingleOrDefaultAsResultAsync("TestEntity").ConfigureAwait(true);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task SingleOrDefaultAsResultAsync_WithPredicate_ReturnsMatchingEntity()
    {
        var result = await _context.TestEntities
            .SingleOrDefaultAsResultAsync(e => e.Name == "Entity3", "TestEntity").ConfigureAwait(true);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(3);
    }

    #endregion

    #region FindAsResultAsync

    [Fact]
    public async Task FindAsResultAsync_WithExistingId_ReturnsSuccess()
    {
        var result = await _context.TestEntities.FindAsResultAsync(2, "TestEntity").ConfigureAwait(true);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Entity2");
    }

    [Fact]
    public async Task FindAsResultAsync_WithNonExistingId_ReturnsNotFoundError()
    {
        var result = await _context.TestEntities.FindAsResultAsync(999, "TestEntity").ConfigureAwait(true);

        result.IsFailure.Should().BeTrue();
        result.Error.EntityType.Should().Be("TestEntity");
        result.Error.EntityId.Should().Be("999");
    }

    [Fact]
    public async Task FindAsResultAsync_WithParamsOverload_ReturnsSuccess()
    {
        var result = await _context.TestEntities
            .FindAsResultAsync("TestEntity", CancellationToken.None, 1).ConfigureAwait(true);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(1);
    }

    [Fact]
    public async Task FindAsResultAsync_WithParamsOverload_NotFound_ReturnsNotFoundError()
    {
        var result = await _context.TestEntities
            .FindAsResultAsync("TestEntity", CancellationToken.None, 999).ConfigureAwait(true);

        result.IsFailure.Should().BeTrue();
        result.Error.EntityType.Should().Be("TestEntity");
    }

    #endregion

    #region Error Details

    [Fact]
    public async Task NotFoundError_ContainsEntityTypeName()
    {
        var result = await _context.TestEntities
            .Where(e => e.Id == 999)
            .FirstOrDefaultAsResultAsync("TestEntity").ConfigureAwait(true);

        result.IsFailure.Should().BeTrue();
        result.Error.EntityType.Should().Be("TestEntity");
        result.Error.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task FindAsResultAsync_NotFoundError_ContainsEntityId()
    {
        var result = await _context.TestEntities.FindAsResultAsync(42, "TestEntity").ConfigureAwait(true);

        result.IsFailure.Should().BeTrue();
        result.Error.EntityId.Should().Be("42");
    }

    #endregion
}
