# AWS test runner

This standalone Terraform configuration provisions the Track 1 AQA runner in
`eu-north-1`. It has its own local Terraform state and does not use the restaurant
application's Terraform state or backend.

The runner uses a versioned S3 source archive and
`aqa/restaurant-aqa-framework/buildspec.yml`. The same four smoke tests run in
GitHub Actions and AWS. GitHub CI runs automatically on pushes; AWS runs are
started manually with `bash aqa/aws/run-tests.sh`, which uploads the committed test
sources and starts CodeBuild. This does not add CodePipeline or complete the
remaining homework levels.

The direct GitHub integration was blocked by an AWS Organizations policy denying
CodeConnections access to the service role. Listing GitHub OIDC providers was also
explicitly denied by an SCP. The S3 source path uses the existing CLI login and
the supported S3 and CodeBuild services. It does not change organization policies,
add long-lived access keys, or automatically upgrade the project plan. The previously
created connection is unused and remains outside Terraform management.

## Resources and access

- A small Linux CodeBuild environment, with a 15-minute build timeout and one build
  at a time. Each run is pinned to the uploaded S3 object version. GitHub pushes
  and pull requests do not trigger AWS builds.
- A service role restricted to this CodeBuild project's trust ARN, the selected
  test report group, artifact bucket, encryption key, and log group. Source reads
  are restricted to the artifact bucket's `source/` prefix.
- A private, versioned S3 bucket with KMS encryption, public-access blocking, HTTPS
  enforcement, and seven-day artifact expiration. Noncurrent versions expire seven
  days after becoming noncurrent, so physical deletion can occur later.
- A KMS-encrypted CloudWatch log group with seven-day retention, a rotating KMS key,
  and a CodeBuild report group. KMS key destruction is protected to keep retained
  reports readable. CodeBuild's test report retention is managed by the service.

Build minutes, KMS, logs, and storage use applicable service allowances or project
credits; an active Free plan does not make every resource free. No capacity is kept
running between on-demand builds. Keep artifacts and state private; reports and
traces can contain application data.

## Configure

Prerequisites: Terraform 1.10+, AWS CLI, a valid `aws-toolkit` login, and the
automation committed to GitHub.

From the repository root:

```bash
cd aqa/aws
cp terraform.tfvars.example terraform.tfvars
```

Fill in the frontend origin and API URL including its stage.
If `terraform.tfvars` has already been prepared locally, keep that file.

The existing AWS provider does not read the newer `aws login` credential format.
Configure AWS's supported credential-process bridge once. It delegates to the
`aws-toolkit` login and refreshes temporary credentials through the CLI; it stores
only a command in the AWS configuration, not access keys:

```bash
aws configure set credential_process \
  'aws configure export-credentials --profile aws-toolkit --format process' \
  --profile aws-toolkit-terraform
aws configure set region eu-north-1 --profile aws-toolkit-terraform
```

Terraform defaults to `aws-toolkit-terraform`; resource-management CLI commands
continue to use `aws-toolkit`. No application account passwords are needed by the
initial smoke tests. Future credentials must use Secrets Manager references.
See [AWS's credential-process bridge documentation](https://docs.aws.amazon.com/cli/latest/userguide/cli-configure-sign-in.html#cli-configure-sign-in-credentials).

## Validate, review, deploy

```bash
terraform init
terraform fmt -check
terraform validate
terraform plan -out=aqa.tfplan
```

Review the plan. It should only add or update AQA resources. Stop on validation, authentication,
or permission errors before applying. The saved plan and local state must stay out
of Git. Keep the local state file after deployment because Terraform needs it for
future updates and cleanup.

```bash
terraform apply aqa.tfplan
terraform output
```

This configuration was formatted and initialized using the existing cached AWS
provider. Full provider validation and a live plan must run from the user's terminal:
the agent session blocks the provider subprocess and outbound AWS access.

## Publish and verify

The buildspec reads the explicit report group ARN from CodeBuild. Commit both
the infrastructure directory and buildspec update before starting an AWS build:

```bash
git add aqa/aws aqa/restaurant-aqa-framework/buildspec.yml aqa/restaurant-aqa-framework/README.md
git commit -m "Add Terraform-managed AWS CodeBuild test runner"
git push origin main
```

Run these Git commands from the repository root. Terraform configuration, examples,
documentation, and the provider lock file are committed; `.terraform`, actual `.tfvars`,
saved plans, and state are ignored. After deployment and committing the changes,
start the first AWS build from the repository root:

```bash
bash aqa/aws/run-tests.sh
```

The script uploads only the committed framework directory, pins the input to the
returned S3 version ID, and prints the build ID and console URL. It refuses to
run while AQA files have uncommitted changes. It uses Terraform's local outputs
to find the bucket and project, and AWS CLI profile `aws-toolkit` for upload/start.

Open the `codebuild_console_url` output, select the build, and inspect logs, the test
report, and S3 artifacts. Verify a successful run before marking AWS setup complete.
Test failures continue to post-build and artifact collection while preserving build
failure status. Reports use the precreated report group rather than broad permissions
to create arbitrary groups.

For cleanup, explicitly decide whether to keep or remove the runner. There are no
automatic AWS triggers to disable. The artifact bucket refuses deletion
while nonempty, and the KMS key has `prevent_destroy`; never delete or disable the key
while keeping encrypted artifacts or logs. The unused GitHub connection is external
to this Terraform configuration and is not deleted with the runner.

References: [CodeBuild S3 source versions](https://docs.aws.amazon.com/codebuild/latest/userguide/sample-source-version.html),
[Terraform AWS CodeBuild project](https://registry.terraform.io/providers/hashicorp/aws/latest/docs/resources/codebuild_project).
