locals {
  ses_enabled = var.domain_name != ""
  ses_cloudflare_enabled = local.ses_enabled && var.use_cloudflare && var.cloudflare_zone_id != ""
}

resource "aws_ses_domain_identity" "main" {
  count  = local.ses_enabled ? 1 : 0
  domain = var.domain_name
}

resource "aws_ses_domain_dkim" "main" {
  count  = local.ses_enabled ? 1 : 0
  domain = aws_ses_domain_identity.main[0].domain
}

resource "terraform_data" "ses_domain_verification_cleanup" {
  count = local.ses_cloudflare_enabled ? 1 : 0

  triggers_replace = [
    aws_ses_domain_identity.main[0].verification_token
  ]

  provisioner "local-exec" {
    interpreter = ["/usr/bin/env", "bash", "-c"]

    environment = {
      CLOUDFLARE_API_TOKEN = var.cloudflare_api_token
      CLOUDFLARE_ZONE_ID   = var.cloudflare_zone_id
      DOMAIN_NAME          = var.domain_name
    }

    command = <<-EOT
      set -euo pipefail

      record_name="_amazonses.$DOMAIN_NAME"
      response=$(curl -fsS --get "https://api.cloudflare.com/client/v4/zones/$CLOUDFLARE_ZONE_ID/dns_records" \
        -H "Authorization: Bearer $CLOUDFLARE_API_TOKEN" \
        -H "Content-Type: application/json" \
        --data-urlencode "type=TXT" \
        --data-urlencode "name=$record_name" \
        --data-urlencode "per_page=100")

      printf '%s' "$response" | python3 -c 'import json, sys; data = json.load(sys.stdin); assert data.get("success"), data.get("errors"); print("\n".join(record["id"] for record in data.get("result", [])))' \
        | while IFS= read -r record_id; do
            [ -n "$record_id" ] || continue
            curl -fsS -X DELETE "https://api.cloudflare.com/client/v4/zones/$CLOUDFLARE_ZONE_ID/dns_records/$record_id" \
              -H "Authorization: Bearer $CLOUDFLARE_API_TOKEN" \
              -H "Content-Type: application/json" >/dev/null
          done
    EOT
  }
}

resource "cloudflare_record" "ses_domain_verification" {
  count           = local.ses_cloudflare_enabled ? 1 : 0
  zone_id         = var.cloudflare_zone_id
  name            = "_amazonses"
  type            = "TXT"
  content         = aws_ses_domain_identity.main[0].verification_token
  ttl             = 1
  proxied         = false
  allow_overwrite = true

  depends_on = [
    terraform_data.ses_domain_verification_cleanup
  ]
}

resource "cloudflare_record" "ses_dkim" {
  for_each = local.ses_cloudflare_enabled ? {
    "0" = 0
    "1" = 1
    "2" = 2
  } : {}

  zone_id         = var.cloudflare_zone_id
  name            = "${aws_ses_domain_dkim.main[0].dkim_tokens[each.value]}._domainkey"
  type            = "CNAME"
  content         = "${aws_ses_domain_dkim.main[0].dkim_tokens[each.value]}.dkim.amazonses.com"
  ttl             = 1
  proxied         = false
  allow_overwrite = true
}

resource "aws_ses_domain_identity_verification" "main" {
  count  = local.ses_cloudflare_enabled ? 1 : 0
  domain = aws_ses_domain_identity.main[0].domain

  depends_on = [
    cloudflare_record.ses_domain_verification,
    cloudflare_record.ses_dkim
  ]
}

resource "aws_iam_user" "ses_smtp" {
  count = local.ses_enabled ? 1 : 0
  name  = "${var.project_name}-ses-smtp"
}

resource "aws_iam_user_policy" "ses_smtp_send" {
  count = local.ses_enabled ? 1 : 0
  user  = aws_iam_user.ses_smtp[0].name

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["ses:SendEmail", "ses:SendRawEmail"]
        Resource = "*"
      }
    ]
  })
}

resource "aws_iam_access_key" "ses_smtp" {
  count = local.ses_enabled ? 1 : 0
  user  = aws_iam_user.ses_smtp[0].name
}

locals {
  ses_smtp_username = local.ses_enabled ? aws_iam_access_key.ses_smtp[0].id : ""
  ses_smtp_secret   = local.ses_enabled ? aws_iam_access_key.ses_smtp[0].secret : ""
  ses_smtp_password_v4 = local.ses_enabled ? aws_iam_access_key.ses_smtp[0].ses_smtp_password_v4 : ""
}
