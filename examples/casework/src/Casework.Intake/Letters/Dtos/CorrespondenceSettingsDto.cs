namespace Casework.Intake.Dtos;

/// <summary>Where this organisation's mail goes out from.</summary>
[MapFrom<CorrespondenceSettings>]
public sealed partial class CorrespondenceSettingsDto
{
    public Guid Id { get; init; }

    public string SenderAddress { get; init; } = "";

    public string SenderName { get; init; } = "";
}
