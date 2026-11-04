#!/usr/bin/env bash
set -euo pipefail

aws_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repository_directory="$(git -C "$aws_directory" rev-parse --show-toplevel)"
aws_profile="${AQA_AWS_PROFILE:-aws-toolkit}"
aws_region="eu-north-1"

if [[ -n "$(git -C "$repository_directory" status --porcelain --untracked-files=normal -- aqa)" ]]; then
  echo 'Commit the AQA changes before starting an AWS run. The source archive uses committed files only.' >&2
  exit 1
fi

artifact_bucket="$(terraform -chdir="$aws_directory" output -raw artifact_bucket)"
build_project="$(terraform -chdir="$aws_directory" output -raw codebuild_project_name)"
console_url="$(terraform -chdir="$aws_directory" output -raw codebuild_console_url)"
commit_id="$(git -C "$repository_directory" rev-parse HEAD)"

work_directory="$(mktemp -d)"
source_archive="$work_directory/restaurant.zip"
trap 'rm -f -- "$source_archive"; rmdir -- "$work_directory"' EXIT

# Archive only committed test sources, including the expected repository-relative path.
# Local .env, Terraform state, provider files and test artifacts are never included.
git -C "$repository_directory" archive --format=zip --output="$source_archive" HEAD \
  aqa/restaurant-aqa-framework backend/restaurant-backend-app/dnapp/lambdas/api-handler

echo "Uploading test source from commit $commit_id"
source_version="$(aws s3api put-object \
  --bucket "$artifact_bucket" --key source/restaurant.zip \
  --body "$source_archive" --metadata "commit=$commit_id" \
  --query VersionId --output text \
  --profile "$aws_profile" --region "$aws_region" --no-cli-pager)"

if [[ -z "$source_version" || "$source_version" == 'None' || "$source_version" == 'null' ]]; then
  echo 'Source upload returned no object version. Verify that bucket versioning is enabled.' >&2
  exit 1
fi

# Pin the exact uploaded version, so another upload cannot change this run's input.
aws codebuild start-build \
  --project-name "$build_project" --source-version "$source_version" \
  --query 'build.{Id:id,Status:buildStatus}' \
  --profile "$aws_profile" --region "$aws_region" --no-cli-pager

echo "View logs, reports and artifacts: $console_url"
