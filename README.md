# Axiom Force: Drift Circuit

A 3D arcade kart racer set in the Axiom Force world of Los Angeles in 2157. Its race loop centers on drifting for boost, choosing shortcuts and timing power-ups. The [game outline](GAME_OUTLINE.md) proposes eight circuits across the City and Skyline Cups. There is no story campaign, dialogue or narrative cutscene.

The offline time trial runs for three laps on any of these circuits.

| Circuit | Routes |
| --- | --- |
| Little Tokyo–Skyport | Market and Skyport loop or maintenance shortcut |
| Concrete Wards Viaduct | Viaduct boulevard or service tunnel |
| Stratos Docks | Inland loading-yard bend or narrow quay shortcut |

Stratos Docks opens eastbound on a flat waterfront straight. Follow East Pier through the loading-yard chicane, take the westbound back straight to Quay Approach, then choose a route to the shared finish. Cross-road stripes mark the chicane braking zone. Amber dashes lead to the quay entrance.

## Play the time trial

Open `Assets/DriftCircuit/Scenes/TrackSelect.unity` in the Unity project and enter Play Mode. Track Select is the startup scene and shows the best lap and best total for each circuit. The original Little Tokyo scene also supports entry directly into the race.

| Action | Keyboard | Gamepad |
| --- | --- | --- |
| Steer | A / D or left / right arrow | Left stick |
| Accelerate | W or up arrow | Right trigger |
| Brake / reverse | S or down arrow | Left trigger |
| Drift | Space | Right shoulder |
| Reset | R | Y |
| Pause / resume | Escape | Start |
| Move menu selection | Up / down arrow or W / S | D-pad or left stick |
| Confirm menu selection | Enter | A |

Hold drift around a turn for half a second while moving forward, then release to use a one-second boost. Drive through the highlighted gates in order. Reset returns the kart to the last valid checkpoint while the clock keeps running. Retry starts a new attempt on the current circuit. Track Select is available from pause and results. Mouse clicks also work on menu buttons.

Results show the total, all three lap splits and any new personal bests. Only a completed run can set a record. The fastest lap can come from a different run than the fastest total. Records are saved locally in `time-trial-records` under Unity's persistent data directory.

The ghost replays the fastest completed run on the chosen circuit. It appears by default when a record exists. The track menu remembers the Ghost toggle. Checkpoint deltas compare elapsed time against the same lap and checkpoint in the saved run, even when the ghost is hidden. Negative deltas mean you are ahead.

### Run the Unity tests

With the project open in the licensed Windows Editor, run these commands from the repository root. Poll the second command until the run finishes.

```powershell
unity command --project-path ./Game test run --mode all --async-tests true
unity command --project-path ./Game test status
```

The Play Mode suite uses virtual keyboard, gamepad and mouse input. It drives each circuit through the main route, shortcut, and main route over three laps. It checks that all three rows and Ghost are reachable, and that a Docks retry loads the saved ghost while the other circuit records stay independent. The Docks race requires at least 22 m/s on the waterfront and compares the yard and quay times from Quay Approach to Finish.

The Windows Unity CLI passed 8 Edit Mode and 7 Play Mode tests with zero new Console errors or warnings. The Docks kart reached 25.0 m/s on each lap. The quay saved 3.14 seconds in the timed run, and a reset from inside the quay returned the kart to the shared approach with Finish still next.

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
