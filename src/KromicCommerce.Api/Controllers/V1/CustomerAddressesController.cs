using Asp.Versioning;
using KromicCommerce.Application.Abstractions.Auth;
using KromicCommerce.Application.Features.Me.Addresses;
using KromicCommerce.Contracts.Me;
using Microsoft.AspNetCore.Authorization;

namespace KromicCommerce.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/customer/addresses")]
[Authorize(Policy = "CustomerOrAdmin")]
public sealed class CustomerAddressesController(
    IMediator mediator, ICurrentUserService currentUser) : ControllerBase
{
    private Guid? CustomerId => currentUser.UserId;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerAddressResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        if (CustomerId is null) return Unauthorized();
        var result = await mediator.Send(new GetCustomerAddressesQuery(CustomerId.Value), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CustomerAddressResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (CustomerId is null) return Unauthorized();
        var result = await mediator.Send(new GetCustomerAddressByIdQuery(CustomerId.Value, id), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpPost]
    [ProducesResponseType(typeof(CustomerAddressResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateAddressRequest request, CancellationToken ct)
    {
        if (CustomerId is null) return Unauthorized();
        var result = await mediator.Send(new CreateCustomerAddressCommand(
            CustomerId.Value, request.Label, request.FirstName, request.LastName,
            request.Company, request.AddressLine1, request.AddressLine2,
            request.City, request.State, request.PostalCode, request.CountryCode,
            request.Phone, request.IsDefault), ct);

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id, version = "1" }, result.Value)
            : result.Error.ToActionResult();
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CustomerAddressResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateAddressRequest request, CancellationToken ct)
    {
        if (CustomerId is null) return Unauthorized();
        var result = await mediator.Send(new UpdateCustomerAddressCommand(
            CustomerId.Value, id, request.Label, request.FirstName, request.LastName,
            request.Company, request.AddressLine1, request.AddressLine2,
            request.City, request.State, request.PostalCode, request.CountryCode,
            request.Phone), ct);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (CustomerId is null) return Unauthorized();
        var result = await mediator.Send(new DeleteCustomerAddressCommand(CustomerId.Value, id), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }

    [HttpPut("{id:guid}/default")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetDefault(Guid id, CancellationToken ct)
    {
        if (CustomerId is null) return Unauthorized();
        var result = await mediator.Send(
            new SetDefaultCustomerAddressCommand(CustomerId.Value, id), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }
}
