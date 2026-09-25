using KromicCommerce.Domain.Common;

namespace KromicCommerce.Api.Extensions;

/// <summary>
/// Maps domain Error to the appropriate HTTP status code and JSON error envelope,
/// matching the API Standards in docs/16-API-Standards.md.
/// </summary>
internal static class ErrorExtensions
{
    internal static IActionResult ToActionResult(this Error error)
    {
        var body = new
        {
            success = false,
            error = new { code = error.Code, message = error.Description }
        };

        return error.Type switch
        {
            ErrorType.NotFound => new NotFoundObjectResult(body),
            ErrorType.Validation => new BadRequestObjectResult(body),
            ErrorType.Conflict => new ConflictObjectResult(body),
            ErrorType.Unauthorized => new UnauthorizedObjectResult(body),
            ErrorType.Forbidden => new ObjectResult(body) { StatusCode = StatusCodes.Status403Forbidden },
            _ => new ObjectResult(body) { StatusCode = StatusCodes.Status500InternalServerError }
        };
    }
}
