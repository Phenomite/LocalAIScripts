# Phenom LocalLLM

Needs RTX 3060 or better, >32GB RAM, and PCIe 4+ NVMe SSD:

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

## Maya `DS41`

My best settings tune:

```json
 "args": [
  "--max-context", "262144",
  "--prefill-chunk", "4096",
  "--reserve-mib", "384",
  "--tp", "1",
  "--threads", "on",
  "--cpu-threads", "20",
  "--experts", "5",
  "--dspark", "on",
  "--dspark-conf", "0.15",
  "--vision", "off",
  "--parallel", "1",
  "--mmap-experts", "on",
  "--ssd-cache", "auto",
  "--ssd-policy", "lfu"
 ],
 "env": {
  "MAYA_DS4_STREAM": "0",
  "DS4_STREAM_WIN": "0",
  "DS4_LAND": "0",
  "DS4_STREAM_ZC": "0",
  "MAYA_VRAM_LATE_MB": "0",
  "MAYA_DS4_DRAFT_VRAM": "0",
  "MAYA_DS4_MARKOV_Q8": "1",
  "MAYA_DS4_ROUTER_BF16": "1",
  "MAYA_DS4_HC_FUSE": "2",
  "MAYA_DS4_ROUTE_FUSE": "2",
  "MAYA_DS4_DECODE_TRIM": "15",
  "DS4_DSPARK_SAMPLE": "1",
  "MAYA_DS4_DRAFT_COST": "1",
  "DS4_RING_MB": "64",
  "DS4_LANE_SPIN_US": "50",
  "DS4_STORE_ROOM_GB": "8",
  "DS4_STORE_UNBUFFERED": "1",
  "DS4_STORE_THREADS": "16",
  "DS4_EG_ZONE_KB": "4096",
  "MAYA_DS4_SLOT_KEEP": "1",
  "MAYA_DS4_SLOT_CAP": "8",
  "MAYA_DS4_SLOT_RAM_GB": "1",
  "MAYA_DS4_SLOT_GB": "32",
  "MAYA_DS4_RESTORE_POINTS": "6",
  "MAYA_DS4_KEEP_THINKING": "1"
 },

 "sampling": {
  "temperature": 0.9,
  "top_p": 0.95
 },
```
