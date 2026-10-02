namespace Pragmatic.Identity;

/// <summary>
///     Marks a property on a <c>[PragmaticUser]</c> entity as part of the user's profile.
///     The source generator collects these to produce an <see cref="IUserProfile"/> implementation.
///     Well-known properties (<c>PreferredCulture</c>, <c>TimeZone</c>) map directly;
///     all others are included in <see cref="IUserProfile.Properties"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ProfilePropertyAttribute : Attribute;
