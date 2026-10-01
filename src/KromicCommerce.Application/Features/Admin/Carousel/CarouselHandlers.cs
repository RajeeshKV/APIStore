using FluentValidation;
using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace KromicCommerce.Application.Features.Admin.Carousel;

// -------------------------------------------------------------------------
// Commands and queries
// -------------------------------------------------------------------------

public sealed record GetAdminCarouselSlidesQuery(bool ActiveOnly = false)
    : IQuery<IReadOnlyList<CarouselSlideResponse>>;

public sealed record GetAdminCarouselSlideQuery(Guid Id) : IQuery<CarouselSlideResponse>;

public sealed record CreateCarouselSlideCommand(
    string Title,
    string? Subtitle,
    string? CtaText,
    int SortOrder,
    bool IsActive) : ICommand<CarouselSlideResponse>;

public sealed record UpdateCarouselSlideCommand(
    Guid Id,
    string Title,
    string? Subtitle,
    string? CtaText,
    int SortOrder,
    bool IsActive) : ICommand<CarouselSlideResponse>;

public sealed record DeleteCarouselSlideCommand(Guid Id) : ICommand;

public sealed record UploadCarouselSlideImageCommand(
    Guid Id, string PublicId, string Url) : ICommand<CarouselSlideResponse>;

public sealed record DeleteCarouselSlideImageCommand(Guid Id) : ICommand<CarouselSlideResponse>;

/// <summary>
/// Public storefront carousel. Returns only active slides that have an image, in display order.
/// </summary>
public sealed record GetStorefrontCarouselQuery : IQuery<IReadOnlyList<StorefrontCarouselSlideResponse>>;

// -------------------------------------------------------------------------
// Validators
// -------------------------------------------------------------------------

internal sealed class CreateCarouselSlideValidator : AbstractValidator<CreateCarouselSlideCommand>
{
    public CreateCarouselSlideValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Subtitle).MaximumLength(500);
        RuleFor(x => x.CtaText).MaximumLength(50);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0)
            .WithMessage("Sort order must be zero or greater.");
    }
}

internal sealed class UpdateCarouselSlideValidator : AbstractValidator<UpdateCarouselSlideCommand>
{
    public UpdateCarouselSlideValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Subtitle).MaximumLength(500);
        RuleFor(x => x.CtaText).MaximumLength(50);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0)
            .WithMessage("Sort order must be zero or greater.");
    }
}

internal sealed class GetAdminCarouselSlidesValidator
    : AbstractValidator<GetAdminCarouselSlidesQuery>
{
    public GetAdminCarouselSlidesValidator()
    {
        // No rules. Carousel is a small admin-managed collection like store policies, which are
        // returned unpaged, so there is no page size to clamp.
    }
}

// -------------------------------------------------------------------------
// Mapping
// -------------------------------------------------------------------------

internal static class CarouselMapper
{
    internal static CarouselSlideResponse Map(CarouselSlide s) =>
        new(s.Id, s.Title, s.Subtitle, s.ImagePublicId, s.ImageUrl,
            s.CtaText, s.SortOrder, s.IsActive, s.CreatedAtUtc, s.UpdatedAtUtc);

    /// <summary>
    /// The storefront projection. Deliberately drops the Cloudinary public id, audit fields and the
    /// active flag — none of which a storefront has any use for.
    /// </summary>
    internal static StorefrontCarouselSlideResponse MapForStorefront(CarouselSlide s) =>
        new(s.Id, s.Title, s.Subtitle, s.ImageUrl!, s.CtaText, s.SortOrder,
            StorefrontCarouselSlideResponse.FixedCtaTarget);
}

// -------------------------------------------------------------------------
// Handlers — admin
// -------------------------------------------------------------------------

