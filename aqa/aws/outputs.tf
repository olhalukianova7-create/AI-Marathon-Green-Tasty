output "codebuild_project_name" {
  value = aws_codebuild_project.aqa.name
}

output "codebuild_console_url" {
  value = "https://${var.aws_region}.console.aws.amazon.com/codesuite/codebuild/${local.account_id}/projects/${aws_codebuild_project.aqa.name}?region=${var.aws_region}"
}

output "artifact_bucket" {
  value = aws_s3_bucket.artifacts.bucket
}

output "report_group_arn" {
  value = aws_codebuild_report_group.aqa.arn
}

output "log_group" {
  value = aws_cloudwatch_log_group.aqa.name
}
