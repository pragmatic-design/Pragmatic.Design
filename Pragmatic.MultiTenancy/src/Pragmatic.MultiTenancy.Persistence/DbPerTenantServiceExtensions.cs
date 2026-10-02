using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Pragmatic.MultiTenancy.Persistence;

/// <summary>
///     Extension methods for registering DB-per-tenant services.
/// </summary>
public static class DbPerTenantServiceExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Registers DB-per-tenant services: options, provisioner (NoOp default), the per-tenant
        ///     migration orchestrator, and (MT-H2) automatic per-request connection routing — a
        ///     <see cref="TenantConnectionInterceptor"/> that rewrites each DbContext's connection to the
        ///     current tenant's dedicated database on connection open. Tenants without a dedicated
        ///     connection stay on the shared database (row-level isolation). No DbContext registration
        ///     change is required: EF Core auto-discovers the interceptor from the service provider.
        /// </summary>
        /// <remarks>
        ///     ⚠️ <b>It registers the per-tenant migration orchestrator; it does not run it.</b> The
        ///     registration is real
        ///     (<c>ITenantMigrationOrchestrator</c> below), and what calls it is the generated host entry
        ///     point at startup, or the application whenever it chooses. Nothing here migrates on a
        ///     request, and nothing here creates a database — see <c>TenantDatabaseOptions</c> for why
        ///     the two halves are the application's to compose.
        /// </remarks>
        public IServiceCollection AddDbPerTenant(Action<TenantDatabaseOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            var options = new TenantDatabaseOptions();
            configure(options);

            // Both ways of asking, and ⚠️ the SAME object. The framework's own consumers inject it bare
            // (TenantConnectionResolver, TenantConnectionStringProvider); an application reasonably asks
            // for IOptions<T>, which is what MultiTenancyOptions does three lines from here. Without the
            // wrapper that resolves a brand new instance with an empty template, silently, and it surfaces
            // four layers later as "Cannot extract database name from connection string" from the provisioner.
            //
            // OptionsWrapper and NOT services.Configure<T>(…): Configure builds its own instance through
            // the options pipeline, so both injections would work and would hand out two objects that
            // drift the moment anything writes to one. Two doors, one room.
            //
            // No IOptionsSnapshot/IOptionsMonitor: this is configured once at registration and is not
            // reloadable, and offering a third door onto something that never changes would say it is.
            services.AddSingleton(options);
            services.AddSingleton<IOptions<TenantDatabaseOptions>>(new OptionsWrapper<TenantDatabaseOptions>(options));

            // Default provisioner — NoOp (external actor manages)
            services.TryAddSingleton<ITenantDatabaseProvisioner, NoOpTenantProvisioner>();

            // Tenant migration orchestrator + its options, so MaxParallelism / ContinueOnFailure /
            // SuspendOnFailure / TenantTimeout are actually resolvable and can be overridden by the
            // host registering its own instance before this call.
            services.TryAddSingleton(new Pragmatic.Migrations.Tenant.TenantMigrationOptions());
            services.TryAddSingleton<Pragmatic.Migrations.Tenant.ITenantMigrationOrchestrator,
                Pragmatic.Migrations.Tenant.TenantMigrationOrchestrator>();

            // The composition of "the shared database and every tenant's", so an application choosing
            // WHEN to migrate N databases writes a call rather than the twenty lines both examples wrote.
            // Registered here because this is where the orchestrator it needs is registered.
            services.TryAddSingleton<Pragmatic.Migrations.Tenant.IDatabaseMigrationSweep,
                Pragmatic.Migrations.Tenant.DatabaseMigrationSweep>();

            // MT-H2 — per-request connection routing. The resolver is a singleton (shared TTL cache);
            // the interceptor is scoped so it sees the request's ITenantContext, and EF Core discovers it
            // from the app service provider and applies it to every DbContext.
            services.TryAddSingleton<TenantConnectionResolver>();
            services.TryAddEnumerable(ServiceDescriptor.Scoped<
                Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor, TenantConnectionInterceptor>());

            return services;
        }

        /// <summary>
        ///     Chooses the provisioner that issues <c>CREATE DATABASE</c> for a new tenant.
        ///     Requires a database provider (PostgreSQL, SQL Server).
        /// </summary>
        /// <remarks>
        ///     ⚠️ <b>"Automatic" names the DDL, not the moment.</b> Nothing calls the provisioner for
        ///     you: the application calls <c>ITenantDatabaseProvisioner.ProvisionAsync</c> where it
        ///     onboards a tenant. The name is what it is because the alternative — a provisioner that
        ///     fires on first access — was promised by an inert option and removed with it.
        ///     <para>
        ///         ⚠️ And it creates an <b>empty</b> database. Putting this service's schema in it is
        ///         <c>IMigrationRunner.MigrateAsync</c>, with the schema the host generates
        ///         (<c>{Database}Schema.Current</c>) — which is why the two are composed by the
        ///         application and not here: this assembly cannot name a type the host generates.
        ///     </para>
        /// </remarks>
        public IServiceCollection UseAutoProvision<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvisioner>()
            where TProvisioner : class, ITenantDatabaseProvisioner
        {
            services.RemoveAll<ITenantDatabaseProvisioner>();
            services.AddSingleton<ITenantDatabaseProvisioner, TProvisioner>();
            return services;
        }
    }
}
