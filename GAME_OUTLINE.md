# Axiom Force: Drift Circuit

An original 3D arcade kart racer set in Los Angeles in 2157. The Axiom Force setting supplies the drivers and racing locations. The game has no story campaign, dialogue or narrative cutscenes.

Choose a driver, race three laps, build boost by drifting, and decide when to use a pickup or take a shortcut. Finish on a results screen. Cups group circuits by theme and difficulty. The circuits and items below are design candidates.

## Circuit roster

| Cup | Circuit | Driving challenge | Route choice |
|---|---|---|---|
| City Cup | **Little Tokyo–Skyport** | The lantern market teaches tight turns before the route opens into broad Skyport lanes. | A maintenance lane trims the final bend if you carry enough speed into it. |
| City Cup | **Concrete Wards Viaduct** | Linked bends beneath elevated roads test drift transitions. | The narrow service tunnel cuts distance while the boulevard offers a better exit angle. |
| City Cup | **Stratos Docks** | Long waterfront straights lead into wide loading-yard chicanes. | A quay-side lane saves a turn but leaves less room to recover from a bad entry. |
| City Cup | **Rustrail Junction** | Sweeping rail-yard corners give way to a compact switchback. | A clearly signaled crossing opens a quicker line on a predictable schedule. |
| Skyline Cup | **Foundry Forge** | A banked factory loop mixes high-speed arcs with slower maintenance turns. | A timed gate offers a short bypass. The main route stays open on every lap. |
| Skyline Cup | **Cyber Grave Circuit** | A winding salvage-yard course rewards early braking and clean exits. | A steep ramp skips a hairpin if you approach straight and fast enough. |
| Skyline Cup | **Spire Outer Hull** | A rising spiral alternates wide drift arcs and tighter service bends. | An open and clearly marked transfer ramp leads to a lower lane with a narrow landing. |
| Skyline Cup | **Apex Bastion Descent** | A fortress climb sets up a long downhill section with blind crests and generous runoff. | A high line preserves speed. The shorter inner line requires precise braking. |

Start with Little Tokyo–Skyport as a time trial. Add Concrete Wards and Stratos Docks next to test technical and high-speed layouts against the same handling model. Finish the City Cup before prototyping the Skyline Cup. Size lanes, jumps and braking zones from measured kart handling. Mark each fork early enough for players to choose a line and make both branches rejoin through valid checkpoints.

## Power-up candidates

| Item | Effect | Counterplay or tradeoff |
|---|---|---|
| **Relay Boost** | Gives a short burst of acceleration. | Timing matters. Save it for a straight, a corner exit or a shortcut entry. |
| **Pulse Shield** | Absorbs one disruption within a short window. | A visible countdown makes it possible to wait out the shield. |
| **Flux Ribbon** | Leaves a small marked patch that briefly reduces grip. | Drivers can steer around it or absorb it with a shield. The patch expires quickly. |
| **Signal Tap** | Sends a short, visible pulse straight ahead. The first rival it reaches loses some acceleration briefly. | The pulse has limited range and no tracking. Change lanes or shield. |
| **Slipstream Link** | Helps a trailing kart close the gap when it holds a line behind a rival. | The assist ends when the driver leaves the draft. |
| **Route Beacon** | Highlights an upcoming optional route and its entry point. | It gives information only. Trial it as a practice aid before using a pickup slot for it. |

Prototype Relay Boost, Pulse Shield and Flux Ribbon first. Add Signal Tap and Slipstream Link only after the first three feel good together. Keep Route Beacon separate until playtests show where route guidance is needed.

- Place pickup gates across multiple lanes so racers have a choice without being forced into one line. Each kart holds one item and activates it with one input. Crossing another gate while full preserves the held item.
- Start with a shared item pool. If trailing drivers consistently stop gaining ground, try weighting them toward Relay Boost or Slipstream Link. Tune from race results, with no guaranteed first-place hit.
- Warn drivers about disruptions with a distinct icon, shape and sound. Make them brief, preserve steering, and give a short recovery window before another disruption can take effect. Keep traps away from mandatory landings, checkpoint gates and respawn points.
- Disable random pickups in time trials. Keep drift boost and fixed track features so lap times can be compared.

## First playable milestone

One kart handling model, one circuit, time trial, ordered checkpoints, safe resets and a finish result. Test that skipping checkpoints never counts a lap and that every intended route can complete a valid lap. Add AI opponents and pickups after driving and lap timing are consistent. In item races, check that shields counter disruptions, repeated hits leave time to recover, and shortcuts reward execution.

Later decisions include driver roster and vehicle differences, local multiplayer, online features and art direction. Windows, Linux, Web, Android, macOS and iOS remain target platforms.
