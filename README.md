# Axiom Force: Drift Circuit

A 3D arcade kart racer set in the Axiom Force world of Los Angeles in 2157. Its race loop centers on drifting for boost, choosing shortcuts and timing power-ups. The [game outline](GAME_OUTLINE.md) proposes eight circuits across the City and Skyline Cups. There is no story campaign, dialogue or narrative cutscene.

The offline time trial runs for three laps on any of these circuits.

| Circuit | Routes |
| --- | --- |
| Little Tokyo–Skyport | Market and Skyport loop or maintenance shortcut |
| Concrete Wards Viaduct | Viaduct boulevard or service tunnel |
| Stratos Docks | Inland loading-yard bend or narrow quay shortcut |
| Rustrail Junction | Always-open switchback or timed rail crossing |

Stratos Docks opens eastbound on a flat waterfront straight. Follow East Pier through the loading-yard chicane, take the westbound back straight to Quay Approach, then choose a route to the shared finish. Cross-road stripes mark the chicane braking zone. Amber dashes lead to the quay entrance.

Rustrail Junction is the final City Cup circuit. Follow Rail Yard, Signal Approach, and Crossing Approach before choosing the left-hand switchback or the right-hand crossing. From GO, the crossing repeats a 24-second cycle. **OPEN** lasts 12 seconds, **CLOSING** warns for 3 seconds while the lane is still passable, and **CLOSED** lasts through the end of the cycle. The barrier waits for either active kart occupying its path. Pausing freezes the timer, Retry opens the crossing for a new attempt, and Reset returns to the last checkpoint without restarting the cycle.

## Play

Open `Assets/DriftCircuit/Scenes/TrackSelect.unity` in the Unity project and enter Play Mode. Track Select is the startup scene and shows the best lap and best total for each circuit. The original Little Tokyo scene also supports entry directly into the race.

Choose **Quick Race** for any City Cup circuit (**Little Tokyo–Skyport**, **Concrete Wards Viaduct**, **Stratos Docks**, or **Rustrail Junction**), in the right-hand Track Select column, for a three-lap offline race against the orange AI kart. You can take either route. Both races use the same kart handling, contact, recovery, controls, and finish rules. Little Tokyo–Skyport and Concrete Wards Viaduct Quick Races use a seeded random item pool across three `ITEM GATE` planes per circuit (weighted toward `Relay Boost` and `Slipstream Link` when trailing). Stratos Docks keeps its six named item gates. Rustrail Junction Quick Race remains item-free. Track Select also includes a **Route Beacon** toggle to highlight shortcut guides during play, and syncs personal bests and preferences to PostgreSQL through the racing Express API (`http://localhost:8001`) when signed in (falling back to local `TrialRecordStore` offline). The Docks rival follows the loading-yard route and keeps its waterfront pace. On Rustrail, the rival decides once per approach at z = 32, before the fork. It takes the crossing only when the scheduled opening leaves enough time to clear the whole kart at 6 m/s plus a one-second margin, and the player is clear of the crossing approach. Otherwise it commits to the switchback. A rival delayed after committing waits behind a closed barrier without triggering stuck recovery. Either kart can defer closure while occupying the barrier. If the normal reset spot is occupied, the kart tries either side and waits if neither side is free. The race clock and completed checkpoints carry through a reset.

| Action | Keyboard | Gamepad |
| --- | --- | --- |
| Steer | A / D or left / right arrow | Left stick |
| Accelerate | W or up arrow | Right trigger |
| Brake / reverse | S or down arrow | Left trigger |
| Drift | Space | Right shoulder |
| Use held item (Docks Quick Race) | E | West face button (X / Square) |
| Reset | R | Y |
| Pause / resume | Escape | Start |
| Move menu selection | Up / down arrow or W / S | D-pad or left stick |
| Confirm menu selection | Enter | A |

