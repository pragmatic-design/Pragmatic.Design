using Pragmatic.Mapping.EFCore.Tests.Dtos;
using Pragmatic.Mapping.EFCore.Tests.Entities;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.EFCore.Tests;

/// <summary>
///     How many columns end up in the <c>UPDATE</c> when only one changes.
/// </summary>
/// <remarks>
///     <para>
///         Publishing the set of written <b>properties</b>, not only navigations, might look like the
///         way to get a minimal <c>UPDATE</c> instead of one over every mapped column.
///     </para>
///     <para>
///         ⚠️ It is not needed: EF Core compares the original values of a <b>tracked</b> entity and
///         writes only what changed. The update is already minimal, so such a list would be a correct
///         mechanism nobody calls.
///     </para>
/// </remarks>
public class MinimalUpdateTests : PostgresTestBase
{
    /// <summary>
    ///     One property changed, a single column modified.
    /// </summary>
    [Fact]
    public async Task ApplyingADto_MarksOnlyWhatChanged()
    {
        var user = await Db.Users.FirstAsync();

        var dto = new UserScalarsWriteDto
        {
            Id = user.Id,
            Email = user.Email,
            FirstName = "cambiato",
            LastName = user.LastName,
        };

        dto.ApplyToLoaded(user);

        var modified = Db.Entry(user).Properties
            .Where(p => p.IsModified)
            .Select(p => p.Metadata.Name)
            .ToList();

        modified.Should().BeEquivalentTo(new[] { nameof(User.FirstName) },
            "EF Core compares the original values of a tracked entity: the UPDATE is already minimal, "
            + "and a list of written properties would have nobody calling it");
    }

    /// <summary>
    ///     ⚠️ The control: two properties changed, two columns modified.
    /// </summary>
    /// <remarks>
    ///     Without it, the case above would pass even if <c>ApplyToLoaded</c> wrote nothing — zero
    ///     modified is a subset of «only the one that changed», and it would be the same assertion with
    ///     the opposite meaning.
    /// </remarks>
    [Fact]
    public async Task WhenTwoChange_BothAreMarked()
    {
        var user = await Db.Users.Skip(1).FirstAsync();

        var dto = new UserScalarsWriteDto
        {
            Id = user.Id,
            Email = "nuova@example.com",
            FirstName = "cambiato",
            LastName = user.LastName,
        };

        dto.ApplyToLoaded(user);

        var modified = Db.Entry(user).Properties
            .Where(p => p.IsModified)
            .Select(p => p.Metadata.Name)
            .ToList();

        modified.Should().BeEquivalentTo(new[] { nameof(User.Email), nameof(User.FirstName) });
    }
}
