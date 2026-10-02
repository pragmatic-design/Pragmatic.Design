namespace Pragmatic.Patch.Samples;

/// <summary>
///     Entity with multiple property types: required, nullable, private setter, auditable.
///     Demonstrates what gets included/excluded in patch generation.
/// </summary>
public class Guest
{
    // Excluded: identity
    public Guid Id { get; set; }

    // Included: public setter
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }

    // Included: private setter with SetVipLevel() method
    public int VipLevel { get; private set; }

    // Excluded: auditable
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public void SetVipLevel(int value) => VipLevel = value;
}
