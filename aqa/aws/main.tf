terraform {
  required_version = ">= 1.10.0"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.100"
    }
  }
}

provider "aws" {
  region  = var.aws_region
  profile = var.aws_profile
  default_tags {
    tags = { Project = "GreenTasty", Purpose = "AQA", ManagedBy = "Terraform" }
  }
}

data "aws_caller_identity" "current" {}

locals {
  account_id  = data.aws_caller_identity.current.account_id
  project_arn = "arn:aws:codebuild:${var.aws_region}:${local.account_id}:project/${var.project_name}"
  log_name    = "/aws/codebuild/${var.project_name}"
  log_arn     = "arn:aws:logs:${var.aws_region}:${local.account_id}:log-group:${local.log_name}"
}

# Keep this key while retained reports exist, so they remain decryptable.
resource "aws_kms_key" "aqa" {
  description             = "Encryption for ${var.project_name} test artifacts and logs"
  enable_key_rotation     = true
  deletion_window_in_days = 30
  lifecycle { prevent_destroy = true }
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid       = "EnableProjectIAMPermissions"
        Effect    = "Allow"
        Principal = { AWS = "arn:aws:iam::${local.account_id}:root" }
        Action    = "kms:*"
        Resource  = "*"
      },
      {
        Sid       = "EncryptOnlyAQALogs"
        Effect    = "Allow"
        Principal = { Service = "logs.${var.aws_region}.amazonaws.com" }
        Action    = ["kms:Encrypt", "kms:Decrypt", "kms:ReEncrypt*", "kms:GenerateDataKey*", "kms:DescribeKey"]
        Resource  = "*"
        Condition = { ArnEquals = { "kms:EncryptionContext:aws:logs:arn" = local.log_arn } }
      }
    ]
  })
}

resource "aws_cloudwatch_log_group" "aqa" {
  name              = local.log_name
  retention_in_days = 7
  kms_key_id        = aws_kms_key.aqa.arn
}

resource "aws_s3_bucket" "artifacts" {
  bucket        = "${var.project_name}-${local.account_id}-${var.aws_region}"
  force_destroy = false
}

resource "aws_s3_bucket_public_access_block" "artifacts" {
  bucket                  = aws_s3_bucket.artifacts.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_versioning" "artifacts" {
  bucket = aws_s3_bucket.artifacts.id
  versioning_configuration { status = "Enabled" }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "artifacts" {
  bucket = aws_s3_bucket.artifacts.id
  rule {
    bucket_key_enabled = true
    apply_server_side_encryption_by_default {
      sse_algorithm     = "aws:kms"
      kms_master_key_id = aws_kms_key.aqa.arn
    }
  }
}

resource "aws_s3_bucket_lifecycle_configuration" "artifacts" {
  bucket     = aws_s3_bucket.artifacts.id
  depends_on = [aws_s3_bucket_versioning.artifacts]
  rule {
    id     = "ExpireTestArtifacts"
    status = "Enabled"
    filter {}
    expiration { days = 7 }
    noncurrent_version_expiration { noncurrent_days = 7 }
    abort_incomplete_multipart_upload { days_after_initiation = 1 }
  }
}

resource "aws_s3_bucket_policy" "artifacts" {
  bucket = aws_s3_bucket.artifacts.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Sid       = "RequireTLS"
      Effect    = "Deny"
      Principal = "*"
      Action    = "s3:*"
      Resource  = [aws_s3_bucket.artifacts.arn, "${aws_s3_bucket.artifacts.arn}/*"]
      Condition = { Bool = { "aws:SecureTransport" = "false" } }
    }]
  })
}

resource "aws_codebuild_report_group" "aqa" {
  name = "${var.project_name}-tests"
  type = "TEST"
  export_config { type = "NO_EXPORT" }
}

resource "aws_iam_role" "codebuild" {
  name = "${var.project_name}-codebuild"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "codebuild.amazonaws.com" }
      Action    = "sts:AssumeRole"
      Condition = {
        StringEquals = { "aws:SourceAccount" = local.account_id }
        ArnEquals    = { "aws:SourceArn" = local.project_arn }
      }
    }]
  })
}

