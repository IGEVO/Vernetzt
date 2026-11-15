using FluentValidation;
using Igevo.Vernetzt.Core.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Igevo.Vernetzt.Server.Security;

/// <summary>
/// Runs the registered FluentValidation validators for every action argument before the action executes.
/// The client-side form rules are a convenience only; this filter is what enforces the input rules
/// (formats, lengths, URL schemes, ...) for every API caller.
/// </summary>
public sealed class FluentValidationActionFilter : IAsyncActionFilter
{
    public const string ErrorCode = "VALIDATION_ERROR";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var cancellationToken = context.HttpContext.RequestAborted;
        var errors = new List<string>();

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null || argument is CancellationToken)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            foreach (var validator in services.GetServices(validatorType).OfType<IValidator>())
            {
                var result = await validator.ValidateAsync(new ValidationContext<object>(argument), cancellationToken);
                errors.AddRange(result.Errors.Select(e => e.ErrorMessage));
            }
        }

        if (errors.Count > 0)
        {
            var distinct = errors.Distinct().ToList();
            context.Result = new BadRequestObjectResult(
                ApiResponse<object>.CreateError(string.Join(" ", distinct), ErrorCode, distinct));
            return;
        }

        await next();
    }
}
