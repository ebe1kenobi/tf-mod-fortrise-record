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

<img width="683" height="561" alt="image" src="https://github.com/user-attachments/assets/7c0def81-0313-4968-a111-9bc4adc264b7" />


**One match = one folder**, named after the mode it recorded:
`<TowerFall>/FortRise/Saves/MatchRecord/Recordings/<mode>_<timestamp>/` - for
instance `headhunters_20260811_204512/` or `darkworld_20260811_211003/`.

Only live play is captured: the `FIGHT!` cutscene, pauses and between-round loading
are excluded, while the last player's death is included.
<img width="987" height="718" alt="image" src="https://github.com/user-attachments/assets/58bb21b2-2442-4f52-a422-a6ceac6c42ba" />



### Versus and co-op

Both are recorded, each behind its own switch. They do not end a round the same way,
and that is the whole difficulty: **`Session.EndRound` is only ever called by the
three versus round logics.** Quest and Dark World never call it - a finished level
goes straight to `Session.GotoNextRound`. So the mod closes a round on *either*
boundary, and ignores the second one when both fire for the same round.

In Dark World, **a round is a level**: `round_00_` is the first level of the run,
`round_01_` the next one, and the GIF of a level is written when you leave it. The
last level has no boundary at all - a wiped party returns to the map - so it is
closed when the recording stops.

In Quest there are no levels either: the game runs **waves** of monsters under a
single round logic, and `QuestRoundLogic.CurrentWave` is the only thing that moves.
So in Quest **a round is a wave** - `round_00_` is the first wave, `round_01_` the
second, one GIF each.

The wave boundary is taken when the *next* wave starts, not when the last monster
dies. Between the two the game keeps running - you pick up arrows, the roman numerals
scroll across the screen - and those frames still belong to the wave you just
finished.

Which family a mode belongs to follows the game's own split, `MatchSettings.SoloMode`:
Quest, Dark World, Trials and the editor tests count as co-op, everything else as
versus. A mode added by another mod therefore always lands in one of the two.

## Watching a recording

Two ways in, and they show the same PNG frames.

**From the round results screen** - `BACK` (B on a pad), listed as `MATCH REPLAY` under
the vanilla guides. It replays the round that just ended, over the level, and hands the
results screen back untouched.



<img width="962" height="462" alt="image" src="https://github.com/user-attachments/assets/573ece84-1134-483b-9d26-6e2aeefe6a63" />

`BACK` because it is the only key that screen never reads. Every other one is taken:
`CONFIRM` continues, `ALT` runs the game's own replay, `SAVE REPLAY` saves it - and that
last one is the same physical button as `ALT2`, on which another mod already opens its
statistics table.

The screen is listened to with a **postfix**, never a prefix. The first attempt used a
prefix returning false, which cancels not only the original method but every prefix
registered after it - two other mods hook the same method, and the whole screen froze.

**From the pause menu** - `WATCH REPLAY` sits under the vanilla items. It replays the
round you are in, without leaving the match: opening the menu player from a running game
would abandon it. The end-of-match menu carries the same item as `WATCH MATCH REPLAY`,
and there it plays the whole match, which is by then complete. Both work in solo modes
too, where a round is a wave.

<img width="969" height="725" alt="image" src="https://github.com/user-attachments/assets/cc6b6a3b-ba2e-4018-9f46-c9d30a304684" />

Do not confuse either with the game's own `REPLAY` on the results screen. That one
replays the last few seconds from the game *state* - a slow motion of the action, no
HUD, and it stops on its own. `MATCH REPLAY` replays the recorded *images*, from the
start, with pause, seek and speed. One is a slow motion, the other is a tape deck.

**From the menu** - `Options > Mods > MatchRecord > Watch replays` lists every
recording, newest first, with its length. That one plays a whole match, rounds
included, rather than a single round.

| Key | Action |
|-----|--------|
| Confirm | pause / resume, or **watch again** once it has reached the end |
| Left / Right | seek 2 s, or one frame when paused |
| Up / Down | speed, from x0.25 to x4 |
| Back | close |

At the end the bar reads `END` and Confirm restarts from the beginning: there is
nothing left to resume, so "resume" no longer means anything there.

### Where the picture is placed

The screen and the interface are not the same size once a mod widens the game. WiderSet
takes the screen to 420 across but leaves the interface at its original 320, recentring
it by translating the UI layers by +50 - which is how the whole vanilla menu, written
for 320, stays centred without being touched. An entity drawing at 0 therefore lands at
50 on screen, and that was the offset on the replay.

So the bar is laid out in the **interface**, 320 across, where everything else in the
game lives and nothing can fall off the edge. The **picture** is placed on the
**screen**, whose width is taken from the picture itself: a recording is a whole-screen
capture, so its width *is* the width of the screen it was taken on. Nothing is asked of
WiderSet or of the engine, and the same arithmetic gives an offset of zero when no mod
widens the game - the frames are then 320 across.

The one case it does not cover is playing a recording taken wide on a game running
narrow: 50 pixels are then cropped on each side.

Speed `x1` is real time, and a seek is two real seconds - not two seconds' worth of
game frames. The recording is taken at the **configured** rate, 15 frames per second by
default, not at the game's 60: replaying it at the game's rate ran it four times too
fast and made a "two second" seek jump eight. The rate is therefore written next to the
frames, in `fps.txt`, when the recording starts - the setting may have changed since,
and it is the rate the pictures were *taken* at that counts. Recordings made before
that file existed are read as 15, the default they almost certainly used.

### Why the PNG frames and not the GIF

The GIF is quantised to a small palette and decimated by the frame-interval setting:
it is an export made to be shared. The PNGs *are* the recording, at the game's own
resolution and rate - and they hand over pause, frame stepping and variable speed for
free, which a GIF could not.

The constraint that decides the design: one decoded frame is 320x240 in RGBA, so
300 KB; a match holds a couple of thousand - **600 MB** if it were preloaded. So the
frames are streamed, and the work is split because the two halves have different
constraints. Reading the file is pure disk wait, so it runs on a background thread
half a second ahead; decoding into a texture has to stay on the main thread, which
is the only one that owns the graphics device. Between the two sits a small queue of
bytes. If the disk falls behind, the previous frame stays on screen rather than
skipping or going black.

Opening a replay right as a round ends waits for the writer queue to drain first -
frames leave on a background thread several seconds behind, and replaying without
waiting would show only the beginning.

## Settings

| Setting | Default | Purpose |
|---------|---------|---------|
| Enable recording | off | master switch |
| Record versus matches | on | Last Man Standing, Headhunters, Team Deathmatch |
| Record co-op | on | Quest, Dark World, Trials, editor tests |
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
- `Recordings/gif/<match>_round_00.gif` - when GIFs are enabled (one per level in co-op)

The `"f"` field ties frames to JSON lines; it runs continuously across the match.

## Tool: `tools/make_gif.py`

Assembles the PNGs into GIFs, one per round, without replaying anything. Drop the
script into `Recordings/` and run it with no argument: it processes every `match_*`
folder sitting next to it (any `*_<timestamp>` recording folder).

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
