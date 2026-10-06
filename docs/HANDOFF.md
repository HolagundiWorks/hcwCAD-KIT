# Handoff

State of the work on 2026-10-06, for whoever picks it up next. Read [ROADMAP.md](ROADMAP.md) for the list of known limits; this file covers what was verified, what was not, and what to do first.

## Verified

- `main` builds clean (0 warnings, 0 errors) against an AutoCAD 2022 install.
- `dotnet test tests/HCW.Logic.Tests`: 512 passed, 0 failed, no analyzer warnings.
- The xUnit analyzer fixes (xUnit2012, xUnit2017, xUnit2029, xUnit2031) in `LogicTests.cs` and `DraftingTests.cs` are in this branch.

## Not verified

- Nothing has been loaded into AutoCAD, BricsCAD or ZWCAD. The command layer (prompts, entity creation, live update events, jigs, ribbon) has only been compiled, never run. This includes the newest commands: `SHEETFIT`, the wall, opening and axis grid tools, `HCWCLEAN`, `HCWCORNER`, `HCWAUDIT`, `HCWROOMSET`, `HCWWALLHATCH`, lintels, the column schedule, `HCWSECTIONDRAW`, `HCWOPENCONVERT`, `HCWOPENHEIGHT`, `HCWOPENSCHED`, `HCWCOLQTY`.
- The BricsCAD and ZWCAD projects were not built.
- The installers (`build\Package-Installers.ps1`) were not built.

## Open problem: ribbon not loading

The user reported "RIBBON NOT LOADING" after a `NETLOAD` of a build from this repo. It is not confirmed fixed.

What the code does now (`src/HCW.AutoCAD.Plugin/UI/HcwRibbonApplication.cs`): `Initialize()` subscribes to `Application.Idle`. The handler keeps trying on each idle until `ComponentManager.Ribbon` exists, then unsubscribes and calls `BuildRibbon()`. Both tabs are added only after every panel has been built, so a failing panel leaves no half-built tab. Any exception is written to the command line as `[hcwCAD-KIT] ribbon build error: <type>: <message>` (an alert if no drawing is open). `HCWRIBBON` rebuilds the tabs, replacing any of ours already there.

Things to check, in order (none confirmed):

1. Did the command line print `[hcwCAD-KIT] ribbon build error: ...`? If so, that message is the lead. Run `HCWRIBBON` to see it again.
2. If it printed "waiting for the ribbon", the workspace has no ribbon: switch to one (`RIBBON`), then run `HCWRIBBON`.
3. A tab with the same id already existing (second `NETLOAD`, or an old copy of the DLL loaded). Restart AutoCAD to rule this out.
4. The build used the AutoCAD 2022 reference DLLs; check the DLL was loaded into the same AutoCAD version.
5. `IconLoader` returns null for a missing icon (all icon names used were checked against the embedded resources); `TitleNoteLibrary.Ensure()` runs while the notes panel builds, and an exception from it lands in the same error message.

## Build on the dev machine used so far

- No system .NET SDK. A per-user SDK 8.0.425 is in `%LOCALAPPDATA%\Microsoft\dotnet` and is on the user PATH (new terminals only).
- `git` is not on PATH; the copy bundled with GitHub Desktop was used: `C:\Users\holag\AppData\Local\GitHubDesktop\app-3.6.6\resources\app\git\cmd\git.exe`.
- The only AutoCAD installed is 2022. Passing `-p:AutoCADInstallDir="...\"` fails in PowerShell (the trailing `\"` is mangled), so set the environment variable instead:

```
$env:AUTOCAD_INSTALL_DIR = "C:\Program Files\Autodesk\AutoCAD 2022\"
dotnet build src\HCW.AutoCAD.Plugin\HCW.AutoCAD.Plugin.csproj -c Release -p:Platform=x64
dotnet test tests\HCW.Logic.Tests
```

Output: `src\HCW.AutoCAD.Plugin\bin\x64\Release\hcwCAD-KIT.dll`. Close and reopen AutoCAD before loading a new build, or the old DLL stays loaded.

## Git and push

- Upstream `main` can receive new commits quickly, so a local branch falls behind. Use `git pull --rebase` and push straight afterwards.
- Pushing from an agent shell can fail with `could not read Username for 'https://github.com'`: the Git Credential Manager has no saved sign-in there. Push from GitHub Desktop or a terminal that is signed in to GitHub.

## What to do next

1. Load the build in AutoCAD; if the ribbon does not appear, read the command line message or run `HCWRIBBON` (above).
2. Run each new command on a real drawing and fix what breaks. Start with `SHEETFIT`, `HCWWALL` (hatch, measurement line), door and window insert (lintel, `W1/3` tags, schedule sync), `HCWAXIS`, `HCWCLEAN`, `HCWCORNER`, `HCWAUDIT`, `HCWSECTIONDRAW`.
3. Build and test the BricsCAD and ZWCAD projects (they need the host API DLLs).
4. Build the installers with `build\Package-Installers.ps1` (Inno Setup 6 required) and test one install.
5. Set the bylaw figures listed in the ROADMAP from the real rules.
