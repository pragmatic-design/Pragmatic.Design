using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pragmatic.Composition;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Identity.Local.Jwt.Tests.Unit;

/// <summary>
///     <c>UseJwtAuthentication()</c> without arguments reads the <c>Jwt</c> section — <c>Key</c>,
///     <c>Issuer</c>, <c>Audience</c>, and optionally <c>TokenExpiration</c>, <c>ClockSkew</c>,
///     <c>RequireSecurityStamp</c> — which is what every host that used JWT wrote by hand.
/// </summary>
public class TheTokenSettingsComeFromConfigurationTests
{
    private const string Key = "configured-signing-key-at-least-32-bytes!";

    [Fact]
    public void TheSection_ConfiguresTheOptionsAndTheValidation()
    {
        var provider = Configure(new()
        {
            ["Jwt:Key"] = Key,
            ["Jwt:Issuer"] = "https://time-off.example",
            ["Jwt:Audience"] = "time-off",
            ["Jwt:TokenExpiration"] = "00:20:00",
            ["Jwt:ClockSkew"] = "00:00:30",
            ["Jwt:RequireSecurityStamp"] = "false"
        });

        var options = provider.GetRequiredService<IOptions<JwtOptions>>().Value;
        options.SigningKey.Should().Be(Key);
        options.Issuer.Should().Be("https://time-off.example");
        options.Audience.Should().Be("time-off");
        options.TokenExpiration.Should().Be(TimeSpan.FromMinutes(20));
        options.ClockSkew.Should().Be(TimeSpan.FromSeconds(30));
        options.RequireSecurityStamp.Should().BeFalse();

        var validation = Bearer(provider).TokenValidationParameters;
        validation.ValidIssuer.Should().Be("https://time-off.example", "the handler validates what the section says");
        validation.ValidAudience.Should().Be("time-off");
    }

    /// <summary>What the section leaves out keeps the defaults of <see cref="JwtOptions" />.</summary>
    [Fact]
    public void WhatTheSectionLeavesOut_KeepsTheDefaults()
    {
        var options = Configure(new() { ["Jwt:Key"] = Key })
            .GetRequiredService<IOptions<JwtOptions>>().Value;

        options.TokenExpiration.Should().Be(TimeSpan.FromHours(1));
        options.ClockSkew.Should().Be(TimeSpan.FromMinutes(1));
        options.RequireSecurityStamp.Should().BeTrue();
        options.Issuer.Should().BeNull();
    }

    [Fact]
    public void WithoutAKey_TheHostDoesNotStart_AndTheMessageNamesTheKey()
    {
        var configure = () => Configure(new() { ["Jwt:Issuer"] = "https://time-off.example" });

        configure.Should().Throw<InvalidOperationException>().WithMessage("*Jwt:Key*");
    }

    [Fact]
    public void AValueThatIsNotATimeSpan_IsRefusedByName()
    {
        var configure = () => Configure(new() { ["Jwt:Key"] = Key, ["Jwt:TokenExpiration"] = "an hour" });

        configure.Should().Throw<InvalidOperationException>().WithMessage("*Jwt:TokenExpiration*");
    }

    [Fact]
    public void AnotherSection_IsReadWhenNamed()
    {
        var options = Configure(new() { ["Auth:Tokens:Key"] = Key }, "Auth:Tokens")
            .GetRequiredService<IOptions<JwtOptions>>().Value;

        options.SigningKey.Should().Be(Key);
    }

    private static ServiceProvider Configure(Dictionary<string, string?> values, string? section = null)
    {
        var builder = new FakeBuilder(values);
        if (section is null)
            builder.UseJwtAuthentication();
        else
            builder.UseJwtAuthentication(section);
        return builder.Services.BuildServiceProvider();
    }

    private static JwtBearerOptions Bearer(ServiceProvider provider)
        => provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

    private sealed class FakeBuilder(Dictionary<string, string?> values) : IPragmaticBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();
        public IConfiguration Configuration { get; } = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        public IHostEnvironment Environment { get; } = new FakeEnvironment();
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
