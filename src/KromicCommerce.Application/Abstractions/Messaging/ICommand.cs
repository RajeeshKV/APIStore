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
