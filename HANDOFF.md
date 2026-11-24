# Racing project handoff

Working directory is D:/Projects/axiom_force_racing. The GitHub repository is configured as origin. No remote HEAD was advertised when checked.

This setup defines separate service ports and storage. The Express base was copied from the existing project at 130f4fb. Game authentication, saves and racing results are future integration work. The placeholder has been retired. The shared website serves the race outline.

The installed Windows Unity CLI is 1.0.1. Its registered Editor is 6000.3.29f1. The official Headless Editor container workflow requires Unity 6000.7 or later, Ubuntu Linux x86-64 and a floating license server. The Windows Student license is active. Use the Windows Unity CLI for project creation, editing, Editor tests and launches. The supported container host, floating license and matching Editor versions remain export prerequisites. The Dockerfile pins Unity's documented base image and reference Editor version. Availability of the pinned Editor and modules still needs a live image build after terms acceptance.

The owner accepted the Unity Editor Software Terms for 6000.3.29f1. The Windows Unity CLI initialized Game/ from the bare 3D URP template and exited successfully. The initial incomplete attempt was preserved at D:/Projects/axiom_force_racing-init-backup-20261123-182935. The game is still an outline, with no story campaign or custom game assets. The Unity project is local and has no Unity Cloud link.

Mount the owner's floating license services-config.json read-only at
/usr/share/unity3d/config/services-config.json when running the Editor.
See https://docs.unity.com/en-us/engine/6000.7/manual/building-and-publishing/docker/run-build.
Docker Hub did not return a manifest from this machine for the documented
1.0.0b2 tag or the 1.0.0 tag listed on the official image page. The Compose
configuration uses 1.0.0. Verify pull access before installing the Editor.

Compose validation passed, the database and API are healthy, the website
proxies /api/health successfully, Chromium smoke passed and all 182 backend
tests passed. The direct pull of unity/editor-base:1.0.0 also returned
not found. Local .env holds isolated development credentials and is ignored.

The Windows Unity CLI installed Ink Unity Integration 2.0.0 through the
official Git UPM source and Unity Pipeline 1.0.1 through the Unity registry.
Tripo DCC Bridge 1.0.14 is a local embedded package from the vendor ZIP at
Game/Packages/com.tripo3d.unitybridge. The vendor files are Git-ignored.
For another checkout, download the official Unity ZIP and place the package
folder at that path. The bridge was not connected and no model was imported.

On this machine the Unity Package Manager could not verify the registry
certificate during Pipeline installation. Passing the trusted Windows root
certificates as a PEM file via process-local NODE_EXTRA_CA_CERTS resolved it.
TLS validation and machine-wide settings were left intact.

## Shared website (2026-11-23)

The sibling `axiom_force_website` checkout serves Drift Circuit at
`http://localhost:5173/en/games/drift-circuit`. The racing placeholder
website and browser image were retired. Run `./tools/website-smoke.ps1`
against the shared site. Racing Express remains on port 8001 and
PostgreSQL on port 5433 with the existing volume. The racing Compose
export profile is unchanged. The shared site connects to the original API.

Racing Compose validation, direct API health, both shared smoke checks and all 182 racing API unit tests passed.
