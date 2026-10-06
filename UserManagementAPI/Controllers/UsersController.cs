using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using UserManagementAPI.Models;
using UserManagementAPI.Services;

namespace UserManagementAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController(IUserService userService) : ControllerBase
{
    private const int MaxPageSize = 100;

    // GET: api/users?page=1&pageSize=50
    [HttpGet]
    [ProducesResponseType<IEnumerable<User>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public ActionResult<IEnumerable<User>> GetUsers(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, MaxPageSize)] int pageSize = 50)
    {
        var (items, totalCount) = userService.GetPage(page, pageSize);
        Response.Headers["X-Total-Count"] = totalCount.ToString();
        return Ok(items);
    }

    // GET: api/users/5
    [HttpGet("{id:int}")]
    [ProducesResponseType<User>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public ActionResult<User> GetUser(int id)
    {
        var user = userService.GetById(id);
        return user is null ? UserNotFound(id) : Ok(user);
    }

    // POST: api/users
    [HttpPost]
    [ProducesResponseType<User>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public ActionResult<User> CreateUser(UserRequest request)
    {
        var result = userService.Create(request);
        return result.Status switch
        {
            UserOperationStatus.Success => CreatedAtAction(nameof(GetUser), new { id = result.User!.Id }, result.User),
            UserOperationStatus.DuplicateEmail => DuplicateEmail(request.Email),
            _ => throw new InvalidOperationException($"Unexpected create status {result.Status}.")
        };
    }

    // PUT: api/users/5
    [HttpPut("{id:int}")]
    [ProducesResponseType<User>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public ActionResult<User> UpdateUser(int id, UserRequest request)
    {
        var result = userService.Update(id, request);
        return result.Status switch
        {
            UserOperationStatus.Success => Ok(result.User),
            UserOperationStatus.NotFound => UserNotFound(id),
            UserOperationStatus.DuplicateEmail => DuplicateEmail(request.Email),
            _ => throw new InvalidOperationException($"Unexpected update status {result.Status}.")
        };
    }

    // DELETE: api/users/5
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public IActionResult DeleteUser(int id) => userService.Delete(id) ? NoContent() : UserNotFound(id);

    private ObjectResult UserNotFound(int id) =>
        Problem(statusCode: StatusCodes.Status404NotFound, title: "User not found.", detail: $"User with ID {id} does not exist.");

    private ObjectResult DuplicateEmail(string email) =>
        Problem(statusCode: StatusCodes.Status409Conflict, title: "Email already in use.", detail: $"A user with email '{email.Trim()}' already exists.");
}
