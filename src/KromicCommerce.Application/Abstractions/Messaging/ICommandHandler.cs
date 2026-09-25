namespace KromicCommerce.Application.Abstractions.Messaging;

/// <summary>Handler for a command returning Result (no value).</summary>
public interface ICommandHandler<TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand
{
}

/// <summary>Handler for a command returning Result&lt;TResponse&gt;.</summary>
public interface ICommandHandler<TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>
{
}
