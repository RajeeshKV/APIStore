namespace KromicCommerce.Application.Abstractions.Messaging;

/// <summary>Marker interface for a command that returns a Result (no value).</summary>
public interface ICommand : IRequest<Result>
{
}

/// <summary>Marker interface for a command that returns a Result&lt;TResponse&gt;.</summary>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>
{
}
