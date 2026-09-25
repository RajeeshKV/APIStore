namespace KromicCommerce.Application.Behaviors;

/// <summary>
/// MediatR pipeline behavior that runs all registered FluentValidation validators
/// for a request before the handler executes.
/// Returns a validation Result failure instead of throwing when validators exist and fail.
/// </summary>
internal sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);

        var failures = validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count == 0)
            return await next();

        // Build a single compound validation error. The first failure's PropertyName
        // is used as the error code; the full list is joined into the description.
        var code = $"VALIDATION_{failures[0].PropertyName.ToUpperInvariant()}";
        var description = string.Join(" | ", failures.Select(f => f.ErrorMessage));
        var error = Error.Validation(code, description);

        // TResponse must be a Result or Result<T> — if not, throw so the developer
        // notices the misconfiguration early.
        if (typeof(TResponse) == typeof(Result))
            return (TResponse)(object)Result.Failure(error);

        var resultType = typeof(TResponse);
        if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var valueType = resultType.GetGenericArguments()[0];
            var failureMethod = typeof(Result)
                .GetMethod(nameof(Result.Failure), 1, [typeof(Error)])!
                .MakeGenericMethod(valueType);
            return (TResponse)failureMethod.Invoke(null, [error])!;
        }

        throw new InvalidOperationException(
            $"{nameof(ValidationBehavior<TRequest, TResponse>)} only supports handlers that return Result or Result<T>. " +
            $"Handler for {typeof(TRequest).Name} returns {typeof(TResponse).Name}.");
    }
}
