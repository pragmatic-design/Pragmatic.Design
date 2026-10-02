using Pragmatic.Result;
using Pragmatic.Result.Http;
using Pragmatic.Result.Serialization;
using Pragmatic.Result.Tests.Unit;

// The result shapes MultiErrorSerializationTests round-trips. Declaring them is what makes the
// generator emit a typed converter for each; there is nothing for it to infer from, because nothing in
// the framework puts a Result on the wire as a Result.
[assembly: JsonResultContract<Result<int, NotFoundError, ConflictError>>]
[assembly: JsonResultContract<Result<int, BadRequestError, NotFoundError, ConflictError>>]
[assembly: JsonResultContract<Result<int, UnregisteredError, ConflictError>>]
[assembly: JsonResultContract<VoidResult<NotFoundError, ConflictError>>]
