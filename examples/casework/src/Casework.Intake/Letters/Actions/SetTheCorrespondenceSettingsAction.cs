using Pragmatic.Persistence.Repository;

namespace Casework.Intake.Letters.Actions;

/// <summary>
///     An organisation says which address its mail goes out from.
/// </summary>
/// <remarks>
///     One row per organisation, replaced rather than added to: an organisation with two senders is an
///     organisation whose applicants get mail from whichever one the query happened to return first.
/// </remarks>
[DomainAction]
[RequirePermission(IntakePermissions.LetterTemplate.Create)]
[Endpoint(HttpVerb.Put, "api/correspondence")]
public partial class SetTheCorrespondenceSettingsAction : DomainAction<CorrespondenceSettingsDto, IError>
{
    private IRepository<CorrespondenceSettings> _settings = null!;

    /// <summary>The address applicants see, and reply to.</summary>
    [Required]
    [MaxLength(200)]
    public required string SenderAddress { get; init; }

    /// <summary>The name beside it.</summary>
    [Required]
    [MaxLength(200)]
    public required string SenderName { get; init; }

    public override async Task<Result<CorrespondenceSettingsDto, IError>> Execute(
        CancellationToken ct = default)
    {
        var existing = await _settings
            .FirstOrDefaultAsync(CorrespondenceSettingsSpecifications.TheOnlyOne(), ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            existing.ChangeTo(SenderAddress, SenderName);

            return CorrespondenceSettingsDto.FromEntity(existing);
        }

        var settings = CorrespondenceSettings.Of(SenderAddress, SenderName);
        _settings.Add(settings);

        return CorrespondenceSettingsDto.FromEntity(settings);
    }
}
