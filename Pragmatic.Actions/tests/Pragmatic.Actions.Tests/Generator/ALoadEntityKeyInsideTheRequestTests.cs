using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     <c>[LoadEntity]</c> can name a key that lives inside the operation's request, through a dotted path.
/// </summary>
/// <remarks>
///     The key was looked up with <c>symbol.GetMembers()</c>, so it had to be a property of the
///     operation itself. An operation that carries its input in a nested request — the shape the framework
///     recommends — could not preload anything, and the Showcase's <c>CreateReservationAction</c> read the
///     room type twice per request: once in its validator, to refuse an unknown one, and once in the body.
/// </remarks>
public class ALoadEntityKeyInsideTheRequestTests : ActionsGeneratorTestBase
{
    private const string Header = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;

        namespace TestApp.Booking;

        public partial class RoomType : IEntity
        {
            public Guid PersistenceId { get; set; }
        }

        public sealed record BookRequest(Guid RoomTypeId, string Note);

        """;

    private const string Nested = Header + """
        [DomainAction]
        [LoadEntity<RoomType>("Request.RoomTypeId")]
        public partial class BookAction : DomainAction<bool>
        {
            public required BookRequest Request { get; init; }

            public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult<Result<bool, IError>>(_roomType is not null);
        }
        """;

    [Fact]
    public void ADottedPath_LoadsTheRowTheRequestNames()
    {
        var result = RunGeneratorWithEntities(Nested);

        GetGeneratedSource(result, "BookAction.LoadEntity").Should()
            .NotBeNull().And.Subject.As<string>().Should()
            .Contain("global::TestApp.Booking.RoomType _roomType", "the row is preloaded into a field");

        var invoker = GetGeneratedSource(result, "BookAction.Invoker");
        invoker.Should().NotBeNull();
        invoker!.Should().Contain("action.Request.RoomTypeId", "the key is read through the request");
    }

    /// <summary>
    ///     The leaf is checked like any other key: a path whose last segment is of the wrong type is
    ///     PRAG0411, not a CS1503 inside generated code.
    /// </summary>
    [Fact]
    public void ADottedPathOfTheWrongType_IsReported()
    {
        var result = RunGeneratorWithEntities(Header + """
            [DomainAction]
            [LoadEntity<RoomType>("Request.Note")]
            public partial class BookAction : DomainAction<bool>
            {
                public required BookRequest Request { get; init; }

                public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<bool, IError>>(true);
            }
            """);

        HasDiagnostic(result, "PRAG0411").Should().BeTrue();
    }

    /// <summary>A segment that names nothing is PRAG0404, as a missing property of the operation is.</summary>
    [Fact]
    public void ADottedPathThatNamesNothing_IsReported()
    {
        var result = RunGeneratorWithEntities(Header + """
            [DomainAction]
            [LoadEntity<RoomType>("Request.RoomTypeIdentifier")]
            public partial class BookAction : DomainAction<bool>
            {
                public required BookRequest Request { get; init; }

                public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<bool, IError>>(true);
            }
            """);

        HasDiagnostic(result, "PRAG0404").Should().BeTrue();
    }

    /// <summary>The control: a plain key on the operation still loads as it did.</summary>
    [Fact]
    public void APlainKey_StillLoads()
    {
        var result = RunGeneratorWithEntities(Header + """
            [DomainAction]
            [LoadEntity<RoomType>(nameof(RoomTypeId))]
            public partial class LookAction : DomainAction<bool>
            {
                public required Guid RoomTypeId { get; init; }

                public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<bool, IError>>(true);
            }
            """);

        GetGeneratedSource(result, "LookAction.Invoker")!.Should().Contain("action.RoomTypeId");
        HasDiagnostic(result, "PRAG0404").Should().BeFalse();
    }
}
