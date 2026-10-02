namespace Casework.Intake.Entities;

/// <summary>The rules a letter template is read by.</summary>
public static partial class LetterTemplateSpecifications
{
    /// <summary>
    ///     One piece of the letter — <c>decision-letter</c> or <c>header</c>.
    /// </summary>
    /// <remarks>
    ///     The organisation is <b>not</b> in the rule, and must not be: the tenant filter already
    ///     answers that question, and a specification that repeated it would be a second answer that can
    ///     disagree. What is here is the only thing that distinguishes two rows of the same organisation.
    /// </remarks>
    public static Specification<LetterTemplate> ThePiece(string piece)
        => Spec<LetterTemplate>.Where(template => template.Piece == piece);
}
