using Pragmatic.Testing.Assertions;
using Pragmatic.Result.Extensions;
using Xunit;

namespace Pragmatic.Result.Tests.Unit;

public class PartitionExtensionsTests
{
    // =========================================================================
    // GetSuccesses
    // =========================================================================

    [Fact]
    public void GetSuccesses_AllSuccess_ReturnsAllValues()
    {
        var results = new[]
        {
            Result<int, StringError>.Success(1),
            Result<int, StringError>.Success(2),
            Result<int, StringError>.Success(3)
        };

        var successes = results.GetSuccesses().ToList();

        successes.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void GetSuccesses_AllFailure_ReturnsEmpty()
    {
        var results = new[]
        {
            Result<int, StringError>.Failure(new StringError("a")),
            Result<int, StringError>.Failure(new StringError("b"))
        };

        var successes = results.GetSuccesses().ToList();

        successes.Should().BeEmpty();
    }

    [Fact]
    public void GetSuccesses_Mixed_ReturnsOnlyValues()
    {
        var results = new[]
        {
            Result<int, StringError>.Success(1),
            Result<int, StringError>.Failure(new StringError("err")),
            Result<int, StringError>.Success(3)
        };

        var successes = results.GetSuccesses().ToList();

        successes.Should().Equal(1, 3);
    }

    [Fact]
    public void GetSuccesses_Empty_ReturnsEmpty()
    {
        var results = Array.Empty<Result<int, StringError>>();

        results.GetSuccesses().Should().BeEmpty();
    }

    [Fact]
    public void GetSuccesses_NullInput_ThrowsArgumentNull()
    {
        IEnumerable<Result<int, StringError>> results = null!;

        var act = () => results.GetSuccesses().ToList();

        act.Should().Throw<ArgumentNullException>();
    }

    // =========================================================================
    // GetFailures
    // =========================================================================

    [Fact]
    public void GetFailures_AllSuccess_ReturnsEmpty()
    {
        var results = new[]
        {
            Result<int, StringError>.Success(1),
            Result<int, StringError>.Success(2)
        };

        var failures = results.GetFailures().ToList();

        failures.Should().BeEmpty();
    }

    [Fact]
    public void GetFailures_AllFailure_ReturnsAllErrors()
    {
        var results = new[]
        {
            Result<int, StringError>.Failure(new StringError("a")),
            Result<int, StringError>.Failure(new StringError("b"))
        };

        var failures = results.GetFailures().ToList();

        failures.Should().HaveCount(2);
        failures[0].Message.Should().Be("a");
        failures[1].Message.Should().Be("b");
    }

    [Fact]
    public void GetFailures_Mixed_ReturnsOnlyErrors()
    {
        var results = new[]
        {
            Result<int, StringError>.Success(1),
            Result<int, StringError>.Failure(new StringError("err")),
            Result<int, StringError>.Success(3)
        };

        var failures = results.GetFailures().ToList();

        failures.Should().ContainSingle().Which.Message.Should().Be("err");
    }

    [Fact]
    public void GetFailures_NullInput_ThrowsArgumentNull()
    {
        IEnumerable<Result<int, StringError>> results = null!;

        var act = () => results.GetFailures().ToList();

        act.Should().Throw<ArgumentNullException>();
    }

    // =========================================================================
    // Partition
    // =========================================================================

    [Fact]
    public void Partition_AllSuccess_ReturnsAllInSuccesses()
    {
        var results = new[]
        {
            Result<string, StringError>.Success("a"),
            Result<string, StringError>.Success("b")
        };

        var (successes, failures) = results.Partition();

        successes.Should().Equal("a", "b");
        failures.Should().BeEmpty();
    }

    [Fact]
    public void Partition_AllFailure_ReturnsAllInFailures()
    {
        var results = new[]
        {
            Result<string, StringError>.Failure(new StringError("x")),
            Result<string, StringError>.Failure(new StringError("y"))
        };

        var (successes, failures) = results.Partition();

        successes.Should().BeEmpty();
        failures.Should().HaveCount(2);
    }

    [Fact]
    public void Partition_Mixed_SplitsCorrectly()
    {
        var results = new[]
        {
            Result<int, StringError>.Success(1),
            Result<int, StringError>.Failure(new StringError("err1")),
            Result<int, StringError>.Success(2),
            Result<int, StringError>.Failure(new StringError("err2")),
            Result<int, StringError>.Success(3)
        };

        var (successes, failures) = results.Partition();

        successes.Should().Equal(1, 2, 3);
        failures.Should().HaveCount(2);
        failures[0].Message.Should().Be("err1");
        failures[1].Message.Should().Be("err2");
    }

    [Fact]
    public void Partition_Empty_ReturnsBothEmpty()
    {
        var results = Array.Empty<Result<int, StringError>>();

        var (successes, failures) = results.Partition();

        successes.Should().BeEmpty();
        failures.Should().BeEmpty();
    }

    [Fact]
    public void Partition_NullInput_ThrowsArgumentNull()
    {
        IEnumerable<Result<int, StringError>> results = null!;

        var act = () => results.Partition();

        act.Should().Throw<ArgumentNullException>();
    }
}
