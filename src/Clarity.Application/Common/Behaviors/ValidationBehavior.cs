using Clarity.Application.Common.Exceptions;
using FluentValidation;
using MediatR;

namespace Clarity.Application.Common.Behaviors;

/// <summary>Runs FluentValidation validators before the handler. Throws ValidationException on failure.</summary>
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (!validators.Any()) return await next();

        var context = new ValidationContext<TRequest>(request);

        var failures = validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f != null)
            .GroupBy(f => f.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

        if (failures.Count != 0)
            throw new Exceptions.ValidationException(failures);

        return await next();
    }
}
