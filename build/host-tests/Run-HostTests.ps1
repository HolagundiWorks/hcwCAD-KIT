# Runs the hcwCAD-KIT command layer headlessly in AutoCAD's core console (accoreconsole.exe) and checks what it prints.
#
#   powershell -File build\host-tests\Run-HostTests.ps1 [-Dll <path to hcwCAD-KIT.dll>] [-Only <name fragment>]
#
# Each test is a script file in tests\*.scr. Header comment lines (a script comment starts with ;) say what to check:
#   ; TEMPLATE: acadiso.dwt          the drawing template to start from (acadiso = millimetres, acad = inches). Default acadiso.dwt
#   ; EXPECT: <regex>                 must match somewhere in the console output (several allowed)
#   ; REJECT: <regex>                 must NOT match anywhere in the console output
#   ; PREPARE: <repo file> => <name>  copy a repo file (for example a docs\bridge fixture) into the work folder before the run
#   ; FILE: <name> => <regex>         a file the run wrote into the work folder must exist and match
# {DLL} in a script is replaced with the path of the DLL under test, {WORK} with the work folder (a path with no spaces).
# The work folder is emptied of .json and .dwg files before every test. Output of every run is kept in the output folder.
#
# What this covers: loading, the prompts, and what the commands put in a drawing (counts per layer, messages). It does not cover the ribbon or
# dialogs (the core console has no UI), so the ribbon and the WinForms dialogs still need the interactive checklist in docs\TESTING.md.
# Agent 1 (local) owns this folder. A run is logged in docs\work\TEST-LOG.md as level L3 (automatic).
param(
    [string]$Dll = "",
    [string]$Only = "",
    [string]$AutoCADDir = "C:\Program Files\Autodesk\AutoCAD 2022",
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Resolve-Path (Join-Path $here "..\..")
if (-not $Dll) { $Dll = Join-Path $root "src\HCW.AutoCAD.Plugin\bin\x64\Release\hcwCAD-KIT.dll" }
$console = Join-Path $AutoCADDir "accoreconsole.exe"
if (-not (Test-Path $console)) { throw "accoreconsole.exe not found in $AutoCADDir" }
if (-not (Test-Path $Dll)) { throw "plugin DLL not found: $Dll (build it first)" }

# A short path with no spaces: the console's NETLOAD does not take a quoted path, and the DLL is copied so the build output is never locked.
$work = Join-Path $env:TEMP "hcwhost"
$out = Join-Path $work "out"
New-Item -ItemType Directory -Force $work, $out | Out-Null
$dllCopy = Join-Path $work "hcwCAD-KIT.dll"
Copy-Item $Dll $dllCopy -Force
$autodesk = Join-Path $env:LOCALAPPDATA "Autodesk"
$found = Get-ChildItem $autodesk -Recurse -Include acadiso.dwt, acad.dwt -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match "AutoCAD 2022" }
foreach ($t in $found) { Copy-Item $t.FullName (Join-Path $work $t.Name) -Force }
foreach ($need in "acadiso.dwt", "acad.dwt") { if (-not (Test-Path (Join-Path $work $need))) { throw "template $need not found under $autodesk (AutoCAD 2022 must have been started once)" } }

