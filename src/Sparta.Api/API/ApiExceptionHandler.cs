using System.ComponentModel.DataAnnotations;
using DevExpress.EntityFrameworkCore.Security;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace Sparta.WebApi;

public sealed class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var status = exception switch
        {
            DevExpress.Persistent.Validation.ValidationException => StatusCodes.Status400BadRequest,
            ValidationException => StatusCodes.Status400BadRequest,
            EFCoreSecurityException => StatusCodes.Status403Forbidden,
            DbUpdateConcurrencyException => StatusCodes.Status409Conflict,
            DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 or 547 } } => StatusCodes.Status409Conflict,
            _ => 0
        };

        if(status == 0)
            return false;
        context.Response.StatusCode = status;
        var detail = exception switch
        {
            DevExpress.Persistent.Validation.ValidationException => exception.Message,
            ValidationException => exception.Message,
            DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } => "This code already exists. Enter a unique code.",
            DbUpdateException { InnerException: SqlException { Number: 547 } } => "This record is referenced by other records or contains an unavailable reference.",
            _ => null
        };
        await Results.Problem(
            statusCode: status,
            detail: detail,
            title: status switch
            {
                400 => "Business validation failed.",
                403 => "The operation is not permitted.",
                _ => "The record changed. Reload and retry."
            },
            extensions: new Dictionary<string, object?>
            {
                ["traceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString()
            })
            .ExecuteAsync(context);
        return true;
    }
}
