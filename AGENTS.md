# Axiom Force Racing agent guidelines

Read README.md, HANDOFF.md and MEMORY.md first. Update MEMORY.md after each task.
Apply the Google EGM skills to planning and review. Use Graphify when this repository has a local graph and verify its results against source.

Use the official Unity plugin and skills for your agent. Use the Docker skills for all Docker work and the PostgreSQL skills for database work. Use the relevant Android and store compliance skills for store-facing changes.

Use the Windows Unity CLI for project initialization, Unity editing, imports, Unity tests and launches. Unity compiles its own scripts as part of these Editor operations. Run standalone C#/.NET tooling and tests through the opt-in `csharp` Docker service. Run PostgreSQL in Docker and access player data through the API.

Only export after the owner explicitly requests a named target. Use the official Unity CLI and Unity build pipeline through the local opt-in export workflow. Do not export through GitHub Actions, on startup, or as part of testing. iOS needs a separately approved native Mac workflow because Unity's official Linux container does not support it. Finish Apple signing and distribution on a Mac.

The racing game is at the outline stage. Asset creation requires a further request. Keep source control and service data separate from the original game. Ask before spending credits or provisioning external services.
