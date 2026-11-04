#!/usr/bin/env python3
"""Copy private account configuration to GitHub secrets or AWS Secrets Manager."""

import argparse
import json
from pathlib import Path
import runpy
import subprocess
import sys
import tempfile

REPOSITORY = "olhalukianova7-create/AI-Marathon-Green-Tasty"
FIELDS = ["AQA_" + role + "_" + value
          for role in ("OWNER", "OTHER_CUSTOMER", "WAITER")
          for value in ("EMAIL", "PASSWORD")]


def execute(arguments, input_value=None):
    return subprocess.run(arguments, input=input_value, text=True, capture_output=True)


def github(values):
    for key in FIELDS:
        # Send secret values on stdin, never as process arguments or log output.
        result = execute(["gh", "secret", "set", key, "--repo", REPOSITORY], values[key])
        if result.returncode:
            raise RuntimeError("Could not configure GitHub secret " + key + ". Check gh auth status.")
        print("Configured GitHub secret: " + key)
    result = execute(["gh", "variable", "set", "AQA_TEST_FILTER", "--repo", REPOSITORY,
                      "--body", "Category=Smoke|Category=Level1"])
    if result.returncode:
        raise RuntimeError("Secrets are configured, but setting the default test suite failed.")
    print("GitHub default suite: Smoke + Level1 (16 cases). Pull requests remain smoke-only.")


def aws(values):
    base = ["aws", "--profile", "aws-toolkit", "--region", "eu-north-1", "--no-cli-pager", "secretsmanager"]
    name = "green-tasty-aqa/accounts"
    existing = execute(base + ["describe-secret", "--secret-id", name, "--query", "ARN", "--output", "text"])
    if existing.returncode and "ResourceNotFoundException" not in existing.stderr:
        raise RuntimeError("Could not inspect the AWS account secret. Check AWS login and Secrets Manager permissions.")

    # NamedTemporaryFile creates a private file (0600). It is removed even if the
    # AWS command fails. Terraform receives only the secret ARN, never its values.
    with tempfile.NamedTemporaryFile(mode="w", suffix=".json", encoding="utf-8") as secret_file:
        json.dump({key: values[key] for key in FIELDS}, secret_file)
        secret_file.flush()
        if existing.returncode == 0:
            arn = existing.stdout.strip()
            result = execute(base + ["put-secret-value", "--secret-id", arn,
                                     "--secret-string", "file://" + secret_file.name])
        else:
            result = execute(base + ["create-secret", "--name", name,
                                     "--description", "Dedicated restaurant AQA test accounts",
                                     "--secret-string", "file://" + secret_file.name,
                                     "--query", "ARN", "--output", "text"])
            arn = result.stdout.strip()
    if result.returncode:
        raise RuntimeError("Could not save the account secret in AWS. No secret contents have been logged.")
    if not arn.startswith("arn:aws:secretsmanager:eu-north-1:"):
        raise RuntimeError("AWS returned an unexpected secret ARN; Terraform configuration was not changed.")

    path = Path(__file__).resolve().parents[2] / "aws" / "terraform.tfvars"
    if not path.exists():
        raise RuntimeError("The secret was saved, but private aqa/aws/terraform.tfvars is missing. Add account_secret_arn manually.")
    lines = path.read_text().splitlines()
    lines = [line for line in lines if line.split("=", 1)[0].strip() != "account_secret_arn"]
    lines.append("account_secret_arn = " + json.dumps(arn))
    path.write_text("\n".join(lines) + "\n")
    print("AWS account secret configured. Only its ARN was saved in private terraform.tfvars.")
    print("Next: plan and apply the aqa/aws Terraform update to grant CodeBuild access.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    destination = parser.add_mutually_exclusive_group(required=True)
    destination.add_argument("--github", action="store_true", help="Configure this repository's Actions secrets")
    destination.add_argument("--aws", action="store_true", help="Configure the account secret in eu-north-1")
    args = parser.parse_args()
    helpers = runpy.run_path(str(Path(__file__).with_name("prepare-accounts.py")))
    values = helpers["read_configuration"]()
    if args.github:
        github(values)
    else:
        aws(values)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        # Configuration and wrapper errors contain field names or operation names,
        # never request bodies. Subprocess stderr is intentionally not printed.
        print("CI setup failed: " + str(error), file=sys.stderr)
        sys.exit(1)
