# Test log

Written by **Agent 1** only. One entry per tested build, newest first. Rules: [../AGENTS.md](../AGENTS.md) (B1, B2, L1 to L3). Levels: L0 unit tests, L1 CAD-free compile check, L2 real build against AutoCAD 2022, L3 host test (AutoCAD), L4 install and uninstall, L5 AQC round trip. Every entry ends with **Not run**.

## Machine

(Agent 1 keeps this current, rule L9. Same facts as AGENTS.md section 8.)

- Windows 11; .NET SDK 8.0.425 (per-user); AutoCAD 2022; `accoreconsole.exe` present.
- Not installed: Inno Setup 6, BricsCAD, ZWCAD, CMake, MSVC, WinUI workload.

## 2026-10-07 · `c85216f` · PARTIAL

| Level | Check | Result |
|---|---|---|
| L0 | `dotnet test tests/HCW.Logic.Tests` | PASS: 594 passed, 0 failed, no analyzer warnings |
| L2 | Release x64 build against AutoCAD 2022 | PASS: 0 warnings, 0 errors. DLL `bin\x64\Release\hcwCAD-KIT.dll`, built 2026-10-07 21:24 from the source of `ca3acbd` (the commits after it changed only docs and tests) |

Not run: **L1** (no check of `build/CompileCheck.csproj` on this commit; CI runs it on push), **L3** (no command in a CAD program was run by Agent 1, so none of D-001 to D-004 is verified; the owner loaded earlier builds in AutoCAD and reported the problems now in DEFECTS.md, but no commands or results were recorded), **L4** (no installer built: Inno Setup is not installed; the DLL was loaded with `NETLOAD`, not installed), **L5** (AQC cannot be built here), BricsCAD and ZWCAD.
