using Chat.Application.DTOs.Auth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Chat.Api.Filters;

/// <summary>
/// Validates antiforgery tokens for unsafe HTTP methods and returns the API's
/// standard JSON error instead of MVC's empty AntiforgeryValidationFailedResult.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ValidateAntiforgeryJsonAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method)
            || HttpMethods.IsHead(method)
            || HttpMethods.IsOptions(method)
            || HttpMethods.IsTrace(method))
        {
            return;
        }

        var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = new ObjectResult(new AuthErrorResponse
            {
                Code = "INVALID_CSRF_TOKEN",
                Message = "CSRF token không hợp lệ hoặc đã hết hạn."
            })
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
        }
    }
}
