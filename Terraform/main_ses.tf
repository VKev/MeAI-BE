locals {
  ses_enabled = var.domain_name != ""
}

resource "aws_ses_domain_identity" "main" {
  count  = local.ses_enabled ? 1 : 0
  domain = var.domain_name
}

resource "aws_ses_domain_dkim" "main" {
  count  = local.ses_enabled ? 1 : 0
  domain = aws_ses_domain_identity.main[0].domain
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

  ses_smtp_k_date    = local.ses_enabled ? hmacsha256("AWS4${local.ses_smtp_secret}", "11111111") : ""
  ses_smtp_k_region  = local.ses_enabled ? hmacsha256(base64decode(local.ses_smtp_k_date), var.aws_region) : ""
  ses_smtp_k_service = local.ses_enabled ? hmacsha256(base64decode(local.ses_smtp_k_region), "ses") : ""
  ses_smtp_k_signing = local.ses_enabled ? hmacsha256(base64decode(local.ses_smtp_k_service), "aws4_request") : ""
  ses_smtp_signature = local.ses_enabled ? hmacsha256(base64decode(local.ses_smtp_k_signing), "SendRawEmail") : ""

  ses_smtp_password_v4 = local.ses_enabled ? base64encode(
    join("", ["\x04", base64decode(local.ses_smtp_signature)])
  ) : ""
}
