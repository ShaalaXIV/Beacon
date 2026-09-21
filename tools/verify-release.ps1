[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $tracked = @(& git ls-files)
    $unsafeTracked = @($tracked | Where-Object {
        $_ -match '(^|/)var/' -or $_ -match '\.db(?:-wal|-shm)?$'
    })
    if ($unsafeTracked.Count -gt 0) {
        throw "Runtime data is tracked by Git:`n$($unsafeTracked -join "`n")"
    }

    $configurationPath = 'src/Compass.Plugin/Services/Configuration.cs'
    $configuration = Get-Content -LiteralPath $configurationPath -Raw
    if ($configuration -match 'ServerUrl\s*\{[^}]*\}\s*=\s*"(http://|[^\"]*localhost)') {
        throw 'The distributed plugin still points at an insecure or local server URL.'
    }
    if ($configuration -notmatch 'https://plugins\.aethercast\.org:61249') {
        throw 'The distributed plugin URL is not the production Compass endpoint.'
    }

    $pluginProject = Get-Content -LiteralPath 'src/Compass.Plugin/Compass.Plugin.csproj' -Raw
    if ($pluginProject -match '<IconUrl>\s*</IconUrl>' -or $pluginProject -match '<RepoUrl>\s*</RepoUrl>') {
        throw 'Plugin release metadata is incomplete.'
    }

    $stack = Get-Content -LiteralPath 'deploy/portainer-stack.yml' -Raw
    foreach ($required in @(
        'restart: always',
        'Compass__DataDirectory: /var/lib/compass',
        '/opt/portainer/compass/data:/var/lib/compass',
        'read_only: true',
        '61249:443'
    )) {
        if ($stack.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
            throw "Production stack invariant is missing: $required"
        }
    }
    if ($stack -match 'ASPNETCORE_ENVIRONMENT:\s*Development') {
        throw 'The production stack enables the Development environment.'
    }

    if (-not $AllowDirty) {
        $dirty = @(& git status --porcelain)
        if ($dirty.Count -gt 0) {
            throw "The release tree is not clean:`n$($dirty -join "`n")"
        }
    }

    if (-not $SkipBuild) {
        & dotnet restore Compass.slnx --locked-mode --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }

        & dotnet build Compass.slnx -c Release --no-restore --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

        & (Join-Path $PSScriptRoot 'smoke-server.ps1')

        $manifestPath = 'src/Compass.Plugin/bin/Release/Compass.json'
        $packagePath = 'src/Compass.Plugin/bin/Release/Compass/latest.zip'
        if (-not (Test-Path -LiteralPath $manifestPath) -or -not (Test-Path -LiteralPath $packagePath)) {
            throw 'Dalamud release manifest or latest.zip was not produced.'
        }

        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ($manifest.RepoUrl -ne 'https://github.com/ShaalaXIV/Compass') {
            throw 'Generated manifest has the wrong repository URL.'
        }
        if ($manifest.IconUrl -ne 'https://plugins.aethercast.org/images/compass.png') {
            throw 'Generated manifest has the wrong icon URL.'
        }

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $packagePath).Path)
        try {
            $names = @($archive.Entries | ForEach-Object FullName)
            foreach ($requiredName in @('Compass.dll', 'Compass.json')) {
                if ($requiredName -notin $names) {
                    throw "Plugin package is missing $requiredName."
                }
            }
            $forbiddenNames = @($names | Where-Object {
                $_ -match '\.pdb$' -or
                $_ -match '(^|/)appsettings\.Development\.json$' -or
                $_ -match '(^|/)(test|tests|testing|mock|mocks)(/|$)'
            })
            if ($forbiddenNames.Count -gt 0) {
                throw "Plugin package contains development or test-only files:`n$($forbiddenNames -join "`n")"
            }
        }
        finally {
            $archive.Dispose()
        }
    }

    Write-Host 'Compass release safety checks passed.'
}
finally {
    Pop-Location
}