internal sealed class GetAdminCarouselSlidesHandler(
    IApplicationDbContext db)
    : IQueryHandler<GetAdminCarouselSlidesQuery, IReadOnlyList<CarouselSlideResponse>>
{
    public async Task<Result<IReadOnlyList<CarouselSlideResponse>>> Handle(
        GetAdminCarouselSlidesQuery query, CancellationToken ct)
    {
        var q = db.CarouselSlides.AsNoTracking().AsQueryable();
        if (query.ActiveOnly)
            q = q.Where(s => s.IsActive);

        // Same deterministic ordering as the storefront, so an admin reordering the list sees
        // exactly the sequence customers will see.
        var slides = await q
            .OrderBy(s => s.SortOrder).ThenBy(s => s.CreatedAtUtc).ThenBy(s => s.Id)
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<CarouselSlideResponse>>(
            slides.Select(CarouselMapper.Map).ToList());
    }
}

internal sealed class GetAdminCarouselSlideHandler(
    IApplicationDbContext db)
    : IQueryHandler<GetAdminCarouselSlideQuery, CarouselSlideResponse>
{
    public async Task<Result<CarouselSlideResponse>> Handle(
        GetAdminCarouselSlideQuery query, CancellationToken ct)
    {
        var slide = await db.CarouselSlides
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == query.Id, ct);

        return slide is null
            ? Result.Failure<CarouselSlideResponse>(
                Error.NotFound("CAROUSEL_SLIDE_NOT_FOUND", "Carousel slide not found."))
            : Result.Success(CarouselMapper.Map(slide));
    }
}

internal sealed class CreateCarouselSlideHandler(
    IApplicationDbContext db,
    ICatalogCacheService cache,
    ILogger<CreateCarouselSlideHandler> logger)
    : ICommandHandler<CreateCarouselSlideCommand, CarouselSlideResponse>
{
    public async Task<Result<CarouselSlideResponse>> Handle(
        CreateCarouselSlideCommand cmd, CancellationToken ct)
    {
        var slide = CarouselSlide.Create(
            cmd.Title, cmd.Subtitle, cmd.CtaText, cmd.SortOrder, cmd.IsActive);

        db.CarouselSlides.Add(slide);
        await db.SaveChangesAsync(ct);

        cache.InvalidateCarousel();
        logger.LogInformation("Carousel slide created: {Id}", slide.Id);

        return Result.Success(CarouselMapper.Map(slide));
    }
}

internal sealed class UpdateCarouselSlideHandler(
    IApplicationDbContext db,
    ICatalogCacheService cache,
    ILogger<UpdateCarouselSlideHandler> logger)
    : ICommandHandler<UpdateCarouselSlideCommand, CarouselSlideResponse>
{
    public async Task<Result<CarouselSlideResponse>> Handle(
        UpdateCarouselSlideCommand cmd, CancellationToken ct)
    {
        var slide = await db.CarouselSlides.FirstOrDefaultAsync(s => s.Id == cmd.Id, ct);
        if (slide is null)
            return Result.Failure<CarouselSlideResponse>(
                Error.NotFound("CAROUSEL_SLIDE_NOT_FOUND", "Carousel slide not found."));

        slide.Update(cmd.Title, cmd.Subtitle, cmd.CtaText, cmd.SortOrder, cmd.IsActive);

        await db.SaveChangesAsync(ct);
        cache.InvalidateCarousel();
        logger.LogInformation("Carousel slide updated: {Id}", slide.Id);

        return Result.Success(CarouselMapper.Map(slide));
    }
}

internal sealed class DeleteCarouselSlideHandler(
    IApplicationDbContext db,
    ICatalogCacheService cache,
    ILogger<DeleteCarouselSlideHandler> logger)
    : ICommandHandler<DeleteCarouselSlideCommand>
{
    public async Task<Result> Handle(DeleteCarouselSlideCommand cmd, CancellationToken ct)
    {
        var slide = await db.CarouselSlides.FirstOrDefaultAsync(s => s.Id == cmd.Id, ct);
        if (slide is null)
            return Result.Failure(
                Error.NotFound("CAROUSEL_SLIDE_NOT_FOUND", "Carousel slide not found."));

        // Hard delete, matching categories and brands. The Cloudinary asset is deliberately left
        // in place: deleting it here would make an admin's "delete slide" destructive to an asset
        // that may still be referenced, and Cloudinary cleans up orphaned uploads on its own
        // lifecycle policy.
        db.CarouselSlides.Remove(slide);
        await db.SaveChangesAsync(ct);
        cache.InvalidateCarousel();
        logger.LogInformation("Carousel slide deleted: {Id}", cmd.Id);

        return Result.Success();
    }
}

