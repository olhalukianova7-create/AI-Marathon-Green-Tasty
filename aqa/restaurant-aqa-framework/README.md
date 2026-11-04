# Restaurant test automation

**Track 1 — our group pre-order implementation. Approach: C# / .NET 10, xUnit, Playwright Page Objects for UI, and an HTTP API client for authorization and validation.**

This directory is part of the public-submission repository
[AI-Marathon-Green-Tasty](https://github.com/olhalukianova7-create/AI-Marathon-Green-Tasty).
Repository visibility still needs to be verified before submission.
Tests target our deployed application. Smoke checks are verified; all 12 Level 1
cases passed locally against the deployed application. Level 2 regression checks,
deployment, and all 14 local cases passed; combined cloud verification is pending. Level 3 is deferred
at the user's request; optional ReportPortal integration remains incomplete.

Initial local verification against the deployed application: **4 passed, 0 failed,
0 skipped**, reported on 3 November 2026. GitHub CI subsequently passed; AWS
CodeBuild build 1 and its report were confirmed successful on 4 November 2026.
The owner and other customer accounts signed in successfully, and the dedicated
waiter account's role and restaurant assignment were verified. Level 1 passed in
separate local runs: **8 API cases and 4 UI cases**, with no failures or skips.
The standalone UI login check also passed. GitHub Actions run `41745144764`
completed **16/16 cases** (4 Smoke + 12 Level1), verified from its downloaded TRX
artifact. The corresponding combined AWS CodeBuild run was confirmed successful
by the user. On 4 November 2026, the Level 2 deployment script completed and the user
reported **14 passed, 0 failed, 0 skipped** in 123.2 seconds: 13 cases against the
deployed app plus one isolated API contract. GitHub Actions and AWS CodeBuild still
need to verify the combined 30-case suite.

Cloud run `41751548828` completed 25/30 cases. Its remaining failures involved
HTTP 504 responses and UI requests that did not finish before their deadlines.
CloudWatch reports for the run showed the API Lambda at 128 MB, peak reported
memory usage of 125 MB, and a maximum invocation duration of 33.3 seconds. The
API-handler source configuration now specifies 512 MB to provide additional memory
and CPU. Apply that runtime setting before repeating cloud verification; whether
it resolves the failures remains to be verified by the new runs.

## Dedicated account setup

Register three distinct application accounts: the reservation owner, another customer,
and the future waiter. Reuse the owner's actual profile name; names are read from `/me`.
Fill their email/password fields in the private `.env`. Do not put credentials in chat
or commit them. From the repository root, run the read-only readiness check:

```bash
python3 aqa/restaurant-aqa-framework/scripts/prepare-accounts.py --check
```

It verifies sign-in and customer roles, lists restaurant locations, and reports the
waiter's existing role and location. It does not print passwords or tokens.
To promote only the configured waiter, explicitly choose an existing location:

```bash
python3 aqa/restaurant-aqa-framework/scripts/prepare-accounts.py \
  --promote-waiter --location-id <location-id>
```

This uses the application's `POST /waiters` setup operation to add the Cognito group
and location record, then signs in again to verify both. It preserves an existing
assignment and refuses to move a waiter to another location. Account setup is a local
administrative operation; it is not run automatically by test CI.

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
AQA_TEST_FILTER='Category=Level1&Layer=API' bash scripts/run-tests.sh
AQA_TEST_FILTER='Category=Level1&Layer=UI' bash scripts/run-tests.sh
AQA_TEST_FILTER='Category=Level1' bash scripts/run-tests.sh
AQA_TEST_FILTER='Category=Level2' bash scripts/run-tests.sh
AQA_TEST_FILTER='Category=Level2&Layer=API' bash scripts/run-tests.sh
AQA_TEST_FILTER='Category=Level2&Layer=UI' bash scripts/run-tests.sh
```

Smoke tests create no accounts, reservations, carts, or other persistent data.
Level 1 uses dedicated preexisting accounts and creates its own future reservations
and guest sessions. Each UI scenario has its own browser context. Separate test
classes can execute in parallel; no test depends on another test's data or order.

## Level 1: participants and totals

There are **12 Level 1 cases: eight API and four UI cases**. API setup creates each
reservation, obtains an invitation, creates guest sessions, and prepares baskets.
The UI tests sign in through the real login form and exercise the owner review page;
booking and guest basket preparation use the API. These cases do not claim to cover
the booking or guest-entry UI flows.

| Layer | Coverage |
| --- | --- |
| API | Confirmed, unconfirmed, and empty guest baskets; names, dishes, quantities, prices, statuses, participant totals, and group sum |
| API | Basket quantity changes update participant and group totals |
| API | Empty group returns `participants: []` and `total: 0` |
| API | Other customer and waiter receive 403; guest token and anonymous caller receive 401 on an existing populated group, without leaking basket data |
| API | Authenticated owner receives 404 for a nonexistent group |
| UI | Owner sees guest dishes, quantities, subtotals, and confirmed/pending labels |
| UI | Empty guest basket stays visible with its pending status |
| UI | Empty group shows zero participants and zero total |
| UI | Grand Total includes all participant baskets and updates after a guest changes a basket and the owner refreshes |

Prerequisites: fill all six dedicated-account fields in private `.env`, assign the
waiter a restaurant, and have an available dish with a positive price and a table
with capacity at least four at that restaurant. Fixture dates use UTC now plus
7–90 days, with random daytime offsets; reservations last 90 minutes. Setup searches
up to 20 distinct dates and every returned table, retrying only documented overlap
conflicts and logging empty-slot/conflict counts. Availability indicates a free slot
later in the day, so reservation creation still validates the exact requested time. Data names
include a unique run ID. Bounded polling waits for eventually consistent indexes;
reservation creation retries only explicit overlap conflicts.

Cleanup removes each created guest session and cancels the created reservation,
including when setup partially fails. Cancellation releases the table and removes
the reservation from the waiter's active list. The public API has no permanent
reservation deletion operation: cancelled reservation/group history remains. Unique
future slots and run IDs prevent subsequent runs from depending on that history.
Cleanup failures fail the test rather than silently leaving active reservations.

Two frontend changes were deployed before the successful Level 1 UI run:

- A missing optional owner basket (HTTP 404) no longer blocks review of a new group;
  other authorization and server errors still propagate.
- The owner page's Grand Total includes every participant basket, as the AQA task
  requires. The consolidated order and submission dialog's Order Total still count
  only confirmed baskets. This supersedes the earlier frontend exercise's
  submitted-only Grand Total display; no submission payload logic changed.

The .NET suite compiles and frontend TypeScript checks pass. Live Level 1 runs passed
**8/8 API** and **4/4 UI** cases, and the frontend regression suite passed **81** tests.
Cloud jobs default to Smoke until the dedicated account secrets are configured.

## Configure account secrets for cloud tests

From the repository root, use the private `.env` to configure this repository's six
Actions secrets. The helper sends values through stdin rather than command arguments
and prints only field names:

```bash
python3 aqa/restaurant-aqa-framework/scripts/configure-ci-accounts.py --github
```

Only after all secrets are saved does it set the repository's default filter to
`Category=Smoke|Category=Level1|Category=Level2` (**30 cases**). Enable this after
deploying the Level 2 prerequisites and verifying local live tests. Push the workflow and tests afterward.
Pull requests always run Smoke without the dedicated-account secrets. Manual Actions
runs can select a level or a combined suite from the dropdown.

For AWS, use the same helper with a different explicit destination:

```bash
python3 aqa/restaurant-aqa-framework/scripts/configure-ci-accounts.py --aws
```

This creates or updates `green-tasty-aqa/accounts` in Secrets Manager in `eu-north-1`,
using AWS profile `aws-toolkit`. A private temporary file is removed after use, and
only the secret ARN is recorded in ignored `aqa/aws/terraform.tfvars`. Terraform
never receives the credential values. The external secret uses the AWS-managed
Secrets Manager key and is not deleted by Terraform.

Plan and apply the `aqa/aws` configuration update. CodeBuild receives secret references,
permission to read this specific secret, and the combined 30-case filter. Commit and
push the changes before `bash aqa/aws/run-tests.sh`, since it archives committed code.
The combined GitHub report has been verified and the AWS run confirmed successful.

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
UI sign-in runs outside the retained trace to avoid saving password entry and login
request bodies. Authenticated traces can still contain session headers and must
be treated as sensitive.

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
The runner infrastructure is prepared in [`../aws`](../aws/README.md).
The GitHub connection was created and authorized, but AWS Organizations denied
CodeConnections access from CodeBuild and denied OIDC-provider discovery. The runner
therefore uses an S3 source archive submitted through the existing CLI login.
GitHub CI is automatic; AWS runs are started manually using `bash aqa/aws/run-tests.sh`
from the repository root. **The revised CodeBuild project is deployed, and its
first smoke build and report passed**. Follow the Terraform setup in `../aws`
to reproduce the deployment.

When provisioning the runner:

1. Use the selected project Region, `eu-north-1`, and CLI profile `aws-toolkit`.
2. Use the managed Ubuntu image `aws/codebuild/standard:7.0`, with the .NET 10 runtime
   and PowerShell. Use an EC2-backed Linux build environment for `on-failure: CONTINUE`.
3. Set the buildspec path to `aqa/restaurant-aqa-framework/buildspec.yml`.
   Set `AQA_REPORT_GROUP_ARN` to the precreated CodeBuild report group's ARN.
4. Use the S3 source configured by Terraform. The helper script uploads a committed
   source archive and pins each build to its exact S3 object version.
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

## Level 2: consolidated submission

There are **14 Level 2 cases**: 10 live HTTP API cases, three UI cases, and one
isolated API-handler contract case. All 14 passed locally after the backend and
frontend deployment. The combined cloud suite will contain **30 cases**
(4 Smoke + 12 Level1 + 14 Level2); the account-setup login check is excluded.

| Layer | Coverage |
| --- | --- |
| API | Confirmed owner quantity 1 and guest quantities 2 and 3 aggregate into quantity 6; draft quantity 7 is excluded; waiter sees the order and both statuses are verified |
| API | No confirmed cart, missing order, nonexistent reservation, nonexistent group: separate scenarios and expected messages |
| API | Other customer/waiter receive 403; guest token/anonymous caller receive 401; rejected requests preserve order/group states |
| API | Repeat submission returns 400 (`Group reservation was submitted.`) without duplicating dishes |
| UI | Owner sees their basket, confirms the submitted-only Order Total and sees Sent to Restaurant; assigned waiter sees the identical dish/quantity in a separate browser context |
| UI | Submit remains disabled without confirmed baskets; missing order produces a visible error |
| Isolated API contract | Confirmed but empty basket returns 400 (`Submitted guest sessions contain no dishes.`) without order/group writes |

The public API rejects confirming empty carts and editing confirmed carts, so the
empty-confirmed state is unreachable through normal requests. That single defensive
case runs the real handler/service with isolated repositories; **it is not a deployed
end-to-end test**. The AQA project references the backend for this contract. AWS source
archives include the tracked API-handler project as well as the framework.

This increment connects an owner's existing nonempty pre-order to a stable group
basket on owner-session lookup, preserving its initial draft/confirmed state.
Existing owner baskets are reused; no owner order retains the 404 contract.
Guest capacity excludes the stored owner session, since the owner place is already
reserved. Full invitation details remain readable; joining still enforces capacity.
The waiter page supports `?reservationId=...` and prioritizes that link over stale
session storage. These backend/frontend changes require deployment.

Fixtures use native order APIs for the owner's basket and have the configured waiter
create reservations for the owner when assigned-waiter verification is needed.
Cleanup removes tracked owner/guest sessions, cancels created orders, and cancels
reservations. Historical records and order notifications remain where no public
permanent-deletion API exists.

Run this pre-deployment regression batch from the repository root:

```bash
bash aqa/restaurant-aqa-framework/scripts/verify-level2.sh
```

It runs relevant backend regressions, frontend homework tests including the waiter
deep link, and the isolated contract. It creates no live reservations. After these
pass, deploy the backend/frontend, run `Category=Level2`, then enable and verify the
30-case cloud suite. Full test execution requires the user's terminal because the
agent session restricts test-runner sockets and outbound network access.

After pre-deployment checks pass, deploy this checkout to the existing AWS environment
and run all 14 Level 2 cases:

```bash
bash aqa/restaurant-aqa-framework/scripts/deploy-level2.sh
```

The script verifies the AWS project and Lambda runtime/architecture, builds both apps,
uploads a versioned backend package to the existing AQA artifact bucket, updates
`api-handler` using its revision ID, uploads the frontend, and waits for CloudFront
invalidation. Live tests create temporary reservations using the configured accounts.
Their reports are written to `TestResults/level2`. GitHub and CodeBuild verification
remain separate steps after this deployment passes.

## Homework progress

1. **Level 1 — complete:** create reservations and guest baskets; verify owner participant views,
   quantities, totals and updates, pending/empty carts, empty groups, missing groups,
   and restrictions for other customers, guests, waiters, and anonymous users.
2. **Level 2 — deployed, local checks passed, cloud verification pending:** consolidate only confirmed carts (including the owner's); verify dish
   aggregation, group/order status, waiter visibility, each rejection condition,
   repeated submission, and owner-only submission.
3. **Level 3 — deferred:** remove participants; verify totals, confirmed and last participants,
   cross-group HTTP 400, missing-participant HTTP 404, role restrictions, and the
   complete remove → submit → waiter workflow.
4. Use unique data, dynamic future dates, independent fixtures, and cleanup for all
   mutating tests. Document unspecified behavior from the actual implementation.
5. Publish the changes to GitHub, verify repository visibility, run GitHub CI and AWS
   CodeBuild, then optionally add ReportPortal for the starred task.
