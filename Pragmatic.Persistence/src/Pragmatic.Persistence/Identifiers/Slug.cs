using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Persistence.Identifiers;

/// <summary>
///     URL-friendly slug generation with multiple strategies.
/// </summary>
/// <remarks>
///     <para>
///         A slug is a URL-friendly identifier typically used for:
///     </para>
///     <list type="bullet">
///         <item>
///             <description>SEO-friendly URLs: /articles/my-awesome-article</description>
///         </item>
///         <item>
///             <description>Human-readable identifiers</description>
///         </item>
///         <item>
///             <description>Filesystem-safe names</description>
///         </item>
///     </list>
///     <para>
///         Features:
///     </para>
///     <list type="bullet">
///         <item>
///             <description>Unicode normalization (é → e, ü → u)</description>
///         </item>
///         <item>
///             <description>Whitespace and special character handling</description>
///         </item>
///         <item>
///             <description>Configurable max length with word boundary respect</description>
///         </item>
///         <item>
///             <description>Unique suffix generation for collision avoidance</description>
///         </item>
///     </list>
/// </remarks>
public static partial class Slug
{
    // Characters allowed in slugs (alphanumeric + hyphen)
    private const string ValidSlugPattern = @"^[a-z0-9]+(?:-[a-z0-9]+)*$";

    // Alphabet for random suffixes (URL-safe, no confusing characters)
    private const string SuffixAlphabet = "abcdefghijkmnpqrstuvwxyz23456789";

    /// <summary>
    ///     Creates a human-readable slug from text.
    /// </summary>
    /// <param name="text">The text to convert to a slug.</param>
    /// <param name="maxLength">Maximum length of the resulting slug (default: 100).</param>
    /// <returns>A URL-friendly slug.</returns>
    /// <remarks>
    ///     <para>Example transformations:</para>
    ///     <list type="bullet">
    ///         <item>
    ///             <description>"Hello World!" → "hello-world"</description>
    ///         </item>
    ///         <item>
    ///             <description>"Café Résumé" → "cafe-resume"</description>
    ///         </item>
    ///         <item>
    ///             <description>"C# Programming 101" → "c-programming-101"</description>
    ///         </item>
    ///         <item>
    ///             <description>"  Multiple   Spaces  " → "multiple-spaces"</description>
    ///         </item>
    ///     </list>
    /// </remarks>
    public static string Create(string text, int maxLength = 100)
    {
        ThrowIfNullOrWhiteSpace(text);
        ThrowIfLessThan(maxLength, 1);

        var slug = Normalize(text);
        return Truncate(slug, maxLength);
    }

    /// <summary>
    ///     Creates a unique slug with a random suffix.
    /// </summary>
    /// <param name="text">The text to convert to a slug.</param>
    /// <param name="maxLength">Maximum length of the resulting slug including suffix (default: 100).</param>
    /// <param name="suffixLength">Length of the random suffix (default: 5).</param>
    /// <returns>A URL-friendly slug with a unique suffix.</returns>
    /// <remarks>
    ///     <para>Example: "Hello World" → "hello-world-x7k2p"</para>
    ///     <para>The suffix ensures uniqueness even for identical inputs.</para>
    /// </remarks>
    public static string CreateUnique(string text, int maxLength = 100, int suffixLength = 5)
    {
        ThrowIfNullOrWhiteSpace(text);
        ThrowIfLessThan(maxLength, suffixLength + 2); // At least 1 char + hyphen + suffix
        ThrowIfLessThan(suffixLength, 1);

        var suffix = GenerateRandomSuffix(suffixLength);
        var maxBaseLength = maxLength - suffixLength - 1; // -1 for hyphen

        var baseSlug = Create(text, maxBaseLength);

        if (string.IsNullOrEmpty(baseSlug))
            return suffix;

        return $"{baseSlug}-{suffix}";
    }

    /// <summary>
    ///     Creates a completely anonymous/opaque slug with no relation to any input.
    /// </summary>
    /// <param name="length">The length of the slug (default: 8).</param>
    /// <returns>A random alphanumeric slug.</returns>
    /// <remarks>
    ///     <para>Example: → "aB3kL9mN"</para>
    ///     <para>Useful when you need a unique identifier without any semantic meaning.</para>
    /// </remarks>
    public static string CreateAnonymous(int length = 8)
    {
        ThrowIfLessThan(length, 1);

        return GenerateRandomSuffix(length);
    }

    /// <summary>
    ///     Validates if a string is a valid slug format.
    /// </summary>
    /// <param name="slug">The string to validate.</param>
    /// <returns>True if the string is a valid slug, false otherwise.</returns>
    /// <remarks>
    ///     <para>A valid slug:</para>
    ///     <list type="bullet">
    ///         <item>
    ///             <description>Contains only lowercase letters, numbers, and hyphens</description>
    ///         </item>
    ///         <item>
    ///             <description>Does not start or end with a hyphen</description>
    ///         </item>
    ///         <item>
    ///             <description>Does not contain consecutive hyphens</description>
    ///         </item>
    ///     </list>
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsValid(string? slug)
    {
        if (string.IsNullOrEmpty(slug))
            return false;

        return SlugValidationRegex().IsMatch(slug);
    }

    /// <summary>
    ///     Normalizes text into slug format.
    /// </summary>
    private static string Normalize(string text)
    {
        // Normalize Unicode (NFD - decompose accented characters)
        var normalized = text.Normalize(NormalizationForm.FormD);

        var sb = new StringBuilder(normalized.Length);
        var lastWasHyphen = true; // Start true to skip leading hyphens

        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);

            // Skip combining marks (accents, diacritics)
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            // Only ASCII [a-z0-9] survive. char.IsLetterOrDigit is true for non-Latin letters that NFD
            // does not decompose (Cyrillic/Greek/CJK); let into the slug, they would produce output that
            // fails the class's own ASCII IsValid regex and enable homograph collisions
            // (e.g. Cyrillic 'а' U+0430 vs ASCII 'a'). Non-ASCII letters are treated as separators.
            if (c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
            {
                sb.Append(char.ToLowerInvariant(c));
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen)
            {
                // Replace non-ASCII-alphanumeric with hyphen (avoid consecutive hyphens)
                sb.Append('-');
                lastWasHyphen = true;
            }
        }

        // Remove trailing hyphen
        var result = sb.ToString().TrimEnd('-');

        return result;
    }

    /// <summary>
    ///     Truncates a slug to max length, respecting word boundaries.
    /// </summary>
    private static string Truncate(string slug, int maxLength)
    {
        if (slug.Length <= maxLength)
            return slug;

        // Find the last hyphen before maxLength
        var truncated = slug.Substring(0, maxLength);
        var lastHyphen = truncated.LastIndexOf('-');

        if (lastHyphen > 0 && lastHyphen > maxLength / 2)
            // Truncate at word boundary if it's not too short
            return truncated.Substring(0, lastHyphen);

        // Otherwise, just truncate at maxLength and remove trailing hyphen
        return truncated.TrimEnd('-');
    }

    /// <summary>
    ///     Generates a random suffix for unique slugs.
    /// </summary>
    private static string GenerateRandomSuffix(int length)
    {
        Span<byte> randomBytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(randomBytes);

        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = SuffixAlphabet[randomBytes[i] % SuffixAlphabet.Length];

        return new string(chars);
    }

    [GeneratedRegex(ValidSlugPattern, RegexOptions.Compiled)]
    private static partial Regex SlugValidationRegex();
}
