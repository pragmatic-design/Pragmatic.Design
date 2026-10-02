using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EFCore.Tests;

/// <summary>Minimal entity used to prove the convention opt-out leaves temporal types unmapped.</summary>
public class OptOutEntity
{
    public int Id { get; set; }
    public LocalDate Date { get; set; }
}
