using Pragmatic.Integration.Tests.Domain.Errors;
using Pragmatic.Result;
using ConflictError = Pragmatic.Result.Http.ConflictError;

namespace Pragmatic.Integration.Tests.Unions;

/// <summary>
///     The error set of placing an order, as a C# 15 union. It declares <c>IError</c> and writes none of its members: the generator writes
///     them, each delegated to the case the union holds (#865).
/// </summary>
public partial union PlaceOrderError(NotFoundError, ConflictError, OrderLocked) : IError;
