// The module's own namespaces that most of its files read. The framework's come with its packages.
global using Casework.Verify.Dtos;
global using Casework.Verify.Entities;
// This service's own contract: the outcome is a type the entity, the DTO and the operation all name, and
// it lives in Casework.Verify.Contracts because it is what the two services say to each other.
// ⚠️ Not needed by the generated code, which qualifies it fully
// (`global::Casework.Verify.Events.VerificationOutcome`): it is here because hand-written files name the
// type. A CS0246 on that name in generated code is a stale generated file, cured by regenerating, not by
// a using.
global using Casework.Verify.Events;
