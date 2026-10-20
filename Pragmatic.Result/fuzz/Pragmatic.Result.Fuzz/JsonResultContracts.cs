using Pragmatic.Result;
using Pragmatic.Result.Http;
using Pragmatic.Result.Serialization;

// The result shapes the harness reads: one value with two declared errors, one with three, and a void result. Declaring
// them is what makes the generator emit a converter for each.
[assembly: JsonResultContract<Result<int, NotFoundError, ConflictError>>]
[assembly: JsonResultContract<Result<string, BadRequestError, NotFoundError, ConflictError>>]
[assembly: JsonResultContract<VoidResult<NotFoundError, ConflictError>>]
