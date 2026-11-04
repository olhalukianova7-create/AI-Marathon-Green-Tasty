#!/usr/bin/env bash
set -euo pipefail

# Deploy this checkout's Level 2 fixes to the existing Green Tasty environment.
framework_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
repository_directory="$(git -C "$framework_directory" rev-parse --show-toplevel)"
frontend_directory="$repository_directory/frontend/restaurant-frontend-app"
backend_project="$repository_directory/backend/restaurant-backend-app/dnapp/lambdas/api-handler/APIHandler.csproj"
deployment_directory="$(mktemp -d "${TMPDIR:-/tmp}/green-tasty-level2.XXXXXX")"
trap 'rm -rf "$deployment_directory"' EXIT

aws_project() {
  aws "$@" --profile aws-toolkit --region eu-north-1 --no-cli-pager
}

echo 'Checking the AWS deployment target...'
project_id="$(aws_project sts get-caller-identity --query Account --output text)"
if [[ "$project_id" != '496336474077' ]]; then
  echo 'Unexpected AWS project. Stopping before deployment.' >&2
  exit 1
fi
aws_project lambda get-function-configuration --function-name api-handler \
  --query '{Runtime:Runtime,Architectures:Architectures,Handler:Handler,RevisionId:RevisionId,PackageType:PackageType}' \
  --output json > "$deployment_directory/function.json"
revision_id="$(python3 - "$deployment_directory/function.json" <<'PY'
import json
import sys
with open(sys.argv[1]) as source:
    config = json.load(source)
if (config['Runtime'] != 'dotnet10'
        or config['Architectures'] != ['x86_64']
        or config['Handler'] != 'APIHandler::APIHandler.Function::FunctionHandler'
        or config['PackageType'] != 'Zip'):
    sys.exit('Lambda configuration changed. Stopping before deployment.')
print(config['RevisionId'])
PY
)"
distribution_domain="$(aws_project cloudfront get-distribution --id E1UXDQ7DMKM73Q \
  --query Distribution.DomainName --output text)"
if [[ "$distribution_domain" != 'd334z9ppars29w.cloudfront.net' ]]; then
  echo 'Unexpected CloudFront distribution. Stopping before deployment.' >&2
  exit 1
fi

echo 'Building the Linux x64 backend package...'
dotnet publish "$backend_project" --configuration Release --framework net10.0 \
  --runtime linux-x64 --self-contained false --output "$deployment_directory/publish"
python3 - "$deployment_directory" <<'PY'
from pathlib import Path
import sys
import zipfile
directory = Path(sys.argv[1])
publish = directory / 'publish'
for required in ('APIHandler.dll', 'APIHandler.deps.json', 'APIHandler.runtimeconfig.json'):
    if not (publish / required).is_file():
        sys.exit(f'Missing deployment file: {required}')
with zipfile.ZipFile(directory / 'api-handler.zip', 'w', zipfile.ZIP_DEFLATED) as package:
    for path in sorted(publish.rglob('*')):
        if path.is_file():
            package.write(path, path.relative_to(publish))
PY

echo 'Building the production frontend and automation before deployment...'
(
  cd "$frontend_directory"
  BACKEND_URL='https://06wfifoo60.execute-api.eu-north-1.amazonaws.com/develop' npm run build
)
dotnet build "$framework_directory/Restaurant.Aqa.csproj" --configuration Release

echo 'Uploading and deploying the backend code...'
artifact_bucket='green-tasty-aqa-496336474077-eu-north-1'
artifact_key="deployments/level2/$(basename "$deployment_directory")/api-handler.zip"
aws_project s3 cp "$deployment_directory/api-handler.zip" \
  "s3://$artifact_bucket/$artifact_key" --only-show-errors
artifact_version="$(aws_project s3api head-object --bucket "$artifact_bucket" \
  --key "$artifact_key" --query VersionId --output text)"
if [[ -z "$artifact_version" || "$artifact_version" == 'None' ]]; then
  echo 'Could not identify the uploaded package version. Stopping.' >&2
  exit 1
fi
aws_project lambda update-function-code --function-name api-handler \
  --s3-bucket "$artifact_bucket" --s3-key "$artifact_key" \
  --s3-object-version "$artifact_version" --revision-id "$revision_id" \
  --query '{FunctionName:FunctionName,LastUpdateStatus:LastUpdateStatus}'
aws_project lambda wait function-updated-v2 --function-name api-handler

echo 'Uploading the frontend and refreshing CloudFront...'
aws_project s3 sync "$frontend_directory/dist/restaurant-frontend-app/browser/" \
  s3://restaurant-frontend-496336474077/ --only-show-errors
invalidation_id="$(aws_project cloudfront create-invalidation \
  --distribution-id E1UXDQ7DMKM73Q --paths '/*' \
  --query Invalidation.Id --output text)"
echo "Waiting for CloudFront invalidation $invalidation_id..."
aws_project cloudfront wait invalidation-completed \
  --distribution-id E1UXDQ7DMKM73Q --id "$invalidation_id"

echo 'Deployment complete. Running all 14 Level 2 cases...'
AQA_TEST_FILTER='Category=Level2' \
  AQA_RESULTS_DIR="$framework_directory/TestResults/level2" \
  bash "$framework_directory/scripts/run-tests.sh"
echo 'Level 2 deployment and tests passed. GitHub and CodeBuild verification are next.'
