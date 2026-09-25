namespace KromicCommerce.Application.Features.Store.SetStoreOpen;

internal sealed class SetStoreOpenValidator : AbstractValidator<SetStoreOpenCommand>
{
    public SetStoreOpenValidator()
    {
        // Closure message is only meaningful when closing; max 500 chars
        RuleFor(x => x.ClosureMessage)
            .MaximumLength(500)
            .When(x => !x.IsOpen && x.ClosureMessage is not null);

        // Warn when closing without a message — we don't enforce it but it's a best practice
        // (validation is purely structural here; business policy is in the domain)
    }
}
