using Pragmatic.Resilience.Errors;
using Pragmatic.Testing.Assertions;

// Every type this suite compares member by member. The generated BeEquivalentTo names the member
// that differs, which is what the failure message could not say before.
[assembly: GenerateComparer<CircuitBrokenError>]
[assembly: GenerateComparer<BulkheadRejectedError>]
[assembly: GenerateComparer<TimeoutError>]
[assembly: GenerateComparer<RateLimitRejectedError>]
