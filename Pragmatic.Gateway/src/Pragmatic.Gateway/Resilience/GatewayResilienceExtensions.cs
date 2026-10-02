using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Resilience.State;
using Yarp.ReverseProxy.Configuration;

namespace Pragmatic.Gateway.Resilience;

/// <summary>
/// Extension methods for wiring Gateway resilience into the YARP proxy pipeline.
/// </summary>
public static class GatewayResilienceExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds resilience services (circuit breaker state store, options) to the DI container.
        /// </summary>
        public IServiceCollection AddGatewayResilience(GatewayResilienceOptions options)
        {
            services.AddSingleton(options);
            services.AddSingleton<ICircuitBreakerStateStore, InMemoryCircuitBreakerStateStore>();
            services.AddSingleton<ProxyResilienceMiddleware>();
            return services;
        }

        /// <summary>
        /// Adds resilience services with configuration callback.
        /// </summary>
        public IServiceCollection AddGatewayResilience(Action<GatewayResilienceOptions>? configure = null)
        {
            var options = new GatewayResilienceOptions();
            configure?.Invoke(options);
            return services.AddGatewayResilience(options);
        }
    }

    /// <summary>
    /// Inserts the resilience middleware into the YARP proxy pipeline.
    /// Call inside MapReverseProxy(pipeline => { pipeline.UseGatewayResilience(); }).
    /// </summary>
    public static IReverseProxyApplicationBuilder UseGatewayResilience(
        this IReverseProxyApplicationBuilder pipeline)
    {
        pipeline.Use((context, next) =>
        {
            var middleware = context.RequestServices.GetRequiredService<ProxyResilienceMiddleware>();
            return middleware.InvokeAsync(context, next);
        });

        return pipeline;
    }
}
