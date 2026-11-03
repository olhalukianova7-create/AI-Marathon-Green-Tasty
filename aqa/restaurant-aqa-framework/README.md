# Restaurant test automation

**Track 1 — our group pre-order implementation. Approach: C# / .NET 10, xUnit, Playwright Page Objects for UI, and an HTTP API client for authorization and validation.**

This directory is part of the public-submission repository
[AI-Marathon-Green-Tasty](https://github.com/olhalukianova7-create/AI-Marathon-Green-Tasty).
Repository visibility still needs to be verified before submission.
Tests target our deployed application. The first increment establishes smoke checks;
it does **not** complete homework Levels 1–3 or the optional ReportPortal integration.

Initial local verification against the deployed application: **4 passed, 0 failed,
0 skipped**, reported on 3 November 2026. GitHub CI and the AWS test runner still
need their first verified runs.

## Configure and run locally

Install the .NET 10 SDK and PowerShell (`pwsh`). From this directory:

```bash
cp .env.example .env
```

Fill in `AQA_UI_BASE_URL` with the deployed frontend URL and `AQA_API_BASE_URL`
with the deployed API Gateway URL **including its stage**. No addresses or passwords
are committed. `.env` is ignored by Git. Environment variables override file values;
CI uses environment variables exclusively. The file supports simple `KEY=value` lines,
optional surrounding quotes, and full-line comments; it does not expand variables.

```bash
dotnet build --configuration Release
pwsh ./bin/Release/net10.0/playwright.ps1 install chromium
bash scripts/run-tests.sh
```

On Linux use `install --with-deps chromium` to install browser system dependencies.
The runner uses the existing Release build; rebuild after editing test code.
Use `AQA_HEADLESS=false` for a visible local browser.

Subsets:

```bash
AQA_TEST_FILTER='Category=Smoke&Layer=API' bash scripts/run-tests.sh
AQA_TEST_FILTER='Category=Smoke&Layer=UI' bash scripts/run-tests.sh
```

Level tags will be added with their scenarios. Current tests create no accounts,
reservations, carts, or other persistent data, and each UI scenario has its own browser
context. Separate test classes can execute in parallel.

## Current coverage

| Layer | Scenario | Expected result |
| --- | --- | --- |
| UI | Anonymous visitor browses the menu and opens a dish | Menu contains dishes; selected dish details show the same name |
| UI | Anonymous visitor opens a group-order route | Redirect to login; group-order heading is not shown |
| API | Public dishes request | HTTP 200 and a nonempty JSON array |
| API | Anonymous group-participants request | HTTP 401 before group lookup |

The anonymous API check uses a random group ID to verify authentication before resource
lookup. Role restrictions on existing groups require fixtures and will be covered at Level 1.
No requests are mocked. The deployed environment must have menu data.

## Reports and failure investigation

`TestResults/results.html` is the human-readable test report;
`TestResults/results.trx` is the machine-readable report.
UI failures save a screenshot and Playwright trace under
`TestResults/failures/<scenario>-<unique-id>/`.
Open a downloaded trace locally:

```bash
pwsh ./bin/Release/net10.0/playwright.ps1 show-trace TestResults/failures/<scenario-id>/trace.zip
```

Build, dependency-installation, or browser-launch failures appear in runner logs;
screenshots and traces require a successfully started browser.
Reports and traces may contain application data; keep downloaded artifacts private.

## GitHub Actions

The repository workflow is `.github/workflows/aqa.yml`.
Create repository **Actions variables** `AQA_UI_BASE_URL` and `AQA_API_BASE_URL`, then
run **Restaurant AQA** from Actions. Changes to automation or application code on
`main` and pull requests also trigger it. Future test-account credentials belong in Actions secrets.

Reports and screenshots/traces are uploaded as `aqa-results-<run-id>-<attempt>` for
seven days. A failed test allows artifact collection to finish, then the job reports
failure. Fork pull requests must not be given test-account credentials.

## AWS CodeBuild

`buildspec.yml` runs the same tests in AWS and publishes the TRX report to a CodeBuild
report group, plus HTML reports and failure artifacts to the project's artifact bucket.
This file is prepared; **the AWS test project and GitHub source connection have not
been provisioned or verified yet**.

When provisioning the runner:

1. Use the selected project Region, `eu-north-1`, and CLI profile `aws-toolkit`.
2. Use the managed Ubuntu image `aws/codebuild/standard:7.0`, with the .NET 10 runtime
   and PowerShell. Use an EC2-backed Linux build environment for `on-failure: CONTINUE`.
3. Set the buildspec path to `aqa/restaurant-aqa-framework/buildspec.yml`.
4. Connect this GitHub repository through CodeConnections. A newly created connection
   requires the GitHub authorization step in the AWS console.
5. Configure the two base URL variables, a private encrypted S3 artifacts destination,
   CloudWatch logging, and permissions scoped to that bucket and the AQA report group.
   Future account credentials must use Secrets Manager references.
6. Trigger a build and verify the report, failure artifacts, and build status.

Test failures continue to the post-build phase; CodeBuild still records failure and
collects reports/artifacts. Infrastructure creation, authentication, billing-plan
verification, and a live test run remain deployment steps.

References: [Playwright setup](https://playwright.dev/dotnet/docs/intro),
[CodeBuild runtimes](https://docs.aws.amazon.com/codebuild/latest/userguide/available-runtimes.html),
[CodeBuild buildspec and reports](https://docs.aws.amazon.com/codebuild/latest/userguide/build-spec-ref.html).

## Remaining homework increments

1. **Level 1:** create reservations and guest baskets; verify owner participant views,
   quantities, totals and updates, pending/empty carts, empty groups, missing groups,
   and restrictions for other customers, guests, waiters, and anonymous users.
2. **Level 2:** consolidate only confirmed carts (including the owner's); verify dish
   aggregation, group/order status, waiter visibility, each rejection condition,
   repeated submission, and owner-only submission.
3. **Level 3:** remove participants; verify totals, confirmed and last participants,
   cross-group HTTP 400, missing-participant HTTP 404, role restrictions, and the
   complete remove → submit → waiter workflow.
4. Use unique data, dynamic future dates, independent fixtures, and cleanup for all
   mutating tests. Document unspecified behavior from the actual implementation.
5. Publish the changes to GitHub, verify repository visibility, run GitHub CI and AWS
   CodeBuild, then optionally add ReportPortal for the starred task.
