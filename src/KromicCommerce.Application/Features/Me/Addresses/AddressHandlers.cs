namespace KromicCommerce.Application.Features.Me.Addresses;

internal static class AddressMapper
{
    internal static CustomerAddressResponse Map(CustomerAddress a) =>
        new(a.Id, a.Label, a.FirstName, a.LastName, a.Company,
            a.AddressLine1, a.AddressLine2, a.City, a.State, a.PostalCode,
            a.CountryCode, a.Phone, a.IsDefault, a.CreatedAtUtc, a.UpdatedAtUtc);
}

internal sealed class GetCustomerAddressesHandler(IApplicationDbContext db)
    : IQueryHandler<GetCustomerAddressesQuery, IReadOnlyList<CustomerAddressResponse>>
{
    public async Task<Result<IReadOnlyList<CustomerAddressResponse>>> Handle(
        GetCustomerAddressesQuery query, CancellationToken ct)
    {
        var addresses = await db.CustomerAddresses.AsNoTracking()
            .Where(a => a.CustomerId == query.CustomerId)
            .OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.CreatedAtUtc)
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<CustomerAddressResponse>>(
            addresses.Select(AddressMapper.Map).ToList());
    }
}

internal sealed class GetCustomerAddressByIdHandler(IApplicationDbContext db)
    : IQueryHandler<GetCustomerAddressByIdQuery, CustomerAddressResponse>
{
    public async Task<Result<CustomerAddressResponse>> Handle(
        GetCustomerAddressByIdQuery query, CancellationToken ct)
    {
        var a = await db.CustomerAddresses.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == query.AddressId && x.CustomerId == query.CustomerId, ct);

        return a is null
            ? Result.Failure<CustomerAddressResponse>(Error.NotFound("ADDRESS_NOT_FOUND", "Address not found."))
            : Result.Success(AddressMapper.Map(a));
    }
}

internal sealed class CreateCustomerAddressHandler(IApplicationDbContext db)
    : ICommandHandler<CreateCustomerAddressCommand, CustomerAddressResponse>
{
    public async Task<Result<CustomerAddressResponse>> Handle(
        CreateCustomerAddressCommand cmd, CancellationToken ct)
    {
        // If this address should be the default, clear existing default first
        if (cmd.IsDefault)
        {
            var existing = await db.CustomerAddresses
                .Where(a => a.CustomerId == cmd.CustomerId && a.IsDefault)
                .ToListAsync(ct);
            foreach (var e in existing) e.ClearDefault();
        }

        var address = CustomerAddress.Create(
            cmd.CustomerId, cmd.Label, cmd.FirstName, cmd.LastName,
            cmd.Company, cmd.AddressLine1, cmd.AddressLine2,
            cmd.City, cmd.State, cmd.PostalCode, cmd.CountryCode, cmd.Phone);

        if (cmd.IsDefault) address.SetAsDefault();

        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync(ct);
        return Result.Success(AddressMapper.Map(address));
    }
}

internal sealed class UpdateCustomerAddressHandler(IApplicationDbContext db)
    : ICommandHandler<UpdateCustomerAddressCommand, CustomerAddressResponse>
{
    public async Task<Result<CustomerAddressResponse>> Handle(
        UpdateCustomerAddressCommand cmd, CancellationToken ct)
    {
        var address = await db.CustomerAddresses
            .FirstOrDefaultAsync(a => a.Id == cmd.AddressId && a.CustomerId == cmd.CustomerId, ct);

        if (address is null)
            return Result.Failure<CustomerAddressResponse>(
                Error.NotFound("ADDRESS_NOT_FOUND", "Address not found."));

        address.Update(cmd.Label, cmd.FirstName, cmd.LastName, cmd.Company,
            cmd.AddressLine1, cmd.AddressLine2, cmd.City, cmd.State,
            cmd.PostalCode, cmd.CountryCode, cmd.Phone);

        await db.SaveChangesAsync(ct);
        return Result.Success(AddressMapper.Map(address));
    }
}

internal sealed class DeleteCustomerAddressHandler(IApplicationDbContext db)
    : ICommandHandler<DeleteCustomerAddressCommand>
{
    public async Task<Result> Handle(DeleteCustomerAddressCommand cmd, CancellationToken ct)
    {
        var address = await db.CustomerAddresses
            .FirstOrDefaultAsync(a => a.Id == cmd.AddressId && a.CustomerId == cmd.CustomerId, ct);

        if (address is null)
            return Result.Failure(Error.NotFound("ADDRESS_NOT_FOUND", "Address not found."));

        var wasDefault = address.IsDefault;
        db.CustomerAddresses.Remove(address);
        await db.SaveChangesAsync(ct);

        // Promote the most recently created remaining address to default
        if (wasDefault)
        {
            var next = await db.CustomerAddresses
                .Where(a => a.CustomerId == cmd.CustomerId)
                .OrderByDescending(a => a.CreatedAtUtc)
                .FirstOrDefaultAsync(ct);
            if (next is not null)
            {
                next.SetAsDefault();
                await db.SaveChangesAsync(ct);
            }
        }

        return Result.Success();
    }
}

internal sealed class SetDefaultCustomerAddressHandler(IApplicationDbContext db)
    : ICommandHandler<SetDefaultCustomerAddressCommand>
{
    public async Task<Result> Handle(SetDefaultCustomerAddressCommand cmd, CancellationToken ct)
    {
        var target = await db.CustomerAddresses
            .FirstOrDefaultAsync(a => a.Id == cmd.AddressId && a.CustomerId == cmd.CustomerId, ct);

        if (target is null)
            return Result.Failure(Error.NotFound("ADDRESS_NOT_FOUND", "Address not found."));

        // Transactionally clear old default and set new one
        var oldDefault = await db.CustomerAddresses
            .Where(a => a.CustomerId == cmd.CustomerId && a.IsDefault && a.Id != cmd.AddressId)
            .FirstOrDefaultAsync(ct);

        oldDefault?.ClearDefault();
        target.SetAsDefault();
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
