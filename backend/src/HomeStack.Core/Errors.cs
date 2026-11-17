using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HomeStack.Core;

/// <summary>Throw from services; the host turns these into ProblemDetails responses.</summary>
public class HomeStackException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
public class NotFoundException(string what = "Not found") : HomeStackException(StatusCodes.Status404NotFound, what);
public class BadRequestException(string message) : HomeStackException(StatusCodes.Status400BadRequest, message);
public class ConflictException(string message) : HomeStackException(StatusCodes.Status409Conflict, message);
public class ForbiddenException(string message) : HomeStackException(StatusCodes.Status403Forbidden, message);

public sealed class HomeStackExceptionHandler(IProblemDetailsService problems, ILogger<HomeStackExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        if (ex is not HomeStackException hs) return false;
        // 4xx are the user's to fix; 5xx (e.g. the AI service failing) belong in the log too.
        if (hs.StatusCode >= 500) logger.LogWarning("{Method} {Path} failed ({Status}): {Message}", http.Request.Method, http.Request.Path, hs.StatusCode, hs.Message);
        http.Response.StatusCode = hs.StatusCode;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails { Status = hs.StatusCode, Title = hs.Message },
            Exception = ex,
        });
    }
}
