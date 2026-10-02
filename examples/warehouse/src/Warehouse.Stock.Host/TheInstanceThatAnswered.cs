using Pragmatic.Composition.Abstractions;
using Pragmatic.Composition.Attributes;
using Pragmatic.ControlPlane;

namespace Warehouse.Stock.Host;

/// <summary>
///     Every response says which process answered it: <c>X-Served-By: {host name}/{instance id}</c>.
/// </summary>
/// <remarks>
///     Behind a gateway a caller cannot tell one instance from another, and neither can a test that asks
///     whether the gateway spread its requests. The instance id is <see cref="IHostIdentity.HostId" />,
///     new at every start, so two instances of one host never answer with the same value.
/// </remarks>
[StartupStep]
public sealed class TheInstanceThatAnswered : IStartupStep
{
    /// <summary>Before everything else, so a response refused early still carries the header.</summary>
    public int Order => 0;

    public void ConfigurePipeline(IApplicationBuilder app)
    {
        var identity = app.ApplicationServices.GetRequiredService<IHostIdentity>();
        var servedBy = $"{identity.HostName}/{identity.HostId}";

        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Served-By"] = servedBy;
            await next(context).ConfigureAwait(false);
        });
    }
}
