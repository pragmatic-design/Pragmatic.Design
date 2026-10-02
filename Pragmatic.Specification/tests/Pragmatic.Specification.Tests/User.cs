namespace Pragmatic.Specification.Tests;

/// <summary>
///     Simple test entity for specification tests.
/// </summary>
public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsDeleted { get; set; }
    public int Age { get; set; }
}