internal sealed class UploadCarouselSlideImageHandler(
    IApplicationDbContext db,
    ICatalogCacheService cache)
    : ICommandHandler<UploadCarouselSlideImageCommand, CarouselSlideResponse>
{
    public async Task<Result<CarouselSlideResponse>> Handle(
        UploadCarouselSlideImageCommand cmd, CancellationToken ct)
    {
        var slide = await db.CarouselSlides.FirstOrDefaultAsync(s => s.Id == cmd.Id, ct);
        if (slide is null)
            return Result.Failure<CarouselSlideResponse>(
                Error.NotFound("CAROUSEL_SLIDE_NOT_FOUND", "Carousel slide not found."));

        slide.SetImage(cmd.PublicId, cmd.Url);

        await db.SaveChangesAsync(ct);
        cache.InvalidateCarousel();

        return Result.Success(CarouselMapper.Map(slide));
    }
}

internal sealed class DeleteCarouselSlideImageHandler(
    IApplicationDbContext db,
    ICatalogCacheService cache)
    : ICommandHandler<DeleteCarouselSlideImageCommand, CarouselSlideResponse>
{
    public async Task<Result<CarouselSlideResponse>> Handle(
        DeleteCarouselSlideImageCommand cmd, CancellationToken ct)
    {
        var slide = await db.CarouselSlides.FirstOrDefaultAsync(s => s.Id == cmd.Id, ct);
        if (slide is null)
            return Result.Failure<CarouselSlideResponse>(
                Error.NotFound("CAROUSEL_SLIDE_NOT_FOUND", "Carousel slide not found."));

        slide.ClearImage();

        await db.SaveChangesAsync(ct);
        cache.InvalidateCarousel();

        return Result.Success(CarouselMapper.Map(slide));
    }
}

// -------------------------------------------------------------------------
// Handler — public storefront
// -------------------------------------------------------------------------

internal sealed class GetStorefrontCarouselHandler(
    IApplicationDbContext db,
    IMemoryCache cache,
    IOptions<CatalogCacheOptions> cacheOpts)
    : IQueryHandler<GetStorefrontCarouselQuery, IReadOnlyList<StorefrontCarouselSlideResponse>>
{
    private const string CacheKey = CatalogCacheKeys.StorefrontCarousel;

    public async Task<Result<IReadOnlyList<StorefrontCarouselSlideResponse>>> Handle(
        GetStorefrontCarouselQuery query, CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyList<StorefrontCarouselSlideResponse>? cached)
            && cached is not null)
            return Result.Success(cached);

        var slides = await db.CarouselSlides
            .AsNoTracking()
            // Active only: the storefront must never see an unpublished slide.
            .Where(s => s.IsActive)
            // An active slide with no image is a draft whose upload never happened. Skipping it
            // here means the storefront can never render a hero with a missing image.
            .Where(s => s.ImageUrl != null)
            // Total ordering. SortOrder alone is not enough because it is admin-supplied and two
            // slides can share a value; without a tiebreak PostgreSQL may return them in any
            // order, which would make the carousel reshuffle between requests.
            .OrderBy(s => s.SortOrder).ThenBy(s => s.CreatedAtUtc).ThenBy(s => s.Id)
            .Select(s => new StorefrontCarouselSlideResponse(
                s.Id, s.Title, s.Subtitle, s.ImageUrl!, s.CtaText, s.SortOrder,
                StorefrontCarouselSlideResponse.FixedCtaTarget))
            .ToListAsync(ct);

        IReadOnlyList<StorefrontCarouselSlideResponse> result = slides;

        cache.Set(CacheKey, result, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(cacheOpts.Value.DefaultExpiryMinutes),
            Size = 1
        });

        return Result.Success(result);
    }
}