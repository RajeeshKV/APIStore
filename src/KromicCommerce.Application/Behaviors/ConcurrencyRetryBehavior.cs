using KromicCommerce.Application.Abstractions.Data;
using Microsoft.EntityFrameworkCore;

namespace KromicCommerce.Application.Behaviors;

/// <summary>
/// MediatR pipeline behavior that automatically retries command handlers on
/// <see cref="DbUpdateConcurrencyException"/>.
///
/// Why this exists:
///   EF Core optimistic concurrency throws when a tracked row was modified
///   or deleted between the time it was loaded and the time SaveChanges runs.
///   This happens in two scenarios in this codebase:
///
///   1. InventoryItem xmin token — expected, prevents overselling.
///      The checkout handler has its own dedicated retry loop for this.
///      This behavior acts as a safety net for any handler that doesn't.
///
///   2. Stale change-tracker state — a Cart, CartItem, or Order row is
///      loaded, then deleted or updated concurrently (e.g. duplicate request,
///      expired-cart cleanup, or a race between two requests).
///
/// Strategy:
///   On <see cref="DbUpdateConcurrencyException"/> the change tracker is
///   cleared so EF forgets all stale snapshots, then the handler is invoked
///   again with the original request. The handler re-queries fresh data from
///   the database. This is safe because:
///     - All handlers are idempotent with respect to business rules.
///     - The domain enforces invariants, so a retry with fresh state produces
///       the correct outcome rather than a corrupt one.
///
///   After <see cref="MaxRetries"/> failed attempts the exception is converted
///   to a typed Result.Failure (409 Conflict) so the FE receives a structured
///   error instead of an unhandled 500.
///
/// Scope:
///   Only applies to commands (mutations). Queries never write so they cannot
///   produce concurrency conflicts; they are passed through unchanged.
/// </summary>
internal sealed class ConcurrencyRetryBehavior<TRequest, TResponse>(
    IApplicationDbContext db,
    ILogger<ConcurrencyRetryBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const int MaxRetries = 3;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Only intercept mutations — skip queries entirely.
        if (request is not IBaseCommand)
            return await next();

        var requestName = typeof(TRequest).Name;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                // Clear all stale EF tracking state before re-running the handler.
                // This forces the handler to re-query fresh data from the database.
                db.ChangeTracker.Clear();

                logger.LogWarning(
                    "Concurrency conflict on {RequestName}. Clearing change tracker and retrying " +
                    "(attempt {Attempt}/{Max}).",
                    requestName, attempt, MaxRetries);

                // Brief back-off to reduce the chance of racing again immediately
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
            }

            try
            {
                return await next();
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < MaxRetries)
            {
                // Log and loop — the stale entries will be cleared at the top of the next iteration
                logger.LogWarning(ex,
                    "DbUpdateConcurrencyException on {RequestName} (attempt {Attempt}/{Max}). Will retry.",
                    requestName, attempt + 1, MaxRetries);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // All retries exhausted — convert to a typed Result failure
                logger.LogError(ex,
                    "DbUpdateConcurrencyException on {RequestName} after {Max} retries. " +
                    "Returning conflict error.",
                    requestName, MaxRetries);

                return BuildConflictResult(
                    "CONCURRENCY_CONFLICT",
                    "The resource was modified by another request. Please refresh and try again.");
            }
        }

        // Unreachable — the loop always returns or throws above.
        // Included to satisfy the compiler.
        throw new InvalidOperationException("ConcurrencyRetryBehavior exited retry loop unexpectedly.");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a typed <see cref="Result"/> or <see cref="Result{T}"/> conflict failure
    /// without knowing the concrete TResponse at compile time.
    /// Uses the same reflection pattern as <see cref="ValidationBehavior{TRequest,TResponse}"/>.
    /// </summary>
    private static TResponse BuildConflictResult(string code, string description)
    {
        var error = Error.Conflict(code, description);

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

        // Handler doesn't return Result — let the exception propagate as before
        throw new DbUpdateConcurrencyException(
            $"Concurrency conflict on {typeof(TRequest).Name} after {MaxRetries} retries " +
            $"and TResponse {typeof(TResponse).Name} is not a Result type.");
    }
}
