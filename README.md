# MatchRecord

Records a whole match: PNG frames, player inputs and game state (positions,
speeds, level grid), to replay a game or feed a machine learning pipeline. It can
also assemble one GIF per round.

A mod for **FortRise 5** (>= 5.3.3). The FortRise 4 version (`tf-mod-fortrise-record`) is no longer maintained: fixes and new features only land in this repository.

## Installation

1. Install FortRise 5 and start the game through `FortRise.exe`.
2. Copy `release/matchrecord` (or the shipped folder) into `<TowerFall>/FortRise/Mods/`.

Settings are under **Options > Mods > MatchRecord**.
Data and log files live in `<TowerFall>/FortRise/Saves/MatchRecord/` and `<TowerFall>/FortRise/Logs/`.

## Usage

Everything is driven from the mod settings; there is no key to remember. Recording
starts on the first round played and stops on returning to the menu.

**One match = one folder**:
`<TowerFall>/FortRise/Saves/MatchRecord/Recordings/match_<timestamp>/`.

Only live play is captured: the `FIGHT!` cutscene, pauses and between-round loading
are excluded, while the last player's death is included.

## Settings

| Setting | Default | Purpose |
|---------|---------|---------|
| Enable recording | off | master switch |
| Captures per second | 15 | capture rate |
| Record images (PNG) | on | 320x240 frame sequence |
| PNG compression | Balanced | size/CPU trade-off (lossless either way) |
| Record player inputs | on | each player's inputs, per frame |
| Record game state | on | positions, speeds and level grid |
| Make GIF per round | off | assemble a GIF at the end of each round |
| GIF quality | High | GIF size/fidelity trade-off |

## Files produced

- `round_00_frame_000000.png`, ... - frames, prefixed with the round number
- `inputs.jsonl` - one JSON line per frame: direction, aim, buttons
- `state.jsonl` - one JSON line per frame: players and arrows
- `round_00_level.json` - solid grid of the level
- `Recordings/gif/<match>_round_00.gif` - when GIFs are enabled

The `"f"` field ties frames to JSON lines; it runs continuously across the match.

## Tool: `tools/make_gif.py`

Assembles the PNGs into GIFs, one per round, without replaying anything. Drop the
script into `Recordings/` and run it with no argument: it processes every `match_*`
folder sitting next to it.

```bash
python make_gif.py --quality medium
```

Main options: `--quality high|medium|low`, `--colors N`, `--every N` (keep one frame
in N), `--fps`, `--scale`, `--force`. Pillow required.

## Build / deployment

| Script | Purpose |
|--------|---------|
| `script/release.bat` | build, then assemble into `release/` |
| `script/deploy.bat` | copy `release/` into the TowerFall `Mods` folder |
| `script/release_deploy.bat` | both, one after the other |

Paths (game folder, module name) are set in `script/config.bat`.
