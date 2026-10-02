namespace Pragmatic.Email.Model;

/// <summary>A social media link for <see cref="EmailBuilder.SocialBar"/>.</summary>
public sealed record SocialLink(string IconUrl, string Href, string Label, int IconSize = 32);
