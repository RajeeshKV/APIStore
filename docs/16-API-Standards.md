# API Standards

## Versioning
Use `/api/v1/...`.

## Naming
Use resource-oriented routes:
- GET /api/v1/products
- GET /api/v1/products/{id}
- POST /api/v1/admin/products
- POST /api/v1/orders

## Response model
Use consistent success/error structures.

Example error:
```json
{
  "success": false,
  "error": {
    "code": "PRODUCT_OUT_OF_STOCK",
    "message": "The requested quantity is unavailable."
  },
  "traceId": "..."
}
```

## Pagination
Prefer:
- page/pageSize for straightforward admin grids
- cursor pagination if scale later requires it

Always enforce server-side maximum page size.

## Filtering/sorting
Whitelist supported fields. Never concatenate arbitrary SQL/order expressions.

## DTOs
Never expose EF entities.

## Validation
Validate at the application boundary using validators.

## HTTP semantics
Use correct status codes:
- 200/201/204 success
- 400 validation
- 401 authentication
- 403 authorization
- 404 not found
- 409 conflict
- 422 business validation where appropriate
- 429 rate limit
- 500 unexpected server error

## Idempotency
Support idempotency keys for operations where duplicate requests can create financial/business side effects.
