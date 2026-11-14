# Phenom LocalLLM

Needs RTX 3060 or better, ~60 Gb RAM, and PCIe 4+ SSD:

1. Install:
    - `1-INSTALL-OMP.bat`;
    - `2-INSTALL-STRATA.bat` - pick `BOTH` for easiest next steps.
2. Run Strata GUI.
3. Pick Best Model (examples for 12 Gb VRAM) (or swap for ablated `~/Documents/Strata/Models/`):
    - DeepSeek 4.1 Flash 1017 -> `IQ2_XXS`
    - Qwen3.8 Flash Next -> `IQ3_S`
4. Start `StrataController.bat`
    - `Start Model`
    - `Sync OMP`
    - `Start OMP`
5. ???
6. Profit.

## Rules

Copy `rules\` into OMP, overwriting same-named files and leaving anything else there alone (run from this folder, no admin needed):

```powershell
$d = "$env:USERPROFILE\.omp\agent\rules"; New-Item -ItemType Directory -Force $d | Out-Null; Copy-Item .\rules\* $d -Force
```
