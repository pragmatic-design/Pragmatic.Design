using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Composition;

namespace Pragmatic.Storage;

/// <summary>
///     Extension methods for configuring file storage on <see cref="IPragmaticBuilder" />.
/// </summary>
public static class PragmaticBuilderStorageExtensions
{
    /// <param name="builder">The Pragmatic builder.</param>
    extension(IPragmaticBuilder builder)
    {
        /// <summary>
        ///     Configures a custom file storage implementation.
        /// </summary>
        /// <param name="factory">Factory to create the file storage instance.</param>
        /// <returns>The builder for chaining.</returns>
        public IPragmaticBuilder UseStorage(Func<IServiceProvider, IFileStorage> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            builder.Services.AddSingleton(factory);
            return builder;
        }

        /// <summary>
        ///     Configures a file storage implementation by type.
        /// </summary>
        /// <typeparam name="TStorage">The file storage implementation type.</typeparam>
        /// <returns>The builder for chaining.</returns>
        public IPragmaticBuilder UseStorage<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStorage>()
            where TStorage : class, IFileStorage
        {
            builder.Services.AddSingleton<IFileStorage, TStorage>();
            return builder;
        }
    }
}
