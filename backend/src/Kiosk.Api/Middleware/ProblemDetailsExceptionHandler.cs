using FluentValidation;
using Kiosk.Application.Common;
using Kiosk.Domain.Common;
using Kiosk.Infrastructure.Payments;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kiosk.Api.Middleware;

/// <summary>Maps known exceptions to RFC 7807 responses. Anything else is a 500 with no internals leaked.</summary>
internal sealed class ProblemDetailsExceptionHandler(IProblemDetailsService problems, ILogger<ProblemDetailsExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        ProblemDetails? problem = exception switch
        {
            ValidationException v => new ValidationProblemDetails(
                v.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
            {
                Status = StatusCodes.Status400BadRequest, Title = "The request is invalid.",
            },
            NotFoundException => new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = exception.Message },
            DomainException d => WithCode(new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = d.Message }, d.Code),
            DbUpdateConcurrencyException => WithCode(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict, Title = "The order changed while you were working on it. Refresh and try again.",
            }, "concurrent_update"),
            InvalidWebhookSignatureException => new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = exception.Message },
            HttpRequestException => new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway, Title = "The payment provider is unavailable. Try again or pay at the counter.",
            },
            _ => null,
        };

        if (problem is null)
            return false;

        if (problem.Status >= 500)
            logger.LogError(exception, "Upstream failure");

        http.Response.StatusCode = problem.Status!.Value;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem, Exception = exception });
    }

    private static ProblemDetails WithCode(ProblemDetails problem, string code)
    {
        problem.Extensions["code"] = code;
        return problem;
    }
}
