#Requires -Version 7
<#
.SYNOPSIS
    Deploys a published B-number build of the Bookings site to KOR-APP01.

.DESCRIPTION
    1. Copies B<n>.zip (latest by default) from the local publish folder to the server.
    2. Backs up the live site folder to C:\inetpub\_backup (keeps the newest $KeepBackups).
    3. Stops the IIS site and app pool.
    4. Deletes the live folder contents, preserving .well-known (win-acme cert renewal) and logs.
    5. Unzips the build into the site folder and deletes the zip.
    6. Starts the app pool and site, then health-checks the public URL and hash-verifies the files.
    If anything fails after the delete, the backup is restored automatically.

    Build the zip first per the publish runbook (dotnet publish the csproj into B<n>, then Compress-Archive).

.EXAMPLE
    ./scripts/deploy-bookings.ps1                 # deploy the highest-numbered B*.zip
    ./scripts/deploy-bookings.ps1 -Build B55      # deploy a specific build
    ./scripts/deploy-bookings.ps1 -WhatIf         # show what would happen
    ./scripts/deploy-bookings.ps1 -Rollback       # restore the most recent backup
    ./scripts/deploy-bookings.ps1 -HealthOnly     # just run the health check
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [string]$Build,
    [switch]$Rollback,
    [switch]$HealthOnly,
    [string]$PublishRoot = 'C:\VIsual Studio Projects\_Publish\Inspections Site',
    [string]$Server = 'KOR-APP01',
    [string]$SiteName = 'bookings.korstructural.com',
    [string]$AppPool = 'booking.korstructural.com',
    [string]$SitePath = 'C:\inetpub\bookings.korstructural.com',
    [string]$BackupRoot = 'C:\inetpub\_backup',
    [string]$StagingRoot = 'C:\inetpub\_deploy',
    [string]$PublicUrl = 'https://bookings.korstructural.com',
    [int]$KeepBackups = 3
)

$ErrorActionPreference = 'Stop'
$Preserve = @('.well-known', 'logs')

function Write-Step([string]$Message) { Write-Host "==> $Message" -ForegroundColor Cyan }

function Test-SiteHealth {
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(20)
    try {
        $deadline = (Get-Date).AddSeconds(90)
        $lastError = $null
        while ((Get-Date) -lt $deadline) {
            try {
                $homePage = $client.GetAsync("$PublicUrl/").GetAwaiter().GetResult()
                $homeBody = $homePage.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                $health = $client.GetAsync("$PublicUrl/healthz").GetAwaiter().GetResult()
                $homeOk = [int]$homePage.StatusCode -eq 200 -and $homeBody -match 'Book a Field Review'
                # /healthz requires AAD, so a redirect to Microsoft login proves the app and auth pipeline started.
                $healthOk = [int]$health.StatusCode -eq 302 -and "$($health.Headers.Location)" -like 'https://login.microsoftonline.com/*'
                if ($homeOk -and $healthOk) {
                    Write-Host "    Home page: 200 OK; /healthz: 302 -> Microsoft login" -ForegroundColor Green
                    return $true
                }
                $lastError = "home=$([int]$homePage.StatusCode) healthz=$([int]$health.StatusCode)"
            }
            catch { $lastError = $_.Exception.GetBaseException().Message }
            Start-Sleep -Seconds 5
        }
        Write-Host "    Health check failed: $lastError" -ForegroundColor Red
        return $false
    }
    finally { $client.Dispose() }
}

if ($HealthOnly) {
    Write-Step "Health-checking $PublicUrl"
    if (-not (Test-SiteHealth)) { exit 1 }
    return
}

