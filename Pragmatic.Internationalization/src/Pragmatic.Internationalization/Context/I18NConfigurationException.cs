namespace Pragmatic.Internationalization.Context;

/// <summary>
///     Exception thrown when internationalization configuration is invalid or missing.
/// </summary>
/// <remarks>
///     <para>
///         This exception is thrown when the <see cref="I18NConfigResolver"/> cannot resolve
///         a required configuration value (e.g., no UI culture configured).
///     </para>
///     <para>
///         Unlike a silent fallback to a default culture, this exception makes configuration
///         errors visible immediately, preventing unexpected behavior in production.
///     </para>
/// </remarks>
public sealed class I18NConfigurationException : InvalidOperationException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="I18NConfigurationException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public I18NConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="I18NConfigurationException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public I18NConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    #region Static Factory Methods

    /// <summary>
    ///     Creates an exception for missing UI culture configuration.
    /// </summary>
    public static I18NConfigurationException NoUICultureConfigured()
    {
        return new I18NConfigurationException(
            "No UI culture configured. " +
            "Either configure options in AddPragmaticInternationalization() " +
            "or register a provider that returns a DefaultUICulture.");
    }

    /// <summary>
    ///     Creates an exception for missing supported cultures configuration.
    /// </summary>
    public static I18NConfigurationException NoSupportedCulturesConfigured()
    {
        return new I18NConfigurationException(
            "No supported cultures configured. " +
            "At least one supported culture must be specified via options or a provider.");
    }

    /// <summary>
    ///     Creates an exception for an unsupported culture.
    /// </summary>
    /// <param name="requestedCulture">The culture that was requested.</param>
    /// <param name="supportedCultures">The list of supported cultures.</param>
    public static I18NConfigurationException CultureNotSupported(
        string requestedCulture,
        IEnumerable<string> supportedCultures)
    {
        return new I18NConfigurationException(
            $"Culture '{requestedCulture}' is not supported. " +
            $"Supported cultures: {string.Join(", ", supportedCultures)}");
    }

    #endregion
}
