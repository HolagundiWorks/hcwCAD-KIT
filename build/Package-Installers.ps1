# Builds hcwCAD-KIT for AutoCAD, BricsCAD, and ZWCAD, then compiles
# a separate per-user setup program for each host.
param(
    [string]$Only = "",
    [string]$Configuration = "Release",
    [string]$AutoCADInstallDir = "C:\Program Files\Autodesk\AutoCAD 2022\",
    [string]$BricscadInstallDir = "C:\Program Files\Bricsys\BricsCAD V26 en_US\",
    [string]$ZwcadInstallDir = "C:\Program Files\ZWSOFT\ZWCAD 2026\"
)

$ErrorActionPreference = "Stop"

function Invoke-Native([string]$FilePath, [string[]]$ArgumentList) {
    $line = ($ArgumentList | ForEach-Object {
        $arg = $_
        if ($arg -match '[\s"]') {
            $arg = $arg.Replace('"', '\"')
            if ($arg -match '\\+$') { $arg = $arg -replace '\\+$', '$0$0' }
            '"' + $arg + '"'
        } else { $arg }
    }) -join " "
    $proc = Start-Process -FilePath $FilePath -ArgumentList $line -Wait -NoNewWindow -PassThru
    return $proc.ExitCode
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$dist = Join-Path $root "dist"
$stage = Join-Path $dist "stage"
$iss = Join-Path $root "installer\hcwCAD-KIT.iss"

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 is not installed. Install JRSoftware.InnoSetup, then run this script again." }

New-Item -ItemType Directory -Path $dist -Force | Out-Null

$targets = @(
    @{
        Name = "AutoCAD"
        Project = "src\HCW.AutoCAD.Plugin\HCW.AutoCAD.Plugin.csproj"
        Bundle = "src\HCW.AutoCAD.Plugin.bundle\PackageContents.xml"
        Props = @("/p:AutoCADInstallDir=$AutoCADInstallDir")
        AppId = "6F2C9E2A-6B3E-4A0E-9D55-9A2B6A2F5C31"
        PluginsRoot = "Autodesk\ApplicationPlugins"
    },
    @{
        Name = "BricsCAD"
        Project = "src\HCW.BricsCAD.Plugin\HCW.BricsCAD.Plugin.csproj"
        Bundle = "src\HCW.BricsCAD.Plugin.bundle\PackageContents.xml"
        Props = @("/p:BricscadInstallDir=$BricscadInstallDir")
        AppId = "B7C8D9E0-1234-4F56-9ABC-DEF012345678"
        PluginsRoot = "Bricsys\ApplicationPlugins"
    },
    @{
        Name = "ZWCAD"
        Project = "src\HCW.ZWCAD.Plugin\HCW.ZWCAD.Plugin.csproj"
        Bundle = "src\HCW.ZWCAD.Plugin.bundle\PackageContents.xml"
        Props = @("/p:ZwcadInstallDir=$ZwcadInstallDir")
        AppId = "C8D9E0F1-2345-4A67-8BCD-EF0123456789"
        PluginsRoot = "ZWSOFT\ApplicationPlugins"
    }
)

if ($Only) {
    $targets = @($targets | Where-Object { $_.Name -eq $Only })
    if ($targets.Count -eq 0) { throw "Unknown host '$Only'. Use AutoCAD, BricsCAD, or ZWCAD." }
}

Push-Location $root
try {
    foreach ($target in $targets) {
        $out = Join-Path $stage $target.Name
        if (Test-Path $out) { Remove-Item $out -Recurse -Force }
        $contents = Join-Path $out "Contents"
        New-Item -ItemType Directory -Path $contents -Force | Out-Null
        Write-Host "Building $($target.Name)..."
        $buildArgs = @("build", $target.Project, "-c", $Configuration, "-p:Platform=x64", "-o", $contents, "--nologo") + @($target.Props)
        if ((Invoke-Native "dotnet" $buildArgs) -ne 0) { throw "$($target.Name) build failed." }
        Copy-Item (Join-Path $root $target.Bundle) (Join-Path $out "PackageContents.xml")
        Get-ChildItem $contents -File | Where-Object { $_.Extension -ne ".dll" } | Remove-Item -Force

        Write-Host "Packing $($target.Name) setup..."
        $packArgs = @(
            "/DHost=$($target.Name)",
            "/DAppId=$($target.AppId)",
            "/DPluginsRoot=$($target.PluginsRoot)",
            "/DStageDir=$out",
            $iss
        )
        if ((Invoke-Native $iscc $packArgs) -ne 0) { throw "$($target.Name) installer compile failed." }

        # The same bundle as a plain zip, for anyone whose security software blocks the setup program:
        # unzip it into the host's ApplicationPlugins folder as hcwCAD-KIT.bundle.
        $zip = Join-Path $dist "hcwCAD-KIT-$($target.Name)-1.0.0-bundle.zip"
        if (Test-Path $zip) { Remove-Item $zip -Force }
        Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip
    }
}
finally {
    Pop-Location
}

# SHA-256 of every file that was built, so a download can be checked against the build.
$files = @(Get-ChildItem (Join-Path $dist "hcwCAD-KIT-*") -File | Where-Object { $_.Extension -in ".exe", ".zip" })
$lines = $files | ForEach-Object { "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower())  $($_.Name)" }
Set-Content -Path (Join-Path $dist "SHA256SUMS.txt") -Value $lines -Encoding ASCII

Write-Host ""
$files | ForEach-Object { Write-Host $_.FullName }
Write-Host (Join-Path $dist "SHA256SUMS.txt")
