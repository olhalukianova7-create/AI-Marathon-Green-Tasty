#!/usr/bin/env bash
set -euo pipefail

framework_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
repository_directory="$(git -C "$framework_directory" rev-parse --show-toplevel)"

echo 'Checking backend group-order and guest-session regressions...'
dotnet test "$repository_directory/backend/restaurant-backend-app/Tests/APIHandler.Tests.csproj" \
  --configuration Release \
  --filter 'FullyQualifiedName~GroupPreOrderServiceTests|FullyQualifiedName~GroupReservationServiceTests|FullyQualifiedName~GuestSessionServiceTests'

echo 'Checking frontend homework regressions...'
(
  cd "$repository_directory/frontend/restaurant-frontend-app"
  npm run test:hw -- --browsers=ChromeHeadless
)

echo 'Checking the isolated empty-confirmed-basket API contract...'
dotnet test "$framework_directory/Restaurant.Aqa.csproj" \
  --configuration Release --filter 'Category=Level2&Layer=APIContract' \
  --results-directory "$framework_directory/TestResults/contracts" \
  --logger 'trx;LogFileName=level2-contract.trx'

echo 'All Level 2 pre-deployment checks passed. No live reservations were created.'
