# Template Prompt Formula API

## Summary

Feature này triển khai API generate nội dung AI theo công thức/template prompt chuẩn hóa.

Quyết định đã chốt:
- Đây là template prompt API.
- Không phải pricing formula.
- Không phải workflow engine nhiều bước trong v1.

## Docs Deliverable

Sau khi implement xong feature, phải tạo thêm file docs:
- `docs/template-prompt-formula-api.md`

File docs này phải chốt:
- format template và placeholder syntax
- precedence giữa `formulaId`, `formulaKey`, và inline `template`
- request/response contract
- validation rules cho missing variables
- audit log behavior
- scope giới hạn của v1 so với workflow engine

## Current State

Hiện trạng xác nhận:
- Repo chưa có formula/template catalog riêng cho AI generation text.
- Có sẵn pricing catalog cho coin và các flow caption generation, nhưng chưa có general-purpose formula API.

## Data Model

Thêm entity mới `PromptFormulaTemplate` trong `Ai.Microservice`:

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `Key` | `string` | unique business key |
| `Name` | `string` | admin label |
| `Template` | `string` | prompt template với placeholder `{{variable}}` |
| `OutputType` | `string` | `caption`, `hook`, `cta`, `outline`, `custom` |
| `DefaultLanguage` | `string?` | nullable |
| `DefaultInstruction` | `string?` | nullable |
| `IsActive` | `bool` | |
| `CreatedAt` | `DateTime` | |
| `UpdatedAt` | `DateTime?` | |

Thêm audit entity `FormulaGenerationLog`:
- `Id`
- `UserId`
- `WorkspaceId`
- `FormulaTemplateId`
- `FormulaKeySnapshot`
- `RenderedPrompt`
- `VariablesJson`
- `OutputType`
- `Model`
- `CreatedAt`

## Public API

User endpoint:
- `POST /api/Ai/formulas/generate`

Admin endpoints:
- `GET /api/Ai/admin/formulas`
- `POST /api/Ai/admin/formulas`
- `PUT /api/Ai/admin/formulas/{formulaId}`
- `DELETE /api/Ai/admin/formulas/{formulaId}`

Generate request:

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

Generate response:

```json
{
  "isSuccess": true,
  "value": {
    "formulaId": "uuid-or-null",
    "formulaKey": "launch-caption",
    "outputType": "caption",
    "renderedPrompt": "....",
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

Precedence:
1. `formulaId`
2. `formulaKey`
3. inline `template`

## Implementation Changes

Thêm service:
- `IFormulaTemplateRenderer`
- `IFormulaGenerationService`

Renderer rules:
- Placeholder syntax duy nhất: `{{variable_name}}`
- Placeholder matching case-insensitive
- Chỉ hỗ trợ scalar string/number/bool stringify trong v1

Generate flow:
1. Resolve template theo precedence trên
2. Validate template active nếu lấy từ catalog
3. Render prompt
4. Validate không còn placeholder chưa bind
5. Debit coin theo action type `formula_generation`
6. Gọi text generation service
7. Lưu `FormulaGenerationLog`
8. Trả `outputs`
9. Nếu generation fail thì refund coin

Pricing:
- thêm coin pricing catalog entry cho `formula_generation`

Model:
- mặc định `gpt-5-4`
- cho phép override sau này, nhưng v1 không expose model override cho user

## Validation And Errors

Validation:
- phải có `formulaId`, `formulaKey`, hoặc `template`
- `variantCount` default `1`, max `5`
- `outputType` bắt buộc

Errors:
- `Formula.NotFound`
- `Formula.Inactive`
- `Formula.TemplateMissing`
- `Formula.MissingVariable`
- `Formula.InvalidOutputType`
- `Billing.InsufficientFunds`

`Formula.MissingVariable` phải trả rõ tên biến còn thiếu.

## Tests

Happy path:
- generate bằng `formulaId`
- generate bằng `formulaKey`
- generate bằng inline `template`

Validation:
- thiếu variable bị fail đúng tên biến
- template còn placeholder chưa bind thì fail
- formula inactive bị block

Billing:
- debit trước generate
- fail thì refund đúng 1 lần

Audit:
- mỗi generate thành công lưu `FormulaGenerationLog`
- `renderedPrompt` trong log đúng với variables đã bind

## Completion Status

- [x] DONE Read and execute plan
- [x] DONE Implement prompt formula templates and persistence
- [x] DONE Implement formula generation services
- [x] DONE Implement public generate API
- [x] DONE Implement admin formula APIs
- [x] DONE Integrate billing and refund flow
- [x] DONE Persist audit log
- [x] DONE Add targeted tests
- [x] DONE Create docs/template-prompt-formula-api.md
- [x] DONE Run targeted validation

- V1 chỉ hỗ trợ output text.
- FE nếu cần UI quản lý formula sẽ dùng admin endpoints riêng.
- Không triển khai nested loops, conditional blocks, hoặc DSL template phức tạp trong v1.
