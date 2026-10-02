using Pragmatic.Testing.Assertions;

namespace Pragmatic.Cryptography.Tests.Unit;

public sealed class EnvironmentEncryptionKeyProviderTests : IDisposable
{
    private static readonly byte[] RawKey = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static string Base64Key => Convert.ToBase64String(RawKey);

    private readonly List<string> _envVarsToClear = [];

    [Fact]
    public async Task Environment_WithVariableSet_DecodesBase64()
    {
        var varName = SetEnv(Base64Key);
        var provider = new EnvironmentEncryptionKeyProvider(varName);

        var key = await provider.GetKeyAsync();

        key.Should().Equal(RawKey);
    }

    [Fact]
    public async Task Environment_VariableMissing_Throws()
    {
        var varName = "PRAGMATIC_TEST_MISSING_" + Guid.NewGuid().ToString("N");
        var provider = new EnvironmentEncryptionKeyProvider(varName);

        var act = async () => await provider.GetKeyAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Environment_InvalidBase64_Throws()
    {
        var varName = SetEnv("@@@not-base64@@@");
        var provider = new EnvironmentEncryptionKeyProvider(varName);

        var act = async () => await provider.GetKeyAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Environment_BlankVariableName_ThrowsArgument(string? name)
    {
        var act = () => new EnvironmentEncryptionKeyProvider(name!);

        act.Should().Throw<ArgumentException>();
    }

    private string SetEnv(string value)
    {
        var name = "PRAGMATIC_TEST_KEY_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name, value);
        _envVarsToClear.Add(name);
        return name;
    }

    public void Dispose()
    {
        foreach (var name in _envVarsToClear)
            Environment.SetEnvironmentVariable(name, null);
    }
}
