# Kiro Context Strategy

This project intentionally separates context into four layers.

## Steering

Always/conditionally loaded rules:
- `steering/project.md`
- `steering/backend.md`
- `steering/configuration.md`
- `steering/testing.md`

Keep these files small.

## Skills

Reusable workflows loaded only when relevant:
- integration-development
- api-development
- database-development
- authentication
- payment-integration
- notification-system
- shipping-tracking
- testing

Skills should explain HOW to work. They should not duplicate the full contents of `/docs`.

## Docs

`/docs` contains detailed requirements and provider contracts. Read only the documents relevant to the current task.

## Specs

Use Kiro Specs for substantial implementation phases. Keep the active spec focused on one feature/phase.

## Token-efficiency rule

Do NOT instruct Kiro to read every file under `/docs` for every request.

Prefer:
1. Always-on steering
2. Relevant skill
3. Relevant docs
4. Relevant source code
5. Current spec/task

This keeps context focused and reduces unnecessary token usage.
