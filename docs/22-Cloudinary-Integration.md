# Cloudinary Integration

## Purpose

Cloudinary is the standard media storage and transformation provider.

## Responsibilities

Cloudinary handles:
- Product images
- Product variant images
- Homepage banners
- Business logo
- Favicon/assets where appropriate
- Other admin-managed media

PostgreSQL stores metadata and relationships, not binary image data.

## Architecture

```text
Admin
  -> Media service
  -> Cloudinary
  -> Store public ID + URL/metadata in PostgreSQL
```

## Configuration

Required:
- Cloud name
- API key
- API secret

Credentials are deployment secrets.

## Upload

Validate before upload:
- Allowed MIME types
- File extension
- File size
- Image dimensions where appropriate

Do not accept arbitrary executable files.

## Folder strategy

Use a predictable customer/deployment prefix:
```text
<deployment>/products/
<deployment>/categories/
<deployment>/branding/
<deployment>/homepage/
```

## Database metadata

Store:
- Provider
- PublicId
- SecureUrl
- ResourceType
- Format
- Width
- Height
- AltText
- SortOrder
- Entity relationship
- CreatedAt

## Deletion

When an asset is deleted from Admin:
1. Delete/replace the database relationship safely.
2. Remove the Cloudinary asset where appropriate.
3. Avoid deleting a shared asset still referenced elsewhere.

## Transformations

Use Cloudinary transformations for:
- Thumbnail
- Card image
- Product detail image
- Responsive image sizes
- WebP/AVIF where supported

## Direct upload

Future optimization may use signed/direct browser uploads. Backend remains responsible for authorization and metadata persistence.

## Failure handling

If Cloudinary fails:
- Do not persist a successful media record
- Return a provider error
- Log provider operation and trace ID

## Security

Never expose API secret to the frontend.
