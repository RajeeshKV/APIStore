namespace KromicCommerce.Application.Abstractions.Messaging;

/// <summary>Handler for a query returning Result&lt;TResponse&gt;.</summary>
public interface IQueryHandler<TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>
{
}
