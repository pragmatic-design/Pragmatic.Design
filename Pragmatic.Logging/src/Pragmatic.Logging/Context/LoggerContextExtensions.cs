using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.Context;

/// <summary>
/// Extension methods for integrating LogContext with ILogger.
/// </summary>
public static class LoggerContextExtensions
{
    /// <param name="logger">The logger</param>
    extension(ILogger logger)
    {
        /// <summary>
        /// Begins a logger scope with the current LogContext properties.
        /// </summary>
        /// <returns>A disposable scope</returns>
        public IDisposable? BeginScopeWithContext()
        {
            var context = LogContextScope.Current;
            if (context == null || context.Properties.Count == 0)
                return null;

            return logger.BeginScope(context.Properties);
        }

        /// <summary>
        /// Begins a logger scope with the specified LogContext properties.
        /// </summary>
        /// <param name="context">The context to use for the scope</param>
        /// <returns>A disposable scope</returns>
        public IDisposable? BeginScope(LogContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (context.Properties.Count == 0)
                return null;

            return logger.BeginScope(context.Properties);
        }

        /// <summary>
        /// Logs a message with automatic context enrichment from current LogContext and providers.
        /// </summary>
        /// <param name="logLevel">The log level</param>
        /// <param name="message">The message template</param>
        /// <param name="args">Message arguments</param>
        public void LogWithContext(LogLevel logLevel, string message, params object?[] args)
        {
            if (!logger.IsEnabled(logLevel))
                return;

            var contextProperties = new List<KeyValuePair<string, object?>>();

            // Add properties from current LogContext
            var currentContext = LogContextScope.Current;
            if (currentContext != null)
            {
                foreach (var kvp in currentContext.Properties)
                {
                    contextProperties.Add(kvp);
                }
            }

            // Add properties from context providers
            var providerProperties = ContextManager.Instance.GetContextProperties();
            foreach (var kvp in providerProperties)
            {
                contextProperties.Add(kvp);
            }

            if (contextProperties.Count > 0)
            {
                using (logger.BeginScope(contextProperties))
                {
                    logger.Log(logLevel, message, args);
                }
            }
            else
            {
                logger.Log(logLevel, message, args);
            }
        }

        /// <summary>
        /// Logs an information message with automatic context enrichment.
        /// </summary>
        /// <param name="message">The message template</param>
        /// <param name="args">Message arguments</param>
        public void LogInformationWithContext(string message, params object?[] args)
        {
            logger.LogWithContext(LogLevel.Information, message, args);
        }

        /// <summary>
        /// Logs a warning message with automatic context enrichment.
        /// </summary>
        /// <param name="message">The message template</param>
        /// <param name="args">Message arguments</param>
        public void LogWarningWithContext(string message, params object?[] args)
        {
            logger.LogWithContext(LogLevel.Warning, message, args);
        }

        /// <summary>
        /// Logs an error message with automatic context enrichment.
        /// </summary>
        /// <param name="message">The message template</param>
        /// <param name="args">Message arguments</param>
        public void LogErrorWithContext(string message, params object?[] args)
        {
            logger.LogWithContext(LogLevel.Error, message, args);
        }

        /// <summary>
        /// Logs an error message with exception and automatic context enrichment.
        /// </summary>
        /// <param name="exception">The exception</param>
        /// <param name="message">The message template</param>
        /// <param name="args">Message arguments</param>
        public void LogErrorWithContext(Exception exception, string message, params object?[] args)
        {
            if (!logger.IsEnabled(LogLevel.Error))
                return;

            var contextProperties = new List<KeyValuePair<string, object?>>();

            // Add properties from current LogContext
            var currentContext = LogContextScope.Current;
            if (currentContext != null)
            {
                foreach (var kvp in currentContext.Properties)
                {
                    contextProperties.Add(kvp);
                }
            }

            // Add properties from context providers
            var providerProperties = ContextManager.Instance.GetContextProperties();
            foreach (var kvp in providerProperties)
            {
                contextProperties.Add(kvp);
            }

            if (contextProperties.Count > 0)
            {
                using (logger.BeginScope(contextProperties))
                {
                    logger.LogError(exception, message, args);
                }
            }
            else
            {
                logger.LogError(exception, message, args);
            }
        }
    }
}