using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HomeStack.Core;

/// <summary>Throw from services; the host turns these into ProblemDetails responses.</summary>
public class HomeStackException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
public class NotFoundException(string what = "Not found") : HomeStackException(StatusCodes.Status404NotFound, what);
public class BadRequestException(string message) : HomeStackException(StatusCodes.Status400BadRequest, message);
public class ConflictException(string message) : HomeStackException(StatusCodes.Status409Conflict, message);

public sealed class HomeStackExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        if (ex is not HomeStackException hs) return false;
        http.Response.StatusCode = hs.StatusCode;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails { Status = hs.StatusCode, Title = hs.Message },
            Exception = ex,
        });
    }
}
