[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ("compass-smoke-" + [guid]::NewGuid().ToString('N'))
$published = Join-Path $scratch 'server'
$data = Join-Path $scratch 'data'
$process = $null

function Get-FreeLoopbackPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try {
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function Start-CompassServer([int]$Port) {
    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = 'dotnet'
    $start.ArgumentList.Add((Join-Path $published 'Compass.Server.dll'))
    $start.WorkingDirectory = $published
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $null = $start.Environment.Remove('ASPNETCORE_ENVIRONMENT')
    $null = $start.Environment.Remove('ASPNETCORE_URLS')
    $null = $start.Environment.Remove('Compass__DataDirectory')
    $null = $start.Environment.Remove('Compass__RegistrationsPerHour')
    $start.Environment.Add('ASPNETCORE_ENVIRONMENT', 'Production')
    $start.Environment.Add('ASPNETCORE_URLS', "http://127.0.0.1:$Port")
    $start.Environment.Add('Compass__DataDirectory', $data)
    $start.Environment.Add('Compass__RegistrationsPerHour', '500')
    $start.Environment.Add('Logging__EventLog__LogLevel__Default', 'None')
    return [System.Diagnostics.Process]::Start($start)
}

function Stop-CompassServer($ServerProcess) {
    if ($null -eq $ServerProcess -or $ServerProcess.HasExited) {
        return
    }

    $ServerProcess.Kill($true)
    $ServerProcess.WaitForExit()
    $ServerProcess.Dispose()
}

function Wait-ForHealth([System.Net.Http.HttpClient]$Client, [string]$BaseUrl) {
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        try {
            $response = $Client.GetAsync("$BaseUrl/health").GetAwaiter().GetResult()
            if ($response.IsSuccessStatusCode) {
                $health = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
                if ($health.status -eq 'ok' -and $health.protocol -eq 1) {
                    return
                }
            }
        }
        catch {
            # Startup is still in progress.
        }

        Start-Sleep -Milliseconds 250
    }

    throw 'Compass did not become healthy within 15 seconds.'
}

try {
    New-Item -ItemType Directory -Path $scratch, $published, $data -Force | Out-Null

    & dotnet publish (Join-Path $repoRoot 'src/Compass.Server/Compass.Server.csproj') `
        -c Release --no-restore --no-self-contained -p:UseAppHost=false -o $published --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Server publish failed.' }

    if (Test-Path (Join-Path $published 'appsettings.Development.json')) {
        throw 'Development settings leaked into the production publish output.'
    }

    $port = Get-FreeLoopbackPort
    $baseUrl = "http://127.0.0.1:$port"
    $client = [System.Net.Http.HttpClient]::new()
    $client.Timeout = [TimeSpan]::FromSeconds(1)

    $process = Start-CompassServer $port
    Wait-ForHealth $client $baseUrl

    $withoutProtocol = $client.GetAsync("$baseUrl/api/beacons").GetAwaiter().GetResult()
    if ([int]$withoutProtocol.StatusCode -ne 426) {
        throw "Protocol guard returned $([int]$withoutProtocol.StatusCode), expected 426."
    }

    $client.DefaultRequestHeaders.Add('X-Compass-Protocol', '1')
    $registrationBody = [System.Net.Http.StringContent]::new(
        '{"displayName":"Release Smoke"}',
        [System.Text.Encoding]::UTF8,
        'application/json')
    $registration = $client.PostAsync("$baseUrl/api/accounts/register", $registrationBody).GetAwaiter().GetResult()
    if (-not $registration.IsSuccessStatusCode) {
        throw "Registration failed with HTTP $([int]$registration.StatusCode)."
    }

    $payload = $registration.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($payload.secretKey) -or [string]::IsNullOrWhiteSpace($payload.account.id)) {
        throw 'Registration response did not contain an account id and secret key.'
    }

    $client.DefaultRequestHeaders.Add('X-Compass-Key', [string]$payload.secretKey)
    $me = $client.GetAsync("$baseUrl/api/accounts/me").GetAwaiter().GetResult()
    if (-not $me.IsSuccessStatusCode) {
        throw "Authenticated account lookup failed with HTTP $([int]$me.StatusCode)."
    }

    Stop-CompassServer $process
    $process = Start-CompassServer $port
    Wait-ForHealth $client $baseUrl

    $afterRestart = $client.GetAsync("$baseUrl/api/accounts/me").GetAwaiter().GetResult()
    if (-not $afterRestart.IsSuccessStatusCode) {
        throw 'Account data did not survive a full server restart.'
    }

    Write-Host 'Compass server contract, protocol guard, and restart persistence smoke test passed.'
}
finally {
    Stop-CompassServer $process
    if (Test-Path -LiteralPath $scratch) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
}