Hold drift around a turn for half a second while moving forward, then release to use a one-second boost. Drive through the highlighted gates in order. Reset returns the kart to the last valid checkpoint while the clock keeps running. Retry starts a new attempt on the current circuit. Track Select is available from pause and results. Mouse clicks also work on menu buttons.

In **Stratos Docks Quick Race**, each kart holds one item: **Relay Boost**, **Flux Ribbon**, **Pulse Shield**, **Signal Tap**, or **Slipstream Link**. Press E or the gamepad's west face button to use it. Both Relay gates remain; the westbound back straight has a cyan Shield gate at x=60 and a magenta Ribbon gate at x=-20; and the eastbound straight adds an amber Signal Tap gate at x=-20 and a green Slipstream Link gate at x=50. Each labelled, non-solid gate grants an item once per kart per valid lap. A full slot preserves its item and spends that gate's allowance. Invalid use keeps the item.

Signal Tap fires a visible 48 m/s straight-ahead pulse with no tracking up to 45 metres. The first grounded opponent it reaches suffers one second of reduced forward acceleration (`5.5 m/s²` instead of `13 m/s²`), followed by 1.5 seconds of immunity; steering, braking, and active boost remain available, and Pulse Shield absorbs the hit. Slipstream Link opens a four-second link window that grants draft assist (`30 m/s` target speed, `21 m/s²` acceleration) while holding a grounded, non-braking line `3–28` metres behind the opponent within `±2.2` metres laterally and `20°` of heading; leaving the draft cone immediately drops the assist.

Relay Boost retains its one-second, 34 m/s boost and requires grounded forward movement above 4 m/s without braking. Ribbon has the same movement requirement, plus alignment within 5° on a shared straight. Allowed drops are eastbound at z=-64, x=-50 to -20 or 20 to 50, and westbound at z=62, x=-40 to 30, within 4.5 metres of the centre. Drops also require the whole patch to stay 12 metres clear of checkpoints, grids and recovery positions. The striped 3×4-metre patch lands 3.8 metres behind, arms after 0.4 seconds and expires after six seconds. Each kart owns at most one live patch and is immune to its own. A grounded opponent crossing it loses lateral grip for one second, including during drift; steering, braking and boost remain available. Grip recovery grants 1.5 seconds of immunity. Each patch can affect an opponent only once, including contacts made during immunity.

Shield can activate while stationary or braking. Its cyan outline lasts three seconds and absorbs one ribbon hit, then grants 1.5 seconds of immunity. It does not cure grip loss already in progress; trying another Shield while protected keeps the held item. The HUD separates the item/binding, active boost, Shield, grip loss and immunity timers, with a one-second incoming-ribbon warning. Pause freezes every timer. Recovery preserves inventory and pickup history, clears active effects and owned patches, and grants 1.5 seconds of immunity; Retry and Track Select clear all item state.

The rival retains its clear-straight Relay policy. It uses Shield for a predicted opposing ribbon contact within one second, and drops Ribbon only for an eligible, unprotected player 8–30 metres behind whose velocity predicts contact 0.4–1.5 seconds later. It retains items when those conditions fail, so an unaided rival can keep a Shield through the finish. Rustrail Quick Race and every time trial remain item-free.

Results show the total, all three lap splits and any new personal bests. Only a completed run can set a record. The fastest lap can come from a different run than the fastest total. Records are saved locally in `time-trial-records` under Unity's persistent data directory.

The ghost replays the fastest completed run on the chosen circuit. It appears by default when a record exists. The track menu remembers the Ghost toggle. Checkpoint deltas compare elapsed time against the same lap and checkpoint in the saved run, even when the ghost is hidden. Negative deltas mean you are ahead.

Quick Race results appear when you finish. An unfinished rival is listed second as **Still racing**, and a same-step finish shares first place. Retry restarts both karts. Ghosts, checkpoint deltas, and personal bests are time-trial features.

### Run the Unity tests

With the project open in the licensed Windows Editor, run these commands from the repository root. Poll the second command until the run finishes.