$results = @()
foreach ($file in Get-ChildItem (Join-Path $here "tests") -Filter *.scr | Sort-Object Name) {
    $name = [IO.Path]::GetFileNameWithoutExtension($file.Name)
    if ($Only -and $name -notlike "*$Only*") { continue }
    $text = Get-Content $file.FullName -Raw
    $template = "acadiso.dwt"
    $expect = @(); $reject = @(); $prepare = @(); $fileChecks = @()
    foreach ($line in ($text -split "`r?`n")) {
        if ($line -match '^\s*;\s*TEMPLATE:\s*(\S+)') { $template = $Matches[1] }
        elseif ($line -match '^\s*;\s*EXPECT:\s*(.+?)\s*$') { $expect += $Matches[1] }
        elseif ($line -match '^\s*;\s*REJECT:\s*(.+?)\s*$') { $reject += $Matches[1] }
        elseif ($line -match '^\s*;\s*PREPARE:\s*(\S+)\s*=>\s*(\S+)') { $prepare += ,@($Matches[1], $Matches[2]) }
        elseif ($line -match '^\s*;\s*FILE:\s*(\S+)\s*=>\s*(.+?)\s*$') { $fileChecks += ,@($Matches[1], $Matches[2]) }
    }
    # a clean work folder for every test: files the earlier tests wrote must not make this one pass or fail
    Get-ChildItem $work -File | Where-Object { $_.Extension -in ".json", ".dwg", ".bak", ".sv$" } | Remove-Item -Force -ErrorAction SilentlyContinue
    foreach ($pair in $prepare) { Copy-Item (Join-Path $root $pair[0]) (Join-Path $work $pair[1]) -Force }   # PREPARE: <repo-relative file> => <name in the work folder>
    $script = Join-Path $work "$name.scr"
    # the standard preamble: no file dialogs, load the plugin; and a QUIT at the end so the console exits
    # TrimEnd matters: a blank line at the Command prompt repeats the last command, so a script must not end with one before QUIT
    $body = "(setvar `"FILEDIA`" 0)`r`n(setvar `"SECURELOAD`" 0)`r`nNETLOAD`r`n$dllCopy`r`n" + $text.Replace("{DLL}", $dllCopy).Replace("{WORK}", $work).TrimEnd() + "`r`nQUIT`r`nY`r`n"
    Set-Content $script $body -Encoding ASCII
    $log = Join-Path $out "$name.out.txt"
    $p = Start-Process -FilePath $console -ArgumentList @("/i", (Join-Path $work $template), "/s", $script, "/l", "en-US") -RedirectStandardOutput $log -NoNewWindow -PassThru
    $timedOut = -not $p.WaitForExit($TimeoutSeconds * 1000)
    if ($timedOut) { try { $p.Kill() } catch { } }
    $output = (Get-Content $log -Encoding Unicode -ErrorAction SilentlyContinue) -join "`n"
    $failures = @()
    if ($timedOut) { $failures += "timed out after $TimeoutSeconds s (a prompt is waiting for input the script did not give)" }
    foreach ($e in $expect) { if ($output -notmatch $e) { $failures += "missing: $e" } }
    foreach ($r in $reject) { if ($output -match $r) { $failures += "found (must not): $r" } }
    foreach ($fc in $fileChecks) {
        $path = Join-Path $work $fc[0]
        if (-not (Test-Path $path)) { $failures += "file not written: $($fc[0])"; continue }
        if ((Get-Content $path -Raw) -notmatch $fc[1]) { $failures += "file $($fc[0]) is missing: $($fc[1])" }
    }
    $results += [pscustomobject]@{ Test = $name; Result = $(if ($failures.Count -eq 0) { "PASS" } else { "FAIL" }); Detail = ($failures -join "; ") }
    Write-Host ("{0,-4} {1}" -f $results[-1].Result, $name) -ForegroundColor $(if ($failures.Count -eq 0) { "Green" } else { "Red" })
    foreach ($f in $failures) { Write-Host "       $f" -ForegroundColor Red }
}

$pass = @($results | Where-Object Result -eq "PASS").Count
Write-Host ("`n{0} passed, {1} failed. Output: {2}" -f $pass, ($results.Count - $pass), $out)
$commit = & git -C $root rev-parse --short HEAD 2>$null
if ($commit) { Write-Host "commit: $commit; DLL: $Dll ($((Get-Item $Dll).Length) bytes, $((Get-Item $Dll).LastWriteTime))" }
if ($results.Count -eq 0) { exit 2 }
exit $(if ($pass -eq $results.Count) { 0 } else { 1 })
