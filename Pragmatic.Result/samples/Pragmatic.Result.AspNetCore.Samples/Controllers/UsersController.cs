using Microsoft.AspNetCore.Mvc;
using Pragmatic.Result.AspNetCore.Samples.Services;
using Pragmatic.Result.Http;

namespace Pragmatic.Result.AspNetCore.Samples.Controllers;

/// <summary>
///     Sample controller demonstrating automatic Result handling with MVC controllers.
///     Actions return Result types directly - the global ResultActionFilter converts to HTTP responses.
/// </summary>
[ApiController]
[Route("api/controller/users")]
public class UsersController(UserService userService) : ControllerBase
{
    /// <summary>
    ///     Gets a user by ID.
    ///     Returns Result&lt;User, NotFoundError&gt; → 200 OK or 404 ProblemDetails
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType<User>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<Result<User, NotFoundError>> Get(int id)
    {
        return await userService.GetByIdAsync(id);
    }

    /// <summary>
    ///     Creates a new user.
    ///     Returns Result&lt;User, ConflictError&gt; → 200 OK or 409 ProblemDetails
    /// </summary>
    [HttpPost]
    [ProducesResponseType<User>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<Result<User, ConflictError>> Create([FromBody] CreateUserRequest request)
    {
        return await userService.CreateAsync(request);
    }

    /// <summary>
    ///     Updates a user.
    ///     Returns Result&lt;User, NotFoundError&gt; → 200 OK or 404 ProblemDetails
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType<User>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<Result<User, NotFoundError>> Update(int id, [FromBody] UpdateUserRequest request)
    {
        return await userService.UpdateAsync(id, request);
    }

    /// <summary>
    ///     Deletes a user.
    ///     Returns VoidResult&lt;NotFoundError&gt; → 204 No Content or 404 ProblemDetails
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<VoidResult<NotFoundError>> Delete(int id)
    {
        return await userService.DeleteAsync(id);
    }
}