```powershell
unity command --project-path ./Game test run --mode all --async-tests true
unity command --project-path ./Game test status
```

The Play Mode suite uses virtual keyboard, gamepad and mouse input. It drives each time trial through the main route, shortcut, and main route over three laps and checks access to every Track Select control. The Docks time trial requires at least 22 m/s on the waterfront and compares the yard and quay times from Quay Approach to Finish. Rustrail tests cover the crossing schedule, the branch barrier, checkpoint recovery, the timed route comparison, and ghost playback after Retry.

Rustrail Quick Race tests cover scheduled route decisions, both committed routes, approach occupancy, closure deferral with either kart or both, delayed entry, recovery before the fork, pause, Retry, and return to Track Select. A virtual gamepad drives both player routes and compares all four seeded record files byte for byte and checks the Ghost preference. A separate run requires the rival to finish three laps within 200 seconds with zero recoveries and no player driving input.

Docks Quick Race tests cover the shared GO step, physical contact, pause, occupied and stuck recovery, both finish orders, a shared first, Retry, and return to Track Select. A virtual gamepad drives the player through the yard, quay, and yard and compares all four seeded time-trial record files byte for byte. A separate run checks that the rival completes three loading-yard laps within 175 seconds with zero recoveries while the player provides no driving input.

The final Signal Tap and Slipstream Link Windows Unity CLI run passed **99 Edit Mode cases and 25 Play Mode tests** (`124/124` combined, `694.8s`), with zero new Console errors or warnings (cursors 208–233). The Docks gamepad completed yard, quay, and yard in 142.54 seconds, collecting 15 items and using 15 across all five item types. The unaided rival finished in 141.60 seconds with zero recoveries, collecting 2 items, using 1, reaching 34.00 m/s under boost, and retaining Pulse Shield without an incoming threat. Both Quick Races preserved all four seeded record files byte for byte and the Ghost preference. Rustrail Quick Race retained a 171.96-second gamepad finish. Backend tests passed 182/182. Evidence is saved locally in `.validation/signal-slipstream/`.

The earlier Flux Ribbon and Pulse Shield Windows Unity CLI run passed **92 Edit Mode cases and 24 Play Mode tests** (`116/116` combined, `690.99s`), with zero new Console errors or warnings (cursors 149–174). The Docks gamepad completed yard, quay, and yard in 141.96 seconds, collecting 9 items and using 9 across all three item types. The unaided rival finished in 141.60 seconds with zero recoveries, collecting 2 items, using 1, reaching 34.00 m/s under boost, and retaining Pulse Shield without an incoming threat. Both Quick Races preserved all four seeded record files byte for byte and the Ghost preference. Rustrail Quick Race retained a 171.94-second gamepad finish. Backend tests passed 182/182. Staged tests and 1280×720 captures in `.validation/flux-shield/` cover swept ribbon arming/expiry, placement limits, grip loss with preserved steering/braking/boost, shield absorption and immunity, rival threat prediction and rejection/retention, pause/recovery/Retry cleanup, and HUD timers. The static scan retains only the four previously documented DefaultVolumeProfile.asset references.

The earlier Relay Boost Windows Unity CLI run passed **38 Edit Mode cases and 22 Play Mode tests**, with zero new Console errors or warnings (cursors 39–64). The Docks gamepad completed yard, quay, and yard in 142.40 seconds, collecting four items and using three. The unaided rival finished in 134.24 seconds with zero recoveries, collecting and using six items and reaching 34 m/s under boost. Both Quick Races preserved all four seeded record files byte for byte and the Ghost preference. Rustrail Quick Race retained a 171.96-second gamepad finish and a 150.34-second unaided rival finish. Backend tests passed 182/182. Relay Boost tests cover swept direction and width/height limits, fast passage, per-lap and independent allowances, full slots, recovery/Retry, one-shot keyboard/gamepad input, phase suppression, same-step pickup/use, shared drift/item duration, braking, rival eligibility and sustained acceleration. Pickup and HUD captures, isolated EGM review, and full results are saved locally in `.validation/relay-boost/`. The static scan retains only the four previously documented DefaultVolumeProfile.asset references.

