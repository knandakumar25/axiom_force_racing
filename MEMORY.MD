# Axiom Force Racing project memory

- Owner requested a sibling Unity repo for an original arcade kart racer in the Axiom Force setting. Use D:/Projects/axiom_force_racing as the working path with origin configured at https://github.com/knandakumar25/axiom_force_racing.git.
- Game design is an outline only. No gameplay, scenes, models, textures or audio were created.
- Windows Unity CLI for project creation, editing, Unity tests and launches. Docker for standalone C#/.NET tools, PostgreSQL and Unity player exports. Exports need explicit authorization and target selection. iOS remains a target with an outstanding native Mac workflow decision.
- Preserve PostgreSQL, backend, website, Playwright and Python/FFmpeg service roles with separate ports/data. Unity plugin and relevant skills apply to Codex, Claude Code and Gemini.

- Local Git initialized on main. Remote connected. The default stack is up
  with separate PostgreSQL storage, API on 8001 and website on 5174.
- Compose validation, Chromium website/API smoke test and all 182 Express
  tests passed. The official Unity base image pull returned not found.
  Unity project initialization and Editor validation remain pending.
- The export service is opt-in and defaults to help. Its script was syntax
  checked. No Unity builds, exports or game asset generation ran.

- Verified the revised Compose configuration and the standalone C# container
  with dotnet --version (10.0.404). No Unity project assets or exports created.

- Chosen title is Axiom Force: Drift Circuit. The authorized bare Unity setup
  extracted starter files in Game/ but stopped before writing ProjectVersion.txt.
  Editor terms acceptance is pending. No commit, push or export was run.

- Owner confirmed the racer has no story campaign, dialogue or narrative cutscenes. Ink and Tripo Bridge were requested as optional Unity plugins.

- User accepted Editor 6000.3.29f1 terms. Windows Unity CLI completed the bare URP project initialization under Game/. The incomplete attempt was preserved in a dated sibling backup. Unity Cloud linking was disabled.

- Installed and resolved Ink 2.0.0, Unity Pipeline 1.0.1 and local Tripo Bridge 1.0.14. No narrative was authored, no models were imported and the Tripo service was not opened.

- Expanded the Drift Circuit outline to eight candidate tracks across City and Skyline Cups. Added six potential items, a three-item prototype and rules for pickup placement, counterplay and time trials. Documentation only. No tracks, game assets or exports were created.

- Prepared the initial GitHub push with scoped Unity ignore rules, build-context allowlists and a README covering project status, local setup, plugins, services and export gates.

## Shared website (2026-11-23)

The sibling `axiom_force_website` checkout serves Drift Circuit at
`http://localhost:5173/en/games/drift-circuit`. The racing placeholder
website and browser image were retired. Run `./tools/website-smoke.ps1`
against the shared site. Racing Express remains on port 8001 and
PostgreSQL on port 5433 with the existing volume. The racing Compose
export profile is unchanged. The shared site connects to the original API.
