using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.Extensions;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Tests.Unit;

/// <summary>
///     Everything an application writes through <c>EnableOutbox(o => …)</c> has to arrive where the
///     services that act on it read it: <see cref="IOptions{TOptions}" /> of
///     <see cref="MessagingOptions" />.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <c>MessagingBuilder.EnableOutbox</c> copies the five properties of <c>OutboxOptions</c>
///         onto the builder's own options, and <c>AddPragmaticMessaging</c> copies them again into the
///         registered <c>MessagingOptions</c>. A property left out of the second hop is dropped
///         silently: an application that asks for a retention of twelve hours gets the default of three
///         days, because <c>OutboxPurgeService</c> reads both from
///         <c>IOptions&lt;MessagingOptions&gt;</c> and finds nothing there but the default.
///     </para>
///     <para>
///         An exclusion is invisible in the result — a test that configures and asserts only some of
///         the properties reports nothing about the others.
///     </para>
///     <para>
///         ⚠️ Each property is named here <b>one by one</b>, and that is on purpose rather than for
///         want of a loop: a property added to <c>OutboxOptions</c> has to be added to the copy in
///         <c>AddPragmaticMessaging</c> and to this test, and the second copy is what makes the first
///         one's omission visible. The coupling itself is the defect; see the remark on the last test.
///     </para>
/// </remarks>
public class TheOutboxRetentionAnApplicationConfiguresTests
{
    [Fact]
    public void EnableOutbox_WithEveryOutboxOption_PutsAllOfThemWhereTheServicesReadThem()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticMessaging(msg =>
        {
            msg.UseInMemory();
            msg.EnableOutbox(o =>
            {
                o.PollingIntervalSeconds = 11;
                o.BatchSize = 42;
                o.MaxRetries = 2;
                o.Retention = TimeSpan.FromHours(12);
                o.PurgeInterval = TimeSpan.FromMinutes(5);
            });
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MessagingOptions>>().Value;

        options.OutboxEnabled.Should().BeTrue();
        options.PollingIntervalSeconds.Should().Be(11);
        options.BatchSize.Should().Be(42);
        options.MaxRetries.Should().Be(2);
        options.OutboxRetention.Should().Be(TimeSpan.FromHours(12),
            "OutboxPurgeService deletes delivered rows older than this, and an application that asked "
            + "for twelve hours keeping three days is a table growing for a reason nobody can see");
        options.OutboxPurgeInterval.Should().Be(TimeSpan.FromMinutes(5),
            "and it sweeps on this interval, which is the other half of the same decision");
    }

    /// <summary>
    ///     The control: the two values above are not the defaults, so the assertion measures the copy
    ///     and not a coincidence.
    /// </summary>
    [Fact]
    public void AddPragmaticMessaging_WithNoOutboxConfiguration_LeavesTheRetentionDefaults()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticMessaging();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MessagingOptions>>().Value;

        options.OutboxRetention.Should().Be(TimeSpan.FromDays(3));
        options.OutboxPurgeInterval.Should().Be(TimeSpan.FromHours(1));
    }

    /// <summary>
    ///     ⚠️ And the one that will still be true for the <b>next</b> option: every writable property of
    ///     <see cref="OutboxOptions" /> arrives in <see cref="MessagingOptions" />, whatever they are.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The two tests above name five properties — but a sixth added tomorrow would be dropped in
    ///         exactly the same place and those two tests would still pass. So this one asks the <b>type</b> instead of a list:
    ///         every property it finds gets a value that is not its default, and the counterpart in
    ///         <c>MessagingOptions</c> has to carry it.
    ///     </para>
    ///     <para>
    ///         The counterpart is found by the convention the copy already follows — the same name, or
    ///         that name with an <c>Outbox</c> prefix (<c>Retention</c> → <c>OutboxRetention</c>). A
    ///         property that follows neither fails here too, which is right: it is the copy in
    ///         <c>AddPragmaticMessaging</c> that has to say where the value goes, and this is the place
    ///         that notices it did not.
    ///     </para>
    ///     <para>
    ///         ⚠️ A property of a type this test cannot vary <b>fails</b> rather than being skipped.
    ///         Skipping it would rebuild the silence the whole file is about: an exclusion is invisible
    ///         in a green result.
    ///     </para>
    ///     <para>
    ///         It does not make the coupling go away — three places still have to agree, and the shapes
    ///         that would remove it (binding <c>OutboxOptions</c> into DI, or holding one inside
    ///         <c>MessagingOptions</c>) change the public surface and are the owner's call. What it does
    ///         is make the next omission loud on the day it is written instead of in an application's
    ///         growing table.
    ///     </para>
    /// </remarks>
    [Fact]
    public void EveryOptionOfTheOutbox_ReachesTheOptionsTheServicesRead()
    {
        var properties = typeof(OutboxOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property is { CanRead: true, CanWrite: true })
            .ToList();

        properties.Should().NotBeEmpty("this test measures nothing if the type has no options");

        var defaults = new OutboxOptions();
        var asked = properties.ToDictionary(
            property => property,
            property => SomethingOtherThanTheDefault(property, property.GetValue(defaults)));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticMessaging(msg =>
        {
            msg.UseInMemory();
            msg.EnableOutbox(o =>
            {
                foreach (var (property, value) in asked)
                    property.SetValue(o, value);
            });
        });

        using var provider = services.BuildServiceProvider();
        var registered = provider.GetRequiredService<IOptions<MessagingOptions>>().Value;

        var lost = new List<string>();
        foreach (var (property, value) in asked)
        {
            var counterpart = typeof(MessagingOptions).GetProperty(property.Name)
                              ?? typeof(MessagingOptions).GetProperty("Outbox" + property.Name);

            if (counterpart is null)
            {
                lost.Add(
                    $"{property.Name}: MessagingOptions has neither '{property.Name}' nor "
                    + $"'Outbox{property.Name}', so nothing the services read can carry it");
                continue;
            }

            var arrived = counterpart.GetValue(registered);
            if (!Equals(arrived, value))
                lost.Add(
                    $"{property.Name}: asked for {value}, and MessagingOptions.{counterpart.Name} is "
                    + $"{arrived} — AddPragmaticMessaging copies the outbox options one by one and this "
                    + "one is not in the list");
        }

        lost.Should().BeEmpty();
    }

    /// <summary>
    ///     A value of the property's own type that is not what a fresh <see cref="OutboxOptions" /> holds.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Throws on a type it does not know rather than returning the default, which would make the
    ///     property look carried when nothing carried it.
    /// </remarks>
    private static object SomethingOtherThanTheDefault(PropertyInfo property, object? current) => current switch
    {
        int number => number + 7,
        TimeSpan span => span + TimeSpan.FromHours(1),
        bool flag => !flag,
        string text => text + "-changed",
        _ => throw new NotSupportedException(
            $"OutboxOptions.{property.Name} is a {property.PropertyType.Name}, and this test does not "
            + "know how to give it a value other than its default. Teach it here — skipping the property "
            + "would leave it uncovered and the result green, which is the shape of defect this file "
            + "exists for.")
    };
}