The earlier Rustrail time-trial milestone passed 9 Edit Mode and 9 Play Mode tests with zero new Console errors or warnings. Rustrail's virtual gamepad run finished in 171.72 seconds. From Crossing Approach to Finish, the crossing took 15.40 seconds and each switchback took 20.74 seconds. Chase captures showed the advance signal from 34 metres before the fork at 43 km/h, a pass through the crossing during CLOSING, and an open switchback with the barrier down. Retry replayed the saved Rustrail ghost. The other three record files matched their starting contents.

## Local setup

Install the official Unity CLI and Unity Editor **6000.3.29f1** on Windows. Sign in with `unity auth login` and confirm an active Editor license with `unity license status`. Open the project from the repository root:

```powershell
unity open ./Game
```

Use the Windows Unity CLI for Editor operations, imports and Unity tests. Unity Package Manager resolves the project's dependencies on first open. The project uses URP, the Input System and Unity Pipeline. Inkle Ink is installed for optional tooling and is currently unused.

Tripo DCC Bridge 1.0.14 is an optional local Editor plugin. Download the [official Unity Bridge ZIP](https://www.tripo3d.ai/blog/tripo-dcc-bridge-for-unity) and extract its package folder to `Game/Packages/com.tripo3d.unitybridge/` if you need it. The directory should directly contain the vendor's `package.json`. Git excludes the vendor files, so each checkout can install the plugin locally. See [HANDOFF.md](../HANDOFF.md#drift-circuit) for the Windows certificate workaround if package resolution fails.

## Development services

Copy `.env.example` to `.env`. Set a unique development database password and a random JWT signing key of at least 32 characters. From the repository root, start the default stack:

```powershell
docker compose up -d --wait
```

| Service | Address or command |
| --- | --- |
| PostgreSQL | `127.0.0.1:5433`, with a separate Compose data volume |
| Express API | `http://localhost:8001/api/health` |
| Shared player website | [http://localhost:5173](http://localhost:5173), started from the sibling website checkout |
| Backend tests | `docker compose exec -T backend npm test` |
| Browser smoke test | `./tools/website-smoke.ps1` |
| Standalone C# tools | `docker compose run --rm csharp --info` |
| Python and FFmpeg | Opt-in `cutscene` service |

Start the shared site with `../axiom_force_website/start.ps1`. It serves the Drift Circuit outline and uses the original game accounts API. The racing API and database are separate and reserved for local development. Racing browser sign-in and online features remain future work. The Express service provides the accounts and save API foundation. Unity integration with that API is future work. PostgreSQL is accessed through the API for game data.

## Exports

The targets are Windows, Linux, Web, Android, macOS and iOS. The local Docker export service is opt-in and requires an explicit owner request and target. It is excluded from GitHub Actions and regular test runs.

The export setup still needs a working official Unity image pull, a supported Linux host with a reachable floating license server, a matching Unity project version and target build profiles. The official Linux container excludes iOS, which needs a separately approved native Mac workflow. See [export/README.md](export/README.md) for target details.

## Repository layout

| Path | Contents |
| --- | --- |
| `Game/` | Unity assets, packages and project settings |
| `backend/` | Express API, SQL migrations and tests |
| `tools/` | Shared website smoke launcher and media tooling |
| `unity/` and `export/` | Headless Editor image recipe and opt-in exporter |
| `AGENTS.md` and agent-specific instructions | Project workflows and rules |
| `HANDOFF.md` and `MEMORY.MD` | Current setup and decisions |

Commit Unity source assets together with their `.meta` files, the package manifests and project settings. Git excludes generated caches, local credentials, build outputs and the local Tripo download. The Docker build contexts each include only the inputs needed by that image.

