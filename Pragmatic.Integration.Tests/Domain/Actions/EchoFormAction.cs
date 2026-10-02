using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;

namespace Pragmatic.Integration.Tests.Domain.Actions;

/// <summary>
///     Answers with the form fields it was given, so a test can see which ones a form may leave out.
/// </summary>
[DomainAction]
[Endpoint(Pragmatic.Endpoints.HttpVerb.Post, "/api/forms/echo")]
public partial class EchoFormAction : DomainAction<string>
{
    /// <summary>The title: not nullable, so a form without it is refused.</summary>
    [FromForm]
    public required string Title { get; init; }

    /// <summary>The caption: nullable, so a form may leave it out.</summary>
    [FromForm]
    public string? Caption { get; init; }

    /// <summary>A note sent under a name of its own, not the property's.</summary>
    [FromForm(Name = "note_text")]
    public string? Note { get; init; }

    /// <inheritdoc />
    public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult(Result<string, IError>.Success($"{Title}|{Caption ?? "none"}|{Note ?? "none"}"));
}
