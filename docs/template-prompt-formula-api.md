# Template Prompt Formula API

## Trạng thái triển khai

Tài liệu này mô tả trạng thái backend hiện tại của template prompt formula API trong `Ai.Microservice`.

### API đã triển khai

- [x] `POST /api/Ai/formulas/generate`
- [x] `GET /api/Ai/admin/formulas`
- [x] `POST /api/Ai/admin/formulas`
- [x] `PUT /api/Ai/admin/formulas/{formulaId}`
- [x] `DELETE /api/Ai/admin/formulas/{formulaId}`

## Mục tiêu

Feature này chuẩn hóa generate nội dung text từ template prompt.

V1 chỉ hỗ trợ text output, không phải workflow engine nhiều bước.

## Template format

Placeholder hiện hỗ trợ:

```text
{{variable_name}}
```

Quy tắc:

- so khớp biến không phân biệt hoa thường
- chỉ hỗ trợ scalar value
- array/object không hợp lệ

## Precedence chọn template

1. `formulaId`
2. `formulaKey`
3. inline `template`

## Generate request

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

## Generate response

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

## Admin catalog

Admin APIs quản lý catalog prompt formula:

- list
- create
- update
- delete soft-disable bằng inactive

## Validation

- tối thiểu phải có một trong `formulaId`, `formulaKey`, `template`
- `outputType` là bắt buộc
- `variantCount` mặc định `1`
- `variantCount` tối đa `5`
- catalog template phải active
- thiếu variable sẽ trả `Formula.MissingVariable`

## Billing và audit

- billing action type: `formula_generation`
- debit trước khi generate
- fail thì refund đúng một lần
- success thì tạo `FormulaGenerationLog`

## Giới hạn v1

- không có loops
- không có condition blocks
- không có nested template execution
- không cho user override model
