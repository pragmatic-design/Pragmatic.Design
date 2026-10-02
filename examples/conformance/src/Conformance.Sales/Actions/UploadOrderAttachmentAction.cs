using Conformance.Sales.Dtos;
using Microsoft.AspNetCore.Http;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;
using Pragmatic.Validation.Attributes;

namespace Conformance.Sales.Actions;

/// <summary>
///     An upload written like any other operation of this framework: <c>required … init</c>
///     properties, a typed form field, a declarative rule on a field.
/// </summary>
/// <remarks>
///     <para>
///         It exists for a single cell: <b>a <c>[FromForm]</c> compiles and executes</b>. The shape
///         combines three things that each need the generator's help — two <c>required</c> fields, a
///         <c>Guid</c> that is not a string, a rule.
///     </para>
///     <para>
///         ⚠️ Form fields must be assigned in the object initializer, not after construction
///         (<c>CS9035</c>, <c>CS8852</c>), bound with their own type (<c>CS1503</c> on the <c>Guid</c>
///         otherwise), and validated without assuming a <c>…Body</c> the form does not have
///         (<c>CS0103</c>). A <c>{ get; set; } = null!</c> upload with only string fields would survive
///         all three by accident.
///     </para>
///     <para>
///         It writes nothing: the receipt says how many bytes arrived, which proves the file was read
///         and not merely accepted. <c>TheFormThatArrives</c> measures it.
///     </para>
/// </remarks>
[DomainAction]
[AllowAnonymous]
[Endpoint(HttpVerb.Post, "api/orders/{id}/attachments")]
public partial class UploadOrderAttachmentAction : DomainAction<UploadReceiptDto, IError>
{
    public required Guid Id { get; init; }

    [FromForm]
    public required IFormFile File { get; init; }

    [FromForm]
    [MinLength(1)]
    public required string Label { get; init; }

    [FromForm]
    public Guid FolderId { get; init; }

    public override async Task<Result<UploadReceiptDto, IError>> Execute(CancellationToken ct = default)
    {
        // Read it through, not File.Length: the header says what the client claims, the stream says
        // what arrived.
        long bytes = 0;
        var buffer = new byte[8192];
        var stream = File.OpenReadStream();
        await using (stream.ConfigureAwait(false))
        {
            int read;
            while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                bytes += read;
        }

        return new UploadReceiptDto(Id, File.FileName, bytes, Label, FolderId);
    }
}
