namespace Showcase.IntegrationTests.Infrastructure;

/// <summary>
///     The <c>[Lookup]</c> reference rows <see cref="PostgresFixture" /> writes once, before any host
///     boots.
/// </summary>
/// <remarks>
///     <para>
///         A lookup table is reference data: it is in the database when the application starts, put
///         there by a deployment, and the application reads it once. Seeding it here rather than from a
///         hosted service inside the Showcase keeps that shape — and keeps the preload's ordering out
///         of the tests, since there is no second startup service that has to run first.
///     </para>
///     <para>
///         Fixed ids so a test can name a row without reading it back. They are ordinary v4 GUIDs; the
///         entity's own ids are Guid7, but nothing about a lookup key depends on that.
///     </para>
/// </remarks>
public static class SeededCategories
{
    public const string ResortName = "Resort";
    public const string BeachResortName = "Beach Resort";

    public static readonly Guid ResortId = new("0f8f4b3a-6c1e-4a2d-9f10-2b7c5d8e1a01");
    public static readonly Guid BeachResortId = new("0f8f4b3a-6c1e-4a2d-9f10-2b7c5d8e1a02");

    /// <summary>Every seeded name, in insertion order.</summary>
    public static readonly string[] Names = [ResortName, BeachResortName];
}
