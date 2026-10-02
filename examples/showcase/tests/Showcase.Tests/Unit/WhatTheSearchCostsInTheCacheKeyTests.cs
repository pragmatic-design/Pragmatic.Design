using Pragmatic.Testing.Assertions;
using Showcase.Catalog.Properties.Queries;
using Xunit;

namespace Showcase.Tests.Unit;

/// <summary>
///     <c>[CacheKey]</c>: what a cacheable search actually puts in the key space.
/// </summary>
/// <remarks>
///     <para>
///         Every public property of a <c>[Cacheable]</c> query contributes a <c>Name=value</c>
///         segment, and the complex filter contributes one per nested member. With the property
///         names, <c>SearchPropertiesQuery</c>'s key was about <b>1 200 characters</b> — paid on
///         every read and every write, by a caller who typically sets one filter.
///     </para>
///     <para>
///         ⚠️ Nothing here can be left out: each property changes the answer, sorting and paging
///         included. Short labels are the whole remedy, which is why <c>Name</c> is declared on all
///         of them rather than on a chosen few.
///     </para>
/// </remarks>
public class WhatTheSearchCostsInTheCacheKeyTests
{
    [Fact]
    public void TheKey_OpensWithTheFilterTheSearchIsUsedWith()
    {
        var key = new SearchPropertiesQuery { City = "Rome" }.GetCacheKey();

        key.Should().Contain(":city=Rome:",
            "City declares Order = 0, so it comes before the properties that declare no order");
        key.IndexOf(":city=", StringComparison.Ordinal).Should().BeLessThan(
            key.IndexOf(":n=", StringComparison.Ordinal),
            "and 'first' means first, not first among the ones that follow it in the source");
    }

    [Fact]
    public void TheKey_IsShortEnoughToBelongInAKeySpace()
    {
        var key = new SearchPropertiesQuery { City = "Rome" }.GetCacheKey();

        key.Length.Should().BeLessThan(200,
            "the labels are the only thing that shortens it — with the property names this same "
            + $"key was about 1 200 characters, and it is now {key.Length}");
    }

    /// <summary>
    ///     The control: shortening the labels did not make two different searches share an answer.
    /// </summary>
    /// <remarks>
    ///     A cache key exists to tell requests apart, and two-letter labels are exactly the change
    ///     that can collapse them — <c>n</c> and <c>cc</c> and <c>sn</c> have to stay distinct, and
    ///     a value moving from one segment to another must still change the key. Without this, "the
    ///     key is short" is satisfied by a key that says almost nothing.
    /// </remarks>
    [Fact]
    public void TwoDifferentSearches_StillHaveDifferentKeys()
    {
        var byCity = new SearchPropertiesQuery { City = "Rome" }.GetCacheKey();
        var byName = new SearchPropertiesQuery { Name = "Rome" }.GetCacheKey();
        var byCountry = new SearchPropertiesQuery { Country = "Rome" }.GetCacheKey();
        var secondPage = new SearchPropertiesQuery { City = "Rome", Page = 2 }.GetCacheKey();

        new[] { byCity, byName, byCountry, secondPage }.Distinct().Should().HaveCount(4,
            "the same word in four different places is four different searches");
    }
}
