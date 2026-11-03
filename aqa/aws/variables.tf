variable "aws_profile" {
  type        = string
  default     = "aws-toolkit-terraform"
  description = "Credential-process profile that delegates to the aws-toolkit CLI login."
}

variable "aws_region" {
  type    = string
  default = "eu-north-1"
  validation {
    condition     = var.aws_region == "eu-north-1"
    error_message = "This project must use its selected Region, eu-north-1."
  }
}

variable "project_name" {
  type    = string
  default = "green-tasty-aqa"
  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{2,39}$", var.project_name))
    error_message = "Use 3–40 lowercase letters, numbers, or hyphens, starting with a letter."
  }
}

variable "ui_base_url" {
  type        = string
  description = "Deployed frontend base URL, without a page path or query."
  validation {
    condition     = can(regex("^https://[^/?#@ ]+/?$", var.ui_base_url))
    error_message = "Supply an HTTPS frontend origin."
  }
}

variable "api_base_url" {
  type        = string
  description = "Deployed API base URL including its API Gateway stage."
  validation {
    condition     = can(regex("^https://[^/?#@ ]+(/[^?#@ ]*)?$", var.api_base_url))
    error_message = "Supply an HTTPS API base URL without credentials, query, or fragment."
  }
}
