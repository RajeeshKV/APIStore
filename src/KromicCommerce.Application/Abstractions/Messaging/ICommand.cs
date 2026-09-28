namespace KromicCommerce.Application.Abstractions.Messaging;

/// <summary>
/// Common marker for all commands (mutations).
/// Used by pipeline behaviors to distinguish commands from queries without
/// needing to inspect generic type arguments.
/// </summary>
public interface IBaseCommand { }

/// <summary>Marker interface for a command that returns a Result (no value).</summary>
public interface ICommand : IRequest<Result>, IBaseCommand
{
}

/// <summary>Marker interface for a command that returns a Result&lt;TResponse&gt;.</summary>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>, IBaseCommand
{
}

/// <summary>
/// Opt-in marker for commands where it is semantically safe to automatically
/// retry on <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>.
///
/// Only apply this to operations where re-running the handler against fresh
/// database state always produces the correct outcome — e.g. cart mutations
/// where the intent is "add quantity" regardless of concurrent state.
///
/// Do NOT apply to administrative writes (Product, Category, Brand, Settings)
/// where a genuine stale-write conflict must be surfaced to the caller so
/// they can reconcile.
/// </summary>
public interface IRetryableConcurrencyCommand : IBaseCommand { }
