namespace Pragmatic.SourceGenerator.Features.Result.Models;

/// <summary>The members of <c>Pragmatic.Result.IError</c>, as a set: those a union error set leaves for the generator to write.</summary>
[System.Flags]
internal enum UnionErrorMembers
{
    None = 0,
    Code = 1,
    StatusCode = 2,
    Title = 4,
    Description = 8,

    /// <summary><c>WriteExtensions(IDictionary&lt;string, object?&gt;)</c>.</summary>
    WriteExtensions = 16,

    /// <summary><c>WriteExtensions(IDictionary&lt;string, object?&gt;, IErrorMessageResolver?)</c>.</summary>
    WriteResolvedExtensions = 32,

    All = Code | StatusCode | Title | Description | WriteExtensions | WriteResolvedExtensions,
}
