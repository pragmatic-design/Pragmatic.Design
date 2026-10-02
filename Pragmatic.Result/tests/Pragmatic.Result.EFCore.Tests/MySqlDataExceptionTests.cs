using System.Data.Common;
using System.Reflection;
using Pragmatic.Result.EntityFrameworkCore.MySql;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Result.EntityFrameworkCore.Tests;

/// <summary>
///     The MySQL parser reads the exceptions of <c>MySql.Data</c>, Oracle's driver, as it reads
///     MySqlConnector's.
/// </summary>
/// <remarks>
///     <para>
///         A host that declares <c>DatabaseProvider.MySql</c> runs on Oracle's EF provider, the only one on
///         EF Core 10, and that provider throws <c>MySql.Data.MySqlClient.MySqlException</c>. The parser
///         recognised only MySqlConnector's type, so a duplicate key or a foreign-key violation on such a
///         host would have mapped to nothing.
///     </para>
///     <para>
///         The parser does not reference <c>MySql.Data</c> (GPL-2.0): it reads the error number the driver
///         writes into <see cref="Exception.Data" /> under <c>"Server Error Code"</c>, which every one of
///         its constructors that takes a server error number does.
///     </para>
///     <para>
///         The exception has no public constructor and cannot be produced without a server, so the test
///         builds it through the internal <c>(string message, int errno)</c> one — the constructor the
///         driver uses for a server error.
///     </para>
/// </remarks>
public class MySqlDataExceptionTests
{
    private static DbException MySqlDataException(string message, int errno)
        => (DbException)Activator.CreateInstance(
            typeof(global::MySql.Data.MySqlClient.MySqlException),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [message, errno],
            culture: null)!;

    [Fact]
    public void ADuplicateEntry_IsAUniqueConstraint()
    {
        var exception = MySqlDataException("Duplicate entry 'a@b.c' for key 'IX_Users_Email'", 1062);

        MySqlExceptionParser.Instance.CanParse(exception).Should().BeTrue();
        var error = MySqlExceptionParser.Instance.Parse(exception)!.Value;
        error.ErrorType.Should().Be(DbErrorType.UniqueConstraint);
        error.ConstraintName.Should().Be("IX_Users_Email");
        error.ErrorCode.Should().Be(1062);
    }

    /// <summary>Wrapped as EF Core wraps it, the way it reaches the parser from SaveChanges.</summary>
    [Fact]
    public void AForeignKeyViolation_IsFound_InsideTheExceptionThatWrapsIt()
    {
        var driver = MySqlDataException(
            "Cannot add or update a child row: a foreign key constraint fails (`shop`.`orders`, CONSTRAINT `FK_Orders_Customers` FOREIGN KEY (`CustomerId`))",
            1452);
        var wrapped = new InvalidOperationException("An error occurred while saving the entity changes.", driver);

        var error = MySqlExceptionParser.Instance.Parse(wrapped)!.Value;
        error.ErrorType.Should().Be(DbErrorType.ForeignKeyConstraint);
        error.ConstraintName.Should().Be("FK_Orders_Customers");
    }

    /// <summary>The control: a database exception that is not MySQL's is not taken for one.</summary>
    [Fact]
    public void AnotherDatabaseException_IsNotParsed()
    {
        MySqlExceptionParser.Instance.CanParse(new OtherDbException()).Should().BeFalse();
        MySqlExceptionParser.Instance.Parse(new OtherDbException()).Should().BeNull();
    }

    /// <summary>The control: the key alone is not enough when its value is not an error number.</summary>
    [Fact]
    public void AKeyWithoutANumber_IsNotParsed()
    {
        var exception = new OtherDbException();
        exception.Data["Server Error Code"] = "1062";

        MySqlExceptionParser.Instance.CanParse(exception).Should().BeFalse();
    }

    private sealed class OtherDbException : DbException;
}
