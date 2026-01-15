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

resource "cloudflare_record" "ses_domain_verification" {
  count           = local.ses_cloudflare_enabled ? 1 : 0
  zone_id         = var.cloudflare_zone_id
  name            = "_amazonses"
  type            = "TXT"
  content         = aws_ses_domain_identity.main[0].verification_token
  ttl             = 1
  proxied         = false
  allow_overwrite = true
}

resource "cloudflare_record" "ses_dkim" {
  for_each = local.ses_cloudflare_enabled ? {
    for token in aws_ses_domain_dkim.main[0].dkim_tokens : token => token
  } : {}

  zone_id         = var.cloudflare_zone_id
  name            = "${each.value}._domainkey"
  type            = "CNAME"
  content         = "${each.value}.dkim.amazonses.com"
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
