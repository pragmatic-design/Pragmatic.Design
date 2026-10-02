using Pragmatic.Testing.Assertions;
using Pragmatic.Endpoints.Processors;
using Pragmatic.Result;
using Pragmatic.Result.Http;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

public class PreProcessorResultTests
{
    [Fact]
    public void Continue_ShouldContinue_ReturnsTrue()
    {
        var result = PreProcessorResult.Continue();

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public void Continue_Error_IsNull()
    {
        var result = PreProcessorResult.Continue();

        result.Error.Should().BeNull();
    }

    [Fact]
    public void Fail_ShouldContinue_ReturnsFalse()
    {
        var error = new TestError();

        var result = PreProcessorResult.Fail(error);

        result.ShouldContinue.Should().BeFalse();
    }

    [Fact]
    public void Fail_Error_IsNotNull()
    {
        var error = new TestError();

        var result = PreProcessorResult.Fail(error);

        result.Error.Should().NotBeNull();
    }

    [Fact]
    public void Fail_Error_MatchesProvidedError()
    {
        var error = new TestError();

        var result = PreProcessorResult.Fail(error);

        result.Error.Should().BeSameAs(error);
    }

    [Fact]
    public void NotFound_WithResourceType_CreatesNotFoundError()
    {
        var result = PreProcessorResult.NotFound("Order");

        result.Error.Should().BeOfType<NotFoundError>();
        var notFound = (NotFoundError)result.Error!;
        notFound.EntityType.Should().Be("Order");
    }

    [Fact]
    public void NotFound_WithId_IncludesIdInError()
    {
        var result = PreProcessorResult.NotFound("Order", "123");

        var notFound = (NotFoundError)result.Error!;
        notFound.EntityId.Should().Be("123");
    }

    [Fact]
    public void NotFound_WithoutId_ErrorHasNullId()
    {
        var result = PreProcessorResult.NotFound("Order");

        var notFound = (NotFoundError)result.Error!;
        notFound.EntityId.Should().BeNull();
    }

    [Fact]
    public void NotFound_WithEntityName_SetsEntityType()
    {
        var result = PreProcessorResult.NotFound("TestEntity");

        var notFound = (NotFoundError)result.Error!;
        notFound.EntityType.Should().Be("TestEntity");
    }

    [Fact]
    public void NotFound_WithEntityNameAndId_ShouldContinue_ReturnsFalse()
    {
        var result = PreProcessorResult.NotFound("TestEntity", "abc");

        result.ShouldContinue.Should().BeFalse();
    }

    [Fact]
    public void NotFound_WithEntityNameAndId_IncludesId()
    {
        var result = PreProcessorResult.NotFound("TestEntity", "abc");

        var notFound = (NotFoundError)result.Error!;
        notFound.EntityId.Should().Be("abc");
    }

    private sealed record TestError : Error
    {
        public override string Code => "TEST_ERROR";
        public override int StatusCode => 400;
    }

    private sealed class TestEntity;
}
