using KromicCommerce.Application.Abstractions.Data;
using Microsoft.EntityFrameworkCore;

namespace KromicCommerce.Application.Behaviors;

/// <summary>
/// MediatR pipeline behavior that handles <see cref="DbUpdateConcurrencyException"/>
/// in an operation-aware way.
///
/// # Two categories of concurrency conflict
///
/// ## 1. Retryable (opt-in via <see cref="IRetryableConcurrencyCommand"/>)
///
///   Operations where re-running the handler against fresh database state is
///   semantically correct. Currently: cart mutations.
///
///   On conflict: clear the change tracker, wait briefly, re-invoke the
///   handler up to <see cref="MaxRetries"/> times.
///
///   If all retries are exhausted the exception propagates to the global
///   exception handler which converts it to a 409 CONCURRENCY_CONFLICT.
///
/// ## 2. Non-retryable (all other IBaseCommand implementations)
///
///   Administrative writes where a genuine stale-write conflict must be
///   surfaced: Product, Category, Brand, Settings, Orders, etc.
///
///   On conflict: let the exception propagate immediately. The global
///   exception handler converts it to a typed 409 CONCURRENCY_CONFLICT
///   response. The caller must reload and reconcile.
///
///   This preserves optimistic concurrency semantics — we never silently
///   overwrite a newer version written by another user/request.
///
/// # What this does NOT do
///   - Does NOT blindly retry every command.
///   - Does NOT retry business validation failures.
///   - Does NOT retry authorization failures.
///   - Does NOT remove xmin or RowVersion tokens.
///   - Does NOT wrap the entire HTTP request.
/// </summary>
internal sealed class ConcurrencyRetryBehavior<TRequest, TResponse>(
    IApplicationDbContext db,
    ILogger<ConcurrencyRetryBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>
    /// Maximum number of retry attempts for retryable commands.
    /// Kept intentionally small — if this is exceeded it indicates a
    /// sustained hotspot that needs architectural attention, not more retries.
    /// </summary>
    private const int MaxRetries = 2;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Only intercept commands (mutations). Queries pass straight through.
        if (request is not IBaseCommand)
            return await next();

        var isRetryable = request is IRetryableConcurrencyCommand;
        var requestName  = typeof(TRequest).Name;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                // Clear ALL stale EF tracking state so the handler re-queries
                // fresh rows from the database on the next iteration.
                db.ChangeTracker.Clear();

                // Linear back-off: 50 ms, 100 ms — keeps pressure off the DB
                var delay = TimeSpan.FromMilliseconds(50 * attempt);
                logger.LogWarning(
                    "Concurrency conflict on {RequestName} (attempt {Attempt}/{Max}). " +
                    "Clearing change tracker, waiting {DelayMs} ms, then retrying.",
                    requestName, attempt, MaxRetries, delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
            }

            try
            {
                return await next();
            }
            catch (DbUpdateConcurrencyException ex) when (isRetryable && attempt < MaxRetries)
            {
                // Retryable operation — log and loop; tracker cleared at top of next iteration
                logger.LogWarning(ex,
                    "Retryable concurrency conflict on {RequestName} (attempt {Attempt}/{Max}).",
                    requestName, attempt + 1, MaxRetries);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Either:
                //   a) Non-retryable command — propagate immediately so the caller is informed.
                //   b) Retryable command — all retries exhausted.
                // In both cases let the exception bubble to GlobalExceptionMiddleware,
                // which converts it to a standardized 409 CONCURRENCY_CONFLICT response.
                logger.LogError(
                    "Unresolved concurrency conflict on {RequestName} " +
                    "(retryable={IsRetryable}, attempt={Attempt}/{Max}). Propagating.",
                    requestName, isRetryable, attempt + 1, MaxRetries);
                throw;
            }
        }

        // Unreachable — loop always returns or throws. Satisfies compiler.
        throw new InvalidOperationException(
            $"{nameof(ConcurrencyRetryBehavior<TRequest, TResponse>)} exited retry loop unexpectedly.");
    }
}
