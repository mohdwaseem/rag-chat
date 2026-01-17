using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace RAGDemoBackend.Services;

public sealed class ApiExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ApiExceptionHandler> _logger;

    public ApiExceptionHandler(ILogger<ApiExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Validation error"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            NotImplementedException => (StatusCodes.Status501NotImplemented, "Not implemented"),
            _ => (StatusCodes.Status500InternalServerError, "Server error")
        };

        // Avoid leaking internal exception details to clients.
        _logger.LogError(exception, "Unhandled exception. TraceId={TraceId}", httpContext.TraceIdentifier);

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Instance = httpContext.Request.Path
        };

        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