resource "aws_iam_role_policy" "codebuild" {
  name = "${var.project_name}-resources"
  role = aws_iam_role.codebuild.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = concat([
      {
        Sid      = "ReadTestSourceArchive"
        Effect   = "Allow"
        Action   = ["s3:GetObject", "s3:GetObjectVersion"]
        Resource = "${aws_s3_bucket.artifacts.arn}/source/*"
      },
      {
        Sid      = "WriteBuildLogs"
        Effect   = "Allow"
        Action   = ["logs:CreateLogStream", "logs:PutLogEvents"]
        Resource = "${local.log_arn}:*"
      },
      {
        Sid      = "ArtifactBucketMetadata"
        Effect   = "Allow"
        Action   = ["s3:GetBucketAcl", "s3:GetBucketLocation"]
        Resource = aws_s3_bucket.artifacts.arn
      },
      {
        Sid      = "WriteBuildArtifacts"
        Effect   = "Allow"
        Action   = ["s3:PutObject"]
        Resource = "${aws_s3_bucket.artifacts.arn}/*"
      },
      {
        Sid      = "EncryptBuildArtifacts"
        Effect   = "Allow"
        Action   = ["kms:Encrypt", "kms:Decrypt", "kms:GenerateDataKey", "kms:DescribeKey"]
        Resource = aws_kms_key.aqa.arn
      },
      {
        Sid      = "WriteTestResults"
        Effect   = "Allow"
        Action   = ["codebuild:CreateReport", "codebuild:UpdateReport", "codebuild:BatchPutTestCases"]
        Resource = aws_codebuild_report_group.aqa.arn
      }
      ], var.account_secret_arn == "" ? [] : [{
        Sid      = "ReadDedicatedTestAccounts"
        Effect   = "Allow"
        Action   = ["secretsmanager:GetSecretValue"]
        Resource = var.account_secret_arn
    }])
  })
}

resource "aws_codebuild_project" "aqa" {
  name                   = var.project_name
  description            = "Green & Tasty deployed application automation tests"
  service_role           = aws_iam_role.codebuild.arn
  build_timeout          = 15
  queued_timeout         = 15
  concurrent_build_limit = 1
  encryption_key         = aws_kms_key.aqa.arn
  depends_on = [
    aws_iam_role_policy.codebuild,
    aws_s3_bucket_policy.artifacts,
    aws_s3_bucket_public_access_block.artifacts,
    aws_s3_bucket_server_side_encryption_configuration.artifacts
  ]

  artifacts {
    type                   = "S3"
    location               = aws_s3_bucket.artifacts.bucket
    name                   = "results.zip"
    namespace_type         = "BUILD_ID"
    packaging              = "ZIP"
    encryption_disabled    = false
    override_artifact_name = false
  }

  environment {
    compute_type                = "BUILD_GENERAL1_SMALL"
    image                       = "aws/codebuild/standard:7.0"
    type                        = "LINUX_CONTAINER"
    image_pull_credentials_type = "CODEBUILD"
    privileged_mode             = false
    environment_variable {
      name  = "AQA_UI_BASE_URL"
      value = var.ui_base_url
    }
    environment_variable {
      name  = "AQA_API_BASE_URL"
      value = var.api_base_url
    }
    environment_variable {
      name  = "AQA_REPORT_GROUP_ARN"
      value = aws_codebuild_report_group.aqa.arn
    }
    environment_variable {
      name  = "AQA_TEST_FILTER"
      value = var.account_secret_arn == "" ? "Category=Smoke" : "Category=Smoke|Category=Level1"
    }
    dynamic "environment_variable" {
      for_each = var.account_secret_arn == "" ? toset([]) : toset([
        "AQA_OWNER_EMAIL", "AQA_OWNER_PASSWORD", "AQA_OTHER_CUSTOMER_EMAIL",
        "AQA_OTHER_CUSTOMER_PASSWORD", "AQA_WAITER_EMAIL", "AQA_WAITER_PASSWORD"
      ])
      content {
        name  = environment_variable.value
        value = "${var.account_secret_arn}:${environment_variable.value}"
        type  = "SECRETS_MANAGER"
      }
    }
  }

  source {
    type      = "S3"
    location  = "${aws_s3_bucket.artifacts.bucket}/source/restaurant.zip"
    buildspec = "aqa/restaurant-aqa-framework/buildspec.yml"
  }

  logs_config {
    cloudwatch_logs {
      status     = "ENABLED"
      group_name = aws_cloudwatch_log_group.aqa.name
    }
  }

}
