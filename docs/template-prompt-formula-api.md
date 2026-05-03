# Template Prompt Formula API

## Scope

This document describes the v1 template prompt formula API implemented in the AI microservice. It standardizes prompt-template based text generation and keeps the existing AI response envelope patterns.

v1 scope only supports text outputs. It does not implement workflow orchestration, loops, conditional blocks, or a custom template DSL.

## Template Format

Supported placeholder syntax:

```text
{{variable_name}}
```

Rules:
- Placeholder matching is case-insensitive.
- Variable names support letters, numbers, and underscores.
- Variables must resolve to scalar values only:
  - string
  - number
  - boolean
  - null/undefined becomes empty string
- Arrays and objects are rejected.

Example template:

```text
Write a {{output_style}} caption about {{product_name}} for {{audience}}.
```

When language or instruction is provided, the rendered prompt is prefixed with normalized metadata lines before the template body.

## Resolution Precedence

Template source precedence is:

1. `formulaId`
2. `formulaKey`
3. inline `template`

Behavior:
- If `formulaId` is present and valid, the catalog template is used and `formulaKey` / `template` are ignored.
- If `formulaId` is absent and `formulaKey` is present, the catalog template is used and inline `template` is ignored.
- If neither catalog selector is present, inline `template` is required.
- Catalog templates must be active.

## Public Generate API

Endpoint:

```http
POST /api/Ai/formulas/generate
```

Auth:
- Requires authenticated user.

Request body:

```json
{
  "formulaId": "uuid-or-null",
  "formulaKey": "launch-caption",
  "template": null,
  "variables": {
    "product_name": "MeAI",
    "audience": "creator"
  },
  "outputType": "caption",
  "language": "vi",
  "instruction": "ngắn, chắc, có CTA",
  "variantCount": 3,
  "workspaceId": "uuid-or-null"
}
```

Response body:

```json
{
  "isSuccess": true,
  "value": {
    "formulaId": "uuid-or-null",
    "formulaKey": "launch-caption",
    "outputType": "caption",
    "renderedPrompt": "...",
    "model": "gpt-5-4",
    "outputs": [
      "variant 1",
      "variant 2",
      "variant 3"
    ],
    "usageReferenceId": "uuid"
  }
}
```

Notes:
- Response uses the existing `Result<T>` success envelope.
- Validation and business failures continue to return `ProblemDetails` through the existing failure handling path.
- Unauthorized responses remain in the existing JSON message shape.

## Admin Formula APIs

Endpoints:

```http
GET    /api/Ai/admin/formulas
POST   /api/Ai/admin/formulas
PUT    /api/Ai/admin/formulas/{formulaId}
DELETE /api/Ai/admin/formulas/{formulaId}
```

Behavior:
- `GET` returns the formula catalog.
- `POST` creates a new prompt formula template.
- `PUT` updates an existing prompt formula template.
- `DELETE` is a soft-disable behavior for v1 by marking the template inactive.

## Validation Rules

Generate request validation:
- At least one of `formulaId`, `formulaKey`, or `template` is required.
- `outputType` is required.
- Allowed `outputType` values:
  - `caption`
  - `hook`
  - `cta`
  - `outline`
  - `custom`
- `variantCount` defaults to `1`.
- `variantCount` must be between `1` and `5`.
- If a catalog template is selected, request `outputType` must match the template `OutputType`.
- Variables must be scalar values only.

Renderer validation:
- Every `{{variable}}` placeholder must be bound.
- Missing placeholders return `Formula.MissingVariable`.
- The error payload includes the missing variable name.

Example missing variable failure semantics:
- `type`: `Formula.MissingVariable`
- `detail`: `Missing variable: audience.`
- `errors.missingVariable`: `audience`
- `errors.missingVariables`: array of unresolved names

## Billing Behavior

Billing action type:
- `formula_generation`

Flow:
1. Resolve and render the template.
2. Quote coin cost using the default model `gpt-5-4`.
3. Debit coins before generation.
4. Call the text generation service.
5. If generation fails or produces no usable output, refund exactly once.
6. Persist AI spend record status updates.

The billing reason/reference strings follow the implemented catalog constants for formula generation debit/refund and usage tracking.

## Audit Log Behavior

Successful generations create a `FormulaGenerationLog` record containing:
- user id
- workspace id
- formula template id when catalog-based
- formula key snapshot
- rendered prompt
- normalized variables JSON
- output type
- generation model
- created time

Important behavior:
- Audit log is written only for successful generation.
- The stored `renderedPrompt` reflects the fully bound template that was sent into generation.
- Inline-template usage is also logged, with nullable formula template id and formula key.

## V1 Boundaries Compared With Workflow Engine

This feature is intentionally limited.

Included in v1:
- single prompt template resolution
- variable substitution
- text generation variants
- billing integration
- audit logging
- admin catalog management

Not included in v1:
- multi-step workflows
- branching or conditions
- loops
- nested template execution
- tool invocation pipelines
- end-user model override
- advanced DSL parsing

## Implementation References

Key implementation points:
- public/admin controller surface: `Backend/Microservices/Ai.Microservice/src/WebApi/Controllers/PromptFormulasController.cs`
- template renderer: `Backend/Microservices/Ai.Microservice/src/Infrastructure/Logic/Formulas/FormulaTemplateRenderer.cs`
- generation service: `Backend/Microservices/Ai.Microservice/src/Infrastructure/Logic/Formulas/FormulaGenerationService.cs`
- billing constants: `Backend/Microservices/Ai.Microservice/src/Application/Billing/CoinCostCatalog.cs`
