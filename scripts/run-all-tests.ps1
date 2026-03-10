Write-Host "Stopping any running app processes..."
taskkill /IM Kor.Inspections.App.exe /F 2>$null

Write-Host "Building solution..."
dotnet build

Write-Host "Running unit tests..."
dotnet test Kor.Inspections.Tests --no-build

Write-Host "Starting app in background..."
$process = Start-Process dotnet -ArgumentList "run --project Kor.Inspections.App" -PassThru

Write-Host "Waiting for server..."

$maxAttempts = 20
$attempt = 0

while ($attempt -lt $maxAttempts) {
    try {
        $response = Invoke-WebRequest https://localhost:7074 -UseBasicParsing -TimeoutSec 2
        if ($response.StatusCode -eq 200) {
            break
        }
    } catch { }

    Start-Sleep -Seconds 1
    $attempt++
}

Write-Host "Running Playwright E2E tests..."
cd Kor.Inspections.App/tests/e2e
npx playwright test
