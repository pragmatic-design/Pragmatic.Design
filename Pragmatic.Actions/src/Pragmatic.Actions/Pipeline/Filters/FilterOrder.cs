namespace Pragmatic.Actions.Pipeline.Filters;

/// <summary>
///     Predefined order values for action filters.
///     Filters execute in ascending order for BeforeExecute and descending for AfterExecute.
/// </summary>
/// <remarks>
///     <para>
///         Use these constants when implementing custom filters to ensure proper ordering.
///         Leave gaps between values to allow inserting custom filters between built-in ones.
///     </para>
///     <para>
///         Example custom filter placement:
///     </para>
///     <list type="bullet">
///         <item>150: Custom authorization after validation</item>
///         <item>250: Custom business rules after authorization</item>
///         <item>500: Custom middleware</item>
///         <item>900: Pre-logging filter</item>
///     </list>
/// </remarks>
public static class FilterOrder
{
    /// <summary>
    ///     Validation filters run first (Order = 100).
    ///     Validates action parameters before any business logic.
    /// </summary>
    public const int Validation = 100;

    /// <summary>
    ///     Authorization filters run after validation (Order = 200).
    ///     Checks if the current user can execute the action.
    /// </summary>
    public const int Authorization = 200;

    /// <summary>
    ///     The declared resource policy runs after the permission (Order = 210).
    /// </summary>
    public const int PolicyEvaluation = 210;

    /// <summary>
    ///     Resource-level authorization runs last of the three (Order = 250).
    /// </summary>
    /// <remarks>
    ///     Unlike the two above it, this one has no internal-call bypass: a permission answers "may this
    ///     user perform this kind of operation", already settled at the outer call, while this answers
    ///     "may they touch <em>this</em> row", which every hop has to ask again.
    /// </remarks>
    public const int ResourceAuthorization = 250;

    // No Transaction (300) or Caching (400). Both were declared here and neither had a filter: the
    // transaction is opened inline by the invoker when the action is [Transactional], and caching is a
    // different mechanism entirely. An order for a stage that does not exist describes a pipeline to
    // whoever reads it, and this file is the one place someone looks to find out what runs when.

    /// <summary>
    ///     Logging and telemetry filters run last (Order = 1000).
    ///     Records action execution for monitoring and debugging.
    /// </summary>
    public const int Logging = 1000;
}
