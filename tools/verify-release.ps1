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

    $configurationPath = 'src/Beacon.Plugin/Services/Configuration.cs'
    $configuration = Get-Content -LiteralPath $configurationPath -Raw
    if ($configuration -match 'ServerUrl\s*\{[^}]*\}\s*=\s*"(http://|[^\"]*localhost)') {
        throw 'The distributed plugin still points at an insecure or local server URL.'
    }
    if ($configuration -notmatch 'https://plugins\.aethercast\.org:61249') {
        throw 'The distributed plugin URL is not the production Beacon endpoint.'
    }

    $pluginProject = Get-Content -LiteralPath 'src/Beacon.Plugin/Beacon.Plugin.csproj' -Raw
    if ($pluginProject -match '<IconUrl>\s*</IconUrl>' -or $pluginProject -match '<RepoUrl>\s*</RepoUrl>') {
        throw 'Plugin release metadata is incomplete.'
    }

    $profileEditor = Get-Content -LiteralPath 'src/Beacon.Plugin/Windows/ProfileEditorWindow.cs' -Raw
    if ($profileEditor -match 'CapturePortraitAsync' -or $profileEditor -match 'screenshots\.CaptureAsync\(') {
        throw 'The profile editor must upload chosen files instead of capturing the viewport.'
    }
    foreach ($required in @(
        'CreateFromImageAsync',
        'CropToPngAsync',
        'PortraitOutputWidth = 600',
        'PortraitOutputHeight = 720',
        'PortraitSourceMaxBytes = 25 * 1024 * 1024',
        'draw.AddImage(texture.Handle, origin, box, uv0, uv1)'
    )) {
        if ($profileEditor.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
            throw "The profile preview-and-crop workflow is incomplete: $required"
        }
    }

    $screenshotService = Get-Content -LiteralPath 'src/Beacon.Plugin/Services/ScreenshotService.cs' -Raw
    foreach ($required in @('CreateFromExistingTextureAsync', 'Uv0 = uv0', 'Uv1 = uv1', 'NewWidth = width', 'NewHeight = height')) {
        if ($screenshotService.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
            throw "The portrait crop encoder is incomplete: $required"
        }
    }

    $profileService = Get-Content -LiteralPath 'src/Beacon.Plugin/Services/ProfileService.cs' -Raw
    foreach ($required in @('previousImages', 'UpdateGalleryAsync', 'PortraitImageId = newImage.ImageId')) {
        if ($profileService.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
            throw "Uploaded portrait replacement is incomplete: $required"
        }
    }

    $dialogDrawSites = @(Get-ChildItem -LiteralPath 'src/Beacon.Plugin' -Recurse -Filter '*.cs' |
        Select-String -Pattern 'screenshots\.Draw\(\);')
    if ($dialogDrawSites.Count -ne 1) {
        throw "The shared file dialog must be drawn exactly once after all plugin windows; found $($dialogDrawSites.Count) draw sites."
    }

    $iconPath = 'images/icon.png'
    if (-not (Test-Path -LiteralPath $iconPath)) {
        throw 'The repository is missing images/icon.png.'
    }
    $iconBytes = [System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $iconPath).Path)
    $pngSignature = [byte[]](0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a)
    $signatureDifference = @(Compare-Object $pngSignature $iconBytes[0..7])
    if ($iconBytes.Length -lt 24 -or $signatureDifference.Count -ne 0) {
        throw 'The Dalamud icon must be a valid PNG file.'
    }
    $iconWidth = [uint32]($iconBytes[16] * 16777216 + $iconBytes[17] * 65536 + $iconBytes[18] * 256 + $iconBytes[19])
    $iconHeight = [uint32]($iconBytes[20] * 16777216 + $iconBytes[21] * 65536 + $iconBytes[22] * 256 + $iconBytes[23])
    if ($iconWidth -ne $iconHeight -or $iconWidth -lt 64 -or $iconWidth -gt 512) {
        throw "The Dalamud icon must be square and between 64x64 and 512x512; found ${iconWidth}x${iconHeight}."
    }

    $stack = Get-Content -LiteralPath 'deploy/portainer-stack.yml' -Raw
    foreach ($required in @(
        'restart: always',
        'Beacon__DataDirectory: /var/lib/beacon',
        '/opt/portainer/beacon/data:/var/lib/beacon',
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

    $backupScript = Get-Content -LiteralPath 'deploy/backup.sh' -Raw
    foreach ($required in @(
        'PRAGMA integrity_check;',
        '__EFMigrationsHistory',
        '[ -s "$work/beacon.db" ]'
    )) {
        if ($backupScript.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
            throw "Backup recovery invariant is missing: $required"
        }
    }

    if (-not $AllowDirty) {
        $dirty = @(& git status --porcelain)
        if ($dirty.Count -gt 0) {
            throw "The release tree is not clean:`n$($dirty -join "`n")"
        }
    }

    if (-not $SkipBuild) {
        & dotnet restore Beacon.slnx --locked-mode --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }

        & dotnet build Beacon.slnx -c Release --no-restore --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

        & (Join-Path $PSScriptRoot 'smoke-server.ps1')

        $manifestPath = 'src/Beacon.Plugin/bin/Release/Beacon.json'
        $packagePath = 'src/Beacon.Plugin/bin/Release/Beacon/latest.zip'
        if (-not (Test-Path -LiteralPath $manifestPath) -or -not (Test-Path -LiteralPath $packagePath)) {
            throw 'Dalamud release manifest or latest.zip was not produced.'
        }

        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ($manifest.RepoUrl -ne 'https://github.com/ShaalaXIV/Beacon') {
            throw 'Generated manifest has the wrong repository URL.'
        }
        if ($manifest.IconUrl -ne 'https://plugins.aethercast.org/images/beacon-icon.png') {
            throw 'Generated manifest has the wrong icon URL.'
        }

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $packagePath).Path)
        try {
            $names = @($archive.Entries | ForEach-Object FullName)
            $expectedNames = @('Beacon.deps.json', 'Beacon.dll', 'Beacon.json', 'Beacon.Shared.dll')
            $packageDifference = @(Compare-Object -ReferenceObject $expectedNames -DifferenceObject $names)
            if ($packageDifference.Count -gt 0) {
                throw "Plugin package must contain exactly the four production files:`n$($names -join "`n")"
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

    Write-Host 'Beacon release safety checks passed.'
}
finally {
    Pop-Location
}
