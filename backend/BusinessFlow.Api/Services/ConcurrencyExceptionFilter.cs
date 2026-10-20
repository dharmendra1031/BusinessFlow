using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Services;

public sealed class ConcurrencyExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not DbUpdateConcurrencyException && !SqlConflicts.IsRetryable(context.Exception)) return;
        context.Result = new ConflictObjectResult(new { message = "This record changed. Reload the latest record before retrying." });
        context.ExceptionHandled = true;
    }
}