$session = New-PSSession -ComputerName $Server
try {
    # Shared remote helpers: stop/start IIS, restore from a backup folder.
    Invoke-Command -Session $session -ArgumentList $SiteName, $AppPool, $SitePath, $Preserve -ScriptBlock {
        param($SiteName, $AppPool, $SitePath, $Preserve)
        Import-Module WebAdministration
        $global:SiteName = $SiteName; $global:AppPool = $AppPool; $global:SitePath = $SitePath; $global:Preserve = $Preserve

        function Stop-BookingsSite {
            if ((Get-WebsiteState -Name $SiteName).Value -ne 'Stopped') { Stop-Website -Name $SiteName }
            if ((Get-WebAppPoolState -Name $AppPool).Value -ne 'Stopped') { Stop-WebAppPool -Name $AppPool }
            $deadline = (Get-Date).AddSeconds(60)
            while ((Get-WebAppPoolState -Name $AppPool).Value -ne 'Stopped') {
                if ((Get-Date) -gt $deadline) { throw "App pool $AppPool did not stop within 60s" }
                Start-Sleep -Seconds 1
            }
            Start-Sleep -Seconds 2   # let w3wp release file handles
        }

        function Start-BookingsSite {
            if ((Get-WebAppPoolState -Name $AppPool).Value -ne 'Started') { Start-WebAppPool -Name $AppPool }
            if ((Get-WebsiteState -Name $SiteName).Value -ne 'Started') { Start-Website -Name $SiteName }
        }

        function Clear-SiteFolder {
            Get-ChildItem -LiteralPath $SitePath -Force |
                Where-Object { $Preserve -notcontains $_.Name } |
                Remove-Item -Recurse -Force
        }

        function Restore-SiteFolder([string]$BackupPath) {
            Clear-SiteFolder
            Get-ChildItem -LiteralPath $BackupPath -Force |
                Where-Object { $Preserve -notcontains $_.Name } |
                Copy-Item -Destination $SitePath -Recurse -Force
        }
    }

    if ($Rollback) {
        $backup = Invoke-Command -Session $session -ArgumentList $BackupRoot -ScriptBlock {
            param($BackupRoot)
            Get-ChildItem -LiteralPath $BackupRoot -Directory -Filter 'bookings-*' |
                Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty FullName
        }
        if (-not $backup) { throw "No bookings-* backup found in $BackupRoot on $Server" }
        if (-not $PSCmdlet.ShouldProcess("$SiteName on $Server", "Roll back to $backup")) { return }

        Write-Step "Rolling back to $backup"
        Invoke-Command -Session $session -ArgumentList $backup -ScriptBlock {
            param($backup)
            Stop-BookingsSite
            Restore-SiteFolder $backup
            Start-BookingsSite
        }
        Write-Step "Health-checking $PublicUrl"
        if (-not (Test-SiteHealth)) { exit 1 }
        Write-Host "Rollback complete." -ForegroundColor Green
        return
    }

    # Resolve the build to deploy.
    if ($Build) {
        $zip = Get-Item -LiteralPath (Join-Path $PublishRoot "$Build.zip")
    }
    else {
        $zip = Get-ChildItem -LiteralPath $PublishRoot -Filter 'B*.zip' |
            Where-Object { $_.BaseName -match '^B\d+$' } |
            Sort-Object { [int]$_.BaseName.Substring(1) } -Descending |
            Select-Object -First 1
        if (-not $zip) { throw "No B<n>.zip found in $PublishRoot" }
    }
    $buildName = $zip.BaseName
    $localBuildFolder = Join-Path $PublishRoot $buildName

    if (-not $PSCmdlet.ShouldProcess("$SiteName on $Server", "Deploy $buildName ($($zip.LastWriteTime))")) { return }

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $remoteZip = Join-Path $StagingRoot "$buildName.zip"
    $remoteStaging = Join-Path $StagingRoot "$buildName-$stamp"
    $backupPath = Join-Path $BackupRoot "bookings-$stamp"

    Write-Step "Copying $buildName.zip to $Server"
    Invoke-Command -Session $session -ArgumentList $StagingRoot -ScriptBlock {
        param($StagingRoot)
        New-Item -ItemType Directory -Force -Path $StagingRoot | Out-Null
    }
    Copy-Item -LiteralPath $zip.FullName -Destination $remoteZip -ToSession $session -Force
    $localHash = (Get-FileHash -LiteralPath $zip.FullName).Hash
    $remoteHash = Invoke-Command -Session $session -ArgumentList $remoteZip -ScriptBlock { param($p) (Get-FileHash -LiteralPath $p).Hash }
    if ($localHash -ne $remoteHash) { throw "Zip hash mismatch after copy" }

    Write-Step "Extracting to staging"
    Invoke-Command -Session $session -ArgumentList $remoteZip, $remoteStaging -ScriptBlock {
        param($remoteZip, $remoteStaging)
        Expand-Archive -LiteralPath $remoteZip -DestinationPath $remoteStaging -Force
        # Zips are built with the B<n> folder as the root; descend into it if so.
        $root = $remoteStaging
        $children = Get-ChildItem -LiteralPath $root -Force
        if ($children.Count -eq 1 -and $children[0].PSIsContainer) { $root = $children[0].FullName }
        if (-not (Test-Path (Join-Path $root 'Kor.Inspections.App.dll'))) { throw "Kor.Inspections.App.dll not found in extracted build" }
        $global:BuildRoot = $root
    }

    Write-Step "Backing up live site to $backupPath"
    Invoke-Command -Session $session -ArgumentList $backupPath, $BackupRoot, $KeepBackups -ScriptBlock {
        param($backupPath, $BackupRoot, $KeepBackups)
        New-Item -ItemType Directory -Force -Path $backupPath | Out-Null
        Get-ChildItem -LiteralPath $SitePath -Force |
            Where-Object { $Preserve -notcontains $_.Name } |
            Copy-Item -Destination $backupPath -Recurse -Force
        Get-ChildItem -LiteralPath $BackupRoot -Directory -Filter 'bookings-*' |
            Sort-Object Name -Descending | Select-Object -Skip $KeepBackups |
            Remove-Item -Recurse -Force
    }

    Write-Step "Stopping site and app pool; replacing files"
    $deployError = Invoke-Command -Session $session -ArgumentList $backupPath -ScriptBlock {
        param($backupPath)
        try {
            Stop-BookingsSite
            Clear-SiteFolder
            Get-ChildItem -LiteralPath $BuildRoot -Force | Copy-Item -Destination $SitePath -Recurse -Force
            $null
        }
        catch {
            $message = $_.Exception.Message
            Restore-SiteFolder $backupPath
            "File replacement failed and the backup was restored: $message"
        }
        finally {
            Start-BookingsSite
        }
    }
    if ($deployError) { throw $deployError }

    Write-Step "Cleaning up zip and staging"
    Invoke-Command -Session $session -ArgumentList $remoteZip, $remoteStaging -ScriptBlock {
        param($remoteZip, $remoteStaging)
        Remove-Item -LiteralPath $remoteZip -Force
        Remove-Item -LiteralPath $remoteStaging -Recurse -Force
    }

    Write-Step "Health-checking $PublicUrl"
    $healthy = Test-SiteHealth

    if (Test-Path -LiteralPath $localBuildFolder) {
        Write-Step "Verifying live files against $localBuildFolder"
        $localHashes = Get-ChildItem -LiteralPath $localBuildFolder -Recurse -File | ForEach-Object {
            [pscustomobject]@{ Rel = $_.FullName.Substring($localBuildFolder.Length + 1); Hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm MD5).Hash }
        }
        $liveHashes = Invoke-Command -Session $session -ArgumentList $SitePath, $Preserve -ScriptBlock {
            param($SitePath, $Preserve)
            Get-ChildItem -LiteralPath $SitePath -Recurse -File | ForEach-Object {
                $rel = $_.FullName.Substring($SitePath.Length + 1)
                if ($Preserve -notcontains $rel.Split('\')[0]) {
                    [pscustomobject]@{ Rel = $rel; Hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm MD5).Hash }
                }
            }
        } | Select-Object Rel, Hash
        $diff = Compare-Object $localHashes $liveHashes -Property Rel, Hash
        if ($diff) {
            Write-Host "    $(@($diff).Count) file difference(s):" -ForegroundColor Red
            $diff | Format-Table -AutoSize | Out-String | Write-Host
            $healthy = $false
        }
        else {
            Write-Host "    All $(@($localHashes).Count) files match" -ForegroundColor Green
        }
    }
    else {
        Write-Host "    Skipped: local folder $localBuildFolder not found" -ForegroundColor Yellow
    }

    if (-not $healthy) {
        Write-Host "Deploy of $buildName finished but verification FAILED. Roll back with: ./scripts/deploy-bookings.ps1 -Rollback" -ForegroundColor Red
        exit 1
    }
    Write-Host "Deployed $buildName to $SiteName. Backup: $backupPath" -ForegroundColor Green
}
finally {
    Remove-PSSession $session -WhatIf:$false
}
