param(
    [Parameter(Mandatory = $true)][string]$SourceRoot,
    [Parameter(Mandatory = $true)][string]$Destination,
    [Parameter(Mandatory = $true)][ValidateSet('brx', 'zwcad')][string]$Platform
)

$ErrorActionPreference = 'Stop'
$SourceRoot = (Resolve-Path $SourceRoot).Path
if (Test-Path $Destination) { Remove-Item $Destination -Recurse -Force }
New-Item -ItemType Directory -Path $Destination | Out-Null

if ($Platform -eq 'brx') {
    $pairs = @(
        @('Autodesk.AutoCAD.ApplicationServices', 'Bricscad.ApplicationServices'),
        @('Autodesk.AutoCAD.DatabaseServices', 'Teigha.DatabaseServices'),
        @('Autodesk.AutoCAD.EditorInput', 'Bricscad.EditorInput'),
        @('Autodesk.AutoCAD.Geometry', 'Teigha.Geometry'),
        @('Autodesk.AutoCAD.Runtime', 'Teigha.Runtime'),
        @('Autodesk.AutoCAD.Colors', 'Teigha.Colors'),
        @('Autodesk.Windows', 'Bricscad.Windows')
    )
} else {
    $pairs = @(
        @('Autodesk.AutoCAD.ApplicationServices', 'ZwSoft.ZwCAD.ApplicationServices'),
        @('Autodesk.AutoCAD.DatabaseServices', 'ZwSoft.ZwCAD.DatabaseServices'),
        @('Autodesk.AutoCAD.EditorInput', 'ZwSoft.ZwCAD.EditorInput'),
        @('Autodesk.AutoCAD.Geometry', 'ZwSoft.ZwCAD.Geometry'),
        @('Autodesk.AutoCAD.Runtime', 'ZwSoft.ZwCAD.Runtime'),
        @('Autodesk.AutoCAD.Colors', 'ZwSoft.ZwCAD.Colors'),
        @('Autodesk.Windows', 'ZwSoft.Windows')
    )
}

Get-ChildItem -Path $SourceRoot -Filter *.cs -Recurse -File | Where-Object {
    $_.FullName -notmatch '\\(bin|obj)\\'
} | ForEach-Object {
    $rel = $_.FullName.Substring($SourceRoot.TrimEnd('\').Length).TrimStart('\')
    $out = Join-Path $Destination $rel
    $dir = Split-Path $out -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    $text = [System.IO.File]::ReadAllText($_.FullName)
    foreach ($pair in $pairs) { $text = $text.Replace($pair[0], $pair[1]) }
    [System.IO.File]::WriteAllText($out, $text)
}
