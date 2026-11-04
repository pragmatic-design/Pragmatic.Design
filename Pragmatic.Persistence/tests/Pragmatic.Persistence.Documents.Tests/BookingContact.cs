namespace PragmaticDocuments.Tests;

/// <summary>A booking with its guest's email, read through a declared join (#508): WhoEmail is Guest.Email, by the alias.</summary>
public sealed class BookingContact
{
    public int Nights { get; init; }

    public string? WhoEmail { get; init; }
}
