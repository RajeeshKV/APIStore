namespace KromicCommerce.Application.Abstractions.Messaging;

/// <summary>Marker interface for a query returning Result&lt;TResponse&gt;.</summary>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>
{
}
