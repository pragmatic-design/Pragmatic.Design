using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Composition.Attributes;

namespace Pragmatic.Composition.Scanning;

internal sealed class LifetimeSelector : ILifetimeSelector
{
    private readonly IServiceCollection _services;
    private readonly Func<Type, IEnumerable<Type>> _serviceTypeSelector;
    private readonly IEnumerable<Type> _types;
    private RegistrationStrategy _strategy = RegistrationStrategy.Append;

    internal LifetimeSelector(
        IServiceCollection services,
        IEnumerable<Type> types,
        Func<Type, IEnumerable<Type>> serviceTypeSelector)
    {
        _services = services;
        _types = types;
        _serviceTypeSelector = serviceTypeSelector;
    }

    public ILifetimeSelector UsingRegistrationStrategy(RegistrationStrategy strategy)
    {
        _strategy = strategy;
        return this;
    }

    public void WithSingletonLifetime() => WithLifetime(Lifetime.Singleton);
    public void WithScopedLifetime() => WithLifetime(Lifetime.Scoped);
    public void WithTransientLifetime() => WithLifetime(Lifetime.Transient);

    public void WithLifetime(Lifetime lifetime)
    {
        var msLifetime = ToMsLifetime(lifetime);

        foreach (var implementationType in _types)
        {
            var serviceTypes = _serviceTypeSelector(implementationType);
            foreach (var serviceType in serviceTypes)
                if (serviceType.IsAssignableFrom(implementationType))
                    RegisterWithStrategy(serviceType, implementationType, msLifetime);
        }
    }

    private void RegisterWithStrategy(
        Type serviceType,
        Type implementationType,
        Microsoft.Extensions.DependencyInjection.ServiceLifetime lifetime)
    {
        var existingDescriptors = _services
            .Where(d => d.ServiceType == serviceType)
            .ToList();

        switch (_strategy)
        {
            case RegistrationStrategy.Append:
                _services.Add(new ServiceDescriptor(serviceType, implementationType, lifetime));
                break;

            case RegistrationStrategy.Skip:
                if (existingDescriptors.Count == 0)
                    _services.Add(new ServiceDescriptor(serviceType, implementationType, lifetime));
                break;

            case RegistrationStrategy.Replace:
                foreach (var descriptor in existingDescriptors)
                    _services.Remove(descriptor);
                _services.Add(new ServiceDescriptor(serviceType, implementationType, lifetime));
                break;

            case RegistrationStrategy.Throw:
                if (existingDescriptors.Count > 0)
                {
                    var existing = existingDescriptors[0];
                    throw new InvalidOperationException(
                        $"Duplicate registration detected for service type '{serviceType.FullName}'. " +
                        $"Existing implementation: '{existing.ImplementationType?.FullName}', " +
                        $"New implementation: '{implementationType.FullName}'.");
                }
                _services.Add(new ServiceDescriptor(serviceType, implementationType, lifetime));
                break;
        }
    }

    private static Microsoft.Extensions.DependencyInjection.ServiceLifetime ToMsLifetime(Lifetime lifetime)
    {
        return lifetime switch
        {
            Lifetime.Singleton => Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton,
            Lifetime.Scoped => Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped,
            Lifetime.Transient => Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient,
            // Fail loudly on an unmapped enum value rather than silently defaulting to Scoped — a future
            // Lifetime addition must be mapped explicitly, not silently mis-registered.
            _ => throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime,
                $"Unmapped {nameof(Lifetime)} value; add an explicit mapping in {nameof(ToMsLifetime)}.")
        };
    }
}
