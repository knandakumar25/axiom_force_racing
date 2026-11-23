# Axiom Force: Drift Circuit

A 3D arcade kart racer set in the Axiom Force world of Los Angeles in 2157. Its race loop centers on drifting for boost, choosing shortcuts and timing power-ups. The [game outline](GAME_OUTLINE.md) proposes eight circuits across the City and Skyline Cups. There is no story campaign, dialogue or narrative cutscene.

The Unity project currently contains the bare 3D URP template. Playable circuits, kart handling and pickups are still in design. The first prototype is a Little Tokyo–Skyport time trial with checkpoints, safe resets and a finish result.

## Local setup

Install the official Unity CLI and Unity Editor **6000.3.29f1** on Windows. Sign in with `unity auth login` and confirm an active Editor license with `unity license status`. Open the project from the repository root:

```powershell
unity open ./Game
```

Use the Windows Unity CLI for Editor operations, imports and Unity tests. Unity Package Manager resolves the project's dependencies on first open. The project uses URP, the Input System and Unity Pipeline. Inkle Ink is installed for optional tooling and is currently unused.

Tripo DCC Bridge 1.0.14 is an optional local Editor plugin. Download the [official Unity Bridge ZIP](https://www.tripo3d.ai/blog/tripo-dcc-bridge-for-unity) and extract its package folder to `Game/Packages/com.tripo3d.unitybridge/` if you need it. The directory should directly contain the vendor's `package.json`. Git excludes the vendor files, so each checkout can install the plugin locally. See [HANDOFF.md](HANDOFF.md) for the Windows certificate workaround if package resolution fails.

## Development services

Copy `.env.example` to `.env`. Set a unique development database password and a random JWT signing key of at least 32 characters. From the repository root, start the default stack:

```powershell
docker compose up -d --wait
```

| Service | Address or command |
| --- | --- |
| PostgreSQL | `127.0.0.1:5433`, with a separate Compose data volume |
| Express API | `http://localhost:8001/api/health` |
| Development website | `http://localhost:5174`, with `/api/` proxied to Express |
| Backend tests | `docker compose exec -T backend npm test` |
| Browser smoke test | `docker compose run --rm playwright` |
| Standalone C# tools | `docker compose run --rm csharp --info` |
| Python and FFmpeg | Opt-in `cutscene` service |

The website is a placeholder endpoint. The Express service provides the accounts and save API foundation. Unity integration with that API is future work. PostgreSQL is accessed through the API for game data.

## Exports

The targets are Windows, Linux, Web, Android, macOS and iOS. The local Docker export service is opt-in and requires an explicit owner request and target. It is excluded from GitHub Actions and regular test runs.

The export setup still needs a working official Unity image pull, a supported Linux host with a reachable floating license server, a matching Unity project version and target build profiles. The official Linux container excludes iOS, which needs a separately approved native Mac workflow. See [export/README.md](export/README.md) for target details.

## Repository layout

| Path | Contents |
| --- | --- |
| `Game/` | Unity assets, packages and project settings |
| `backend/` | Express API, SQL migrations and tests |
| `website/` | Development website and API proxy |
| `tools/` | Playwright smoke test and media tooling |
| `unity/` and `export/` | Headless Editor image recipe and opt-in exporter |
| `AGENTS.md` and agent-specific instructions | Project workflows and rules |
| `HANDOFF.md` and `MEMORY.MD` | Current setup and decisions |

Commit Unity source assets together with their `.meta` files, the package manifests and project settings. Git excludes generated caches, local credentials, build outputs and the local Tripo download. The Docker build contexts each include only the inputs needed by that image.
