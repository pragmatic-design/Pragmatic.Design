using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;

namespace Pragmatic.Integration.Tests.Domain.Actions;

/// <summary>
///     Answers with the optional values it was given, so a test can see what an absent one became.
/// </summary>
/// <remarks>
///     Both are <c>init</c>, the shape that has to compile: an absent header must leave
///     <see cref="Source" /> at <c>null</c>, and an absent query value must leave <see cref="Limit" /> at the
///     20 its declaration says.
/// </remarks>
[DomainAction]
[Endpoint(Pragmatic.Endpoints.HttpVerb.Get, "/api/notes/echo")]
public partial class EchoNoteAction : DomainAction<string>
{
    /// <summary>Who is asking, when the caller says.</summary>
    [FromHeader(Name = "X-Note-Source")]
    public string? Source { get; init; }

    /// <summary>How many notes to show.</summary>
    [FromQuery]
    public int Limit { get; init; } = 20;

    /// <inheritdoc />
    public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult(Result<string, IError>.Success($"{Source ?? "none"}|{Limit}"));
}
