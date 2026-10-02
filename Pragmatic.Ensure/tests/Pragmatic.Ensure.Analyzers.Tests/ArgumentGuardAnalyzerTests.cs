using Microsoft.CodeAnalysis.Testing;
using Xunit;
using AnalyzerVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Pragmatic.Ensure.Analyzers.ArgumentGuardAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Ensure.Analyzers.Tests;

/// <summary>
///     Tests for <see cref="ArgumentGuardAnalyzer" /> (PRAG0100).
/// </summary>
/// <remarks>
///     The rule "use Ensure, never a hand-written argument guard" was in the coding standard with
///     nothing enforcing it, and the repository held 76 hand-written guards across 52 files while the
///     standard said otherwise. A construct is only the default when something fails without it.
/// </remarks>
public class ArgumentGuardAnalyzerTests
{
    private static Task VerifyAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            ArgumentGuardAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        };

        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync();
    }

    [Fact]
    public async Task CoalesceThrow_IsReported()
    {
        const string source = """
            using System;

            namespace App;

            public class Service
            {
                private readonly string _name;
                public Service(string name) => _name = name ?? throw new ArgumentNullException(nameof(name));
            }
            """;

        await VerifyAsync(source,
            AnalyzerVerifier.Diagnostic("PRAG0100").WithSpan(8, 52, 8, 97)
                .WithArguments("name", "ArgumentNullException", "ThrowIfNull"));
    }

    [Fact]
    public async Task IfNullThrow_IsReported()
    {
        const string source = """
            using System;

            namespace App;

            public class Service
            {
                public void Run(string name)
                {
                    if (name is null)
                        throw new ArgumentNullException(nameof(name));
                }
            }
            """;

        await VerifyAsync(source,
            AnalyzerVerifier.Diagnostic("PRAG0100").WithSpan(10, 13, 10, 59)
                .WithArguments("name is null", "ArgumentNullException", "ThrowIfNull"));
    }

    [Fact]
    public async Task AGuardInsideBraces_IsReported()
    {
        const string source = """
            using System;

            namespace App;

            public class Service
            {
                public void Run(string name)
                {
                    if (string.IsNullOrEmpty(name))
                    {
                        throw new ArgumentException("empty", nameof(name));
                    }
                }
            }
            """;

        await VerifyAsync(source,
            AnalyzerVerifier.Diagnostic("PRAG0100").WithSpan(11, 13, 11, 64)
                .WithArguments("string.IsNullOrEmpty(name)", "ArgumentException", "ThrowIfNullOrWhiteSpace"));
    }

    /// <summary>
    ///     A throw that is not about validating an argument is none of Ensure's business.
    /// </summary>
    /// <remarks>
    ///     The test is the exception type, deliberately. An analyzer that flagged every guard-shaped
    ///     throw would fire on state checks and domain rules, and an analyzer people learn to suppress
    ///     enforces nothing at all.
    /// </remarks>
    [Fact]
    public async Task AStateCheck_IsNotReported()
    {
        const string source = """
            using System;

            namespace App;

            public class Service
            {
                private bool _started;

                public void Run()
                {
                    if (!_started)
                        throw new InvalidOperationException("not started");
                }
            }
            """;

        await VerifyAsync(source);
    }

    /// <summary>
    ///     An <c>if</c> with an <c>else</c> is control flow, not a guard.
    /// </summary>
    /// <remarks>
    ///     Rewriting it as an <c>Ensure</c> call would change what the method does, so the analyzer
    ///     must not suggest it — a suggestion that is wrong when followed is worse than silence.
    /// </remarks>
    [Fact]
    public async Task AThrowWithAnElseBranch_IsNotReported()
    {
        const string source = """
            using System;

            namespace App;

            public class Service
            {
                public int Run(string name)
                {
                    if (name is null)
                        throw new ArgumentNullException(nameof(name));
                    else
                        return name.Length;
                }
            }
            """;

        await VerifyAsync(source);
    }

    /// <summary>
    ///     Ensure's own throws are the implementation of the rule, not a bypass of it.
    /// </summary>
    [Fact]
    public async Task InsideEnsureItself_IsNotReported()
    {
        const string source = """
            using System;

            namespace Pragmatic.Ensure;

            public static class Guard
            {
                public static string ThrowIfNull(string? value)
                    => value ?? throw new ArgumentNullException(nameof(value));
            }
            """;

        await VerifyAsync(source);
    }
}
