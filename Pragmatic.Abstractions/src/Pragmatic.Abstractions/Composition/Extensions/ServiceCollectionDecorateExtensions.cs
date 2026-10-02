using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Pragmatic.Composition.Extensions;

/// <summary>
///     Extension methods for decorating services in <see cref="IServiceCollection" />.
///     Lives in Pragmatic.Abstractions so domain modules can use <c>Decorate&lt;&gt;</c>
///     without referencing ASP.NET Core or Pragmatic.Composition.Host.
/// </summary>
/// <remarks>
///     <b>Disposal:</b> the inner (decorated) instance is created by this extension's factory, so the
///     container only tracks the outer decorator for disposal. An <see cref="IDisposable"/> inner
///     implementation is NOT disposed by the container — have the decorator take ownership (dispose
///     the inner from its own Dispose) when the inner holds resources.
/// </remarks>
public static class ServiceCollectionDecorateExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Decorates all registrations of <typeparamref name="TService" /> with
        ///     <typeparamref name="TDecorator" />.
        /// </summary>
        public IServiceCollection Decorate<
            TService,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TDecorator>()
            where TService : class
            where TDecorator : class, TService
        {
            return services.Decorate(typeof(TService), typeof(TDecorator));
        }

        /// <summary>
        ///     Decorates all registrations of the specified service type with the decorator type.
        /// </summary>
        /// <remarks>
        ///     <paramref name="decoratorType"/> is instantiated via
        ///     <see cref="ActivatorUtilities"/> at container build time. It is treated as <b>trusted
        ///     DI-setup input</b> supplied by application/composition code — never a user- or
        ///     network-controlled type. The activation is a deliberate, contained use of reflection in
        ///     the registration path (not on a request hot path), consistent with the framework's
        ///     zero-reflection-at-runtime policy.
        /// </remarks>
        public IServiceCollection Decorate(Type serviceType,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type decoratorType)
        {
            var wrappedDescriptors = services
                .Where(d => d.ServiceType == serviceType)
                .ToList();

            if (wrappedDescriptors.Count == 0)
                throw new InvalidOperationException(
                    $"Cannot decorate {serviceType.Name}: no registrations found. " +
                    "Register the service before decorating it.");

            foreach (var descriptor in wrappedDescriptors)
            {
                var index = services.IndexOf(descriptor);
                services.RemoveAt(index);

                var decoratorDescriptor = CreateDecoratorDescriptor(descriptor, decoratorType);
                services.Insert(index, decoratorDescriptor);
            }

            return services;
        }

        /// <summary>
        ///     Decorates all registrations of <typeparamref name="TService" /> using a factory.
        /// </summary>
        public IServiceCollection Decorate<TService>(Func<TService, IServiceProvider, TService> decorator)
            where TService : class
            => DecorateWhere(services, decorator, static _ => true);

        /// <summary>
        ///     Decorates only the unkeyed registrations of <typeparamref name="TService" /> using a factory.
        /// </summary>
        /// <remarks>
        ///     For a decorator whose keyed registrations are built on the unkeyed one. Pragmatic.Caching's
        ///     category stacks are a prefix over the default <c>ICacheStack</c>, resolved from the
        ///     container: decorate the default and every category already goes through the decorator —
        ///     decorate the categories too and it runs twice, once before the prefix and once after.
        /// </remarks>
        public IServiceCollection DecorateUnkeyed<TService>(Func<TService, IServiceProvider, TService> decorator)
            where TService : class
            => DecorateWhere(services, decorator, static d => !d.IsKeyedService);
    }

    private static IServiceCollection DecorateWhere<TService>(
        IServiceCollection services,
        Func<TService, IServiceProvider, TService> decorator,
        Func<ServiceDescriptor, bool> include)
        where TService : class
    {
        var wrappedDescriptors = services
            .Where(d => d.ServiceType == typeof(TService) && include(d))
            .ToList();

        if (wrappedDescriptors.Count == 0)
            throw new InvalidOperationException(
                $"Cannot decorate {typeof(TService).Name}: no registrations found.");

        foreach (var descriptor in wrappedDescriptors)
        {
            var index = services.IndexOf(descriptor);
            services.RemoveAt(index);

            var innerFactory = CreateInnerFactory<TService>(descriptor);
            // Preserve keyed-ness: a keyed registration must stay resolvable by its key.
            var decoratorDescriptor = descriptor.IsKeyedService
                ? ServiceDescriptor.DescribeKeyed(
                    typeof(TService),
                    descriptor.ServiceKey,
                    (sp, _) => decorator(innerFactory(sp), sp),
                    descriptor.Lifetime)
                : ServiceDescriptor.Describe(
                    typeof(TService),
                    sp => decorator(innerFactory(sp), sp),
                    descriptor.Lifetime);

            services.Insert(index, decoratorDescriptor);
        }

        return services;
    }

    private static ServiceDescriptor CreateDecoratorDescriptor(
        ServiceDescriptor innerDescriptor,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type decoratorType)
    {
        // Preserve keyed-ness: replacing a keyed registration with a non-keyed one would silently
        // break every [FromKeyedServices]/GetRequiredKeyedService resolution of the decorated type.
        if (innerDescriptor.IsKeyedService)
            return ServiceDescriptor.DescribeKeyed(
                innerDescriptor.ServiceType,
                innerDescriptor.ServiceKey,
                (sp, _) =>
                {
                    var inner = CreateInstance(sp, innerDescriptor);
                    return ActivatorUtilities.CreateInstance(sp, decoratorType, inner);
                },
                innerDescriptor.Lifetime);

        return ServiceDescriptor.Describe(
            innerDescriptor.ServiceType,
            sp =>
            {
                var inner = CreateInstance(sp, innerDescriptor);
                return ActivatorUtilities.CreateInstance(sp, decoratorType, inner);
            },
            innerDescriptor.Lifetime);
    }

    private static Func<IServiceProvider, TService> CreateInnerFactory<TService>(
        ServiceDescriptor descriptor) where TService : class
    {
        return sp => (TService)CreateInstance(sp, descriptor);
    }

    private static object CreateInstance(IServiceProvider sp, ServiceDescriptor descriptor)
    {
        // Keyed service descriptors (.NET 8+) MUST be checked first: on a keyed descriptor the
        // non-keyed ImplementationInstance/Factory/Type getters THROW InvalidOperationException
        // (BCL contract), so touching them before this check would make the keyed path unreachable.
        if (descriptor.IsKeyedService)
        {
            if (descriptor.KeyedImplementationInstance is not null)
                return descriptor.KeyedImplementationInstance;

            if (descriptor.KeyedImplementationFactory is not null)
                return descriptor.KeyedImplementationFactory(sp, descriptor.ServiceKey);

            if (descriptor.KeyedImplementationType is not null)
                return ActivatorUtilities.CreateInstance(sp, descriptor.KeyedImplementationType);

            // A keyed descriptor with none of the three implementation properties set is invalid.
            throw new InvalidOperationException(
                $"Cannot create keyed service instance for {descriptor.ServiceType.Name} " +
                $"(key '{descriptor.ServiceKey}'): descriptor has no implementation instance, " +
                "factory, or type. Ensure the keyed service was registered correctly.");
        }

        if (descriptor.ImplementationInstance is not null)
            return descriptor.ImplementationInstance;

        if (descriptor.ImplementationFactory is not null)
            return descriptor.ImplementationFactory(sp);

        if (descriptor.ImplementationType is not null)
            return ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType);

        throw new InvalidOperationException(
            $"Cannot create instance for {descriptor.ServiceType.Name}");
    }
}
