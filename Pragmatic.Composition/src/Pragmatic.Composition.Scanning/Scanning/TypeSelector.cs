using Microsoft.Extensions.DependencyInjection;

namespace Pragmatic.Composition.Scanning;

internal sealed class TypeSelector : ITypeSelector
{
    private readonly IServiceCollection _services;
    private readonly IEnumerable<Type> _types;

    internal TypeSelector(IServiceCollection services, IEnumerable<Type> types)
    {
        _services = services;
        _types = types;
    }

    public ILifetimeSelector AsImplementedInterfaces()
        => new LifetimeSelector(_services, _types, t => t.GetInterfaces());

    public ILifetimeSelector AsSelf()
        => new LifetimeSelector(_services, _types, t => [t]);

    public ILifetimeSelector AsSelfWithInterfaces()
        => new LifetimeSelector(_services, _types, t => t.GetInterfaces().Prepend(t));

    public ILifetimeSelector As<T>() => As(typeof(T));

    public ILifetimeSelector As(Type type)
        => new LifetimeSelector(_services, _types, _ => [type]);

    public ILifetimeSelector As(Func<Type, Type> selector)
        => new LifetimeSelector(_services, _types, t => [selector(t)]);

    public ILifetimeSelector AsMatchingInterface()
    {
        return new LifetimeSelector(_services, _types, t =>
        {
            var name = "I" + t.Name;
            return t.GetInterfaces().Where(i => i.Name == name);
        });
    }
}
