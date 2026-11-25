using System.Collections.Generic;
using System.IO;
using UnityEngine.Rendering;
using UnityEngine;

namespace DriftCircuit
{
    /// <summary>Runs Track Select, offline time trials, and Quick Race.</summary>
    public sealed class TimeTrial : MonoBehaviour
    {
        public const string ScenePath = "Assets/DriftCircuit/Scenes/LittleTokyoSkyportTimeTrial.unity";

        public const string MenuScenePath = "Assets/DriftCircuit/Scenes/TrackSelect.unity";
        internal const string GhostPreference = "DriftCircuit.ShowGhost";
        internal static string RecordDirectoryOverride { get; set; }

        [SerializeField] private bool startAtTrackSelect;
        private readonly List<Object> owned = new();
        private readonly List<double> checkpointTimes = new();
        private readonly List<GhostPose> poses = new();
        private GameObject raceRoot;
        private TrialRecordStore records;
        private TrialRecord comparison;
        private TrialGhost ghost;
        private RecordResult result;
        private double? checkpointDelta;
        private bool showGhost;
        private TrialInput input;
        private TrialCourse course;
        private TrialKart kart;
        private TrialHud hud;
        private Transform chaseCamera;
        private float previousTimeScale;
        private float shownGo;
        private bool ownsPause;
        private bool resetRequested;
        private bool itemRequested;
        private RelayBoostPickups playerPickups, rivalPickups;
        internal RaceItems Items { get; private set; }
        private LineRenderer playerShield, rivalShield;

        internal TrialKart PlayerKart => kart;
        internal bool ItemsEnabled => playerPickups != null;

        internal CourseDefinition Circuit { get; private set; }
        internal TrialRival Rival { get; private set; }
        internal QuickRaceResult? RaceResult { get; private set; }
        public TrialRules Rules { get; private set; }
        public Rigidbody KartBody => kart?.Body;
        public bool BoostReady => kart?.BoostReady ?? false;
        public bool BoostActive => kart?.BoostActive ?? false;

        /// <summary>Creates the course, kart, view, and HUD from the saved scene entry point.</summary>
        private void Awake()
        {
            input = new TrialInput();
            records = new TrialRecordStore(RecordDirectoryOverride ??
                Path.Combine(Application.persistentDataPath, "time-trial-records"));
            showGhost = PlayerPrefs.GetInt(GhostPreference, 1) != 0;
            hud = new TrialHud(transform, TogglePause, Retry, OpenTrackSelect, StartCircuit,
                definition => StartCircuit(definition, quickRace: true), ToggleGhost);
            if (startAtTrackSelect)
                OpenTrackSelect();
            else
                StartCircuit(CourseDefinition.Tokyo);
        }

        private void StartCircuit(CourseDefinition definition) => StartCircuit(definition, quickRace: false);

        /// <summary>Creates either an item-free trial or a rival race; only Docks Quick Race owns pickups.</summary>
        private void StartCircuit(CourseDefinition definition, bool quickRace)
        {
            Circuit = definition;
            raceRoot = new GameObject(definition.Name);
            raceRoot.transform.SetParent(transform, false);
            course = new TrialCourse(definition, raceRoot.transform, owned);
            course.Build();
            Rules = new TrialRules(definition.Gates, definition.Start);
            kart = BuildKart("Player kart", 0x18a4b9, out _);
            if (quickRace)
            {
                TrialKart rivalKart = BuildKart("Rival kart", 0xf05a27, out Material tailLights);
                Rival = new TrialRival(definition, rivalKart, tailLights);
                if (definition == CourseDefinition.Docks)
                {
                    playerPickups = new RelayBoostPickups();
                    rivalPickups = new RelayBoostPickups();
                    course.BuildRelayGates();
                    Items = new RaceItems(course);
                    playerShield = BuildShield(kart);
                    rivalShield = BuildShield(rivalKart);
                }
            }
            else
                ghost = new TrialGhost(raceRoot.transform, kart.Body.transform, GhostMaterial());
            BuildView();
            hud.ShowCircuit(definition, quickRace);
            BeginCountdown();
            RefreshHud();
        }

        /// <summary>Discards pending gameplay input and releases the current race before showing the menu.</summary>
        private void OpenTrackSelect()
        {
            itemRequested = false;
            Items?.Clear();
            Items = null;
            playerPickups = rivalPickups = null;
            RestoreTimeScale();
            if (raceRoot)
            {
                raceRoot.SetActive(false);
                Destroy(raceRoot);
                ReleaseRaceResources();
            }
            Rules = null;
            Circuit = null;
            kart = null;
            Rival = null;
            RaceResult = null;
            ghost = null;
            comparison = null;
            poses.Clear();
            checkpointTimes.Clear();
            hud.ShowTracks(records, showGhost);
        }

        private void ToggleGhost()
        {
            showGhost = !showGhost;
            PlayerPrefs.SetInt(GhostPreference, showGhost ? 1 : 0);
            PlayerPrefs.Save();
            hud.ShowGhostPreference(showGhost);
        }

        /// <summary>Starts reading devices when the trial is active.</summary>
        private void OnEnable() => input.Enable();

        /// <summary>Restores the caller's time scale if a paused trial is disabled or unloaded.</summary>
        private void OnDisable()
        {
            itemRequested = false;
            input?.Disable();
            if (ownsPause)
            {
                ToggleRacePhases();
                RestoreTimeScale();
            }
        }

        /// <summary>Releases runtime meshes, materials, and actions when leaving the scene.</summary>
        private void OnDestroy()
        {
            input?.Dispose();
            ReleaseRaceResources();
        }

        private void ReleaseRaceResources()
        {
            foreach (Object item in owned)
                Destroy(item);
            owned.Clear();
        }

        /// <summary>Reads frame-edge button actions, including the actions used during pause.</summary>
        private void Update()
        {
            // Latch an edge for exactly one racing physics step. Menu/countdown edges are discarded.
            if (Rules?.Phase != TrialPhase.Racing || !ItemsEnabled || resetRequested)
                itemRequested = false;
            else if (input.Item.WasPressedThisFrame())
                itemRequested = true;
            if (Rules == null || Rules.Phase == TrialPhase.Paused || Rules.Phase == TrialPhase.Finished)
            {
                if (Rules?.Phase == TrialPhase.Paused && input.Drift.WasReleasedThisFrame())
                    kart.CancelDrift();
                if (Rules?.Phase == TrialPhase.Paused && input.Pause.WasPressedThisFrame())
                    TogglePause();
                else
                    hud.Navigate(input.MenuUp.WasPressedThisFrame(), input.MenuDown.WasPressedThisFrame(),
                        input.Confirm.WasPressedThisFrame());
            }
            else if (input.Pause.WasPressedThisFrame())
                TogglePause();
            else if (Rules.Phase == TrialPhase.Racing && input.Reset.WasPressedThisFrame())
            {
                resetRequested = true;
                itemRequested = false;
            }

            if (Rules == null)
                return;
            if (Rules.Phase == TrialPhase.Racing)
                shownGo = Mathf.Max(0f, shownGo - Time.deltaTime);
            RefreshHud();
        }

        private void RefreshHud() => hud.Refresh(Rules, kart, shownGo > 0f, checkpointDelta, result, RaceResult,
            Items?.Incoming(kart) ?? false, Items?.IncomingPulse(kart) ?? false,
            RaceItems.IsInSlipstream(kart, Rival?.Kart));

        /// <summary>Advances countdown or applies the racing step, including respawn and gate checks.</summary>
        private void FixedUpdate()
        {
            if (Rules == null)
                return;
            if (Rules.Phase == TrialPhase.Countdown)
            {
                playerPickups?.Sample(kart, Rules);
                rivalPickups?.Sample(Rival.Kart, Rival.Rules);
                Rules.Tick(Time.fixedDeltaTime, kart.Body.position);
                Rival?.Rules.Tick(Time.fixedDeltaTime, Rival.Kart.Body.position);
                if (Rules.Phase == TrialPhase.Racing)
                {
                    kart.Body.isKinematic = false;
                    if (Rival != null)
                        Rival.Kart.Body.isKinematic = false;
                    else
                        CapturePose();
                    shownGo = 0.75f;
                }
                return;
            }
            if (Rules.Phase != TrialPhase.Racing)
                return;

            bool outOfBounds = kart.OutOfBounds;
            int oldGate = Rules.NextGate;
            int oldLap = Rules.Laps;
            Sample(Rules, kart, outOfBounds);
            if (Rules.NextGate != oldGate || Rules.Laps != oldLap)
            {
                if (Rival == null)
                {
                    checkpointTimes.Add(Rules.Elapsed);
                    if (comparison != null)
                        checkpointDelta = Rules.Elapsed - comparison.Checkpoints[oldLap * Circuit.Gates.Length + oldGate];
                }
                course.ShowExpected(Rules.NextGate);
            }
            if (Rival == null && !outOfBounds)
                CapturePose();
            bool rivalOutOfBounds = false;
            if (Rival?.Rules.Phase == TrialPhase.Racing)
            {
                rivalOutOfBounds = Rival.Kart.OutOfBounds;
                Sample(Rival.Rules, Rival.Kart, rivalOutOfBounds);
                if (Rival.Rules.Phase == TrialPhase.Finished)
                {
                    Items?.RemoveOwned(Rival.Kart);
                    Rival.Retire();
                }
            }
            if (Rules.Phase == TrialPhase.Finished)
            {
                itemRequested = false;
                kart.Freeze();
                if (Rival == null)
                    result = records.SaveResult(Circuit, Rules, checkpointTimes.ToArray(), poses.ToArray());
                else
                {
                    Rival.Kart.Freeze();
                    RaceResult = new QuickRaceResult(Rules.Elapsed,
                        Rival.Rules.Phase == TrialPhase.Finished ? Rival.Rules.Elapsed : null);
                }
                return;
            }

            // Account for legitimate movement before a manual reset, then rebase at the new position.
            if (resetRequested || outOfBounds)
            {
                itemRequested = false;
                if (TryRespawn(kart, Rules, Rival?.Kart, out Vector3 respawn, out Vector3 forward))
                {
                    resetRequested = false;
                    outOfBounds = false;
                    PlaceKart(respawn, forward);
                    Items?.Recover(kart);
                    if (Rival == null)
                        CapturePose(reset: true);
                }
            }

            bool rivalReady = Rival?.Rules.Phase == TrialPhase.Racing;
            if (rivalReady && (rivalOutOfBounds || Rival.NeedsRecovery))
            {
                rivalReady = TryRespawn(Rival.Kart, Rival.Rules, kart, out Vector3 respawn, out Vector3 forward);
                if (rivalReady)
                {
                    Rival.Recover(respawn, forward);
                    Items?.Recover(Rival.Kart);
                }
            }
            // Both recovery attempts precede occupancy checks and all driving input.
            course.UpdateCrossing(Rules.Elapsed, kart,
                Rival?.Rules.Phase == TrialPhase.Racing ? Rival.Kart : null);
            playerPickups?.Sample(kart, Rules);
            rivalPickups?.Sample(Rival.Kart, Rival.Rules);
            float throttle = input.Throttle.ReadValue<float>();
            float brake = input.Brake.ReadValue<float>();
            float steer = input.Steer.ReadValue<float>();
            bool drift = input.Drift.IsPressed();
            if (rivalReady) Rival.Decide(kart, Time.fixedDeltaTime, course.Crossing, ItemsEnabled);
            if (itemRequested && !outOfBounds)
                Items?.Activate(kart, kart.Grounded, brake);
            itemRequested = false;
            if (rivalReady) Rival.ActivateItem(kart, Items);
            if (!outOfBounds) Items?.Resolve(kart);
            if (rivalReady) Items?.Resolve(Rival.Kart);
            if (!outOfBounds)
                kart.Step(throttle, brake, steer, drift, kart.Grounded, Time.fixedDeltaTime, false,
                    RaceItems.IsInSlipstream(kart, Rival?.Kart));
            if (rivalReady)
                Rival.Apply(Time.fixedDeltaTime, false, RaceItems.IsInSlipstream(Rival.Kart, kart));
            kart.AdvanceEffects(Time.fixedDeltaTime);
            if (rivalReady) Rival.Kart.AdvanceEffects(Time.fixedDeltaTime);
            Items?.Advance(Time.fixedDeltaTime);
            if (playerShield) playerShield.enabled = kart.ShieldActive;
            if (rivalShield) rivalShield.enabled = rivalReady && Rival.Kart.ShieldActive;
        }

        private static void Sample(TrialRules rules, TrialKart racer, bool outOfBounds)
        {
            Vector3 position = racer.Body.position;
            if (outOfBounds)
                rules.Rebase(position);
            rules.Tick(Time.fixedDeltaTime, position);
        }

        private bool TryRespawn(TrialKart racer, TrialRules rules, TrialKart other,
            out Vector3 position, out Vector3 forward)
        {
            var respawn = Circuit.Respawn(rules.LastGate);
            position = respawn.Position;
            forward = respawn.Forward;
            Quaternion rotation = Quaternion.LookRotation(forward);
            Vector3 side = Vector3.Cross(Vector3.up, forward) * 2.4f;
            if (!racer.OverlapsAt(position, rotation, other))
                return true;
            position = respawn.Position + side;
            if (!racer.OverlapsAt(position, rotation, other))
                return true;
            position = respawn.Position - side;
            return !racer.OverlapsAt(position, rotation, other);
        }

        private void CapturePose(bool reset = false) =>
            poses.Add(new GhostPose(Rules.Elapsed, kart.Body.position, kart.Body.rotation, reset));

        /// <summary>Follows the interpolated body after frame motion has settled.</summary>
        private void LateUpdate()
        {
            if (Rules == null)
                return;
            if (ghost != null && Rules.Phase != TrialPhase.Paused)
            {
                // Match the one-step interpolation used by the player body.
                double frameOffset = System.Math.Clamp(Time.timeAsDouble - Time.fixedTimeAsDouble, 0, Time.fixedDeltaTime);
                double renderTime = System.Math.Max(0, Rules.Elapsed - Time.fixedDeltaTime + frameOffset);
                ghost.Refresh(comparison, Rules.Elapsed, renderTime, showGhost && Rules.Phase == TrialPhase.Racing);
            }
            float blend = 1f - Mathf.Exp(-8f * Time.deltaTime);
            UpdateCamera(kart.Body.transform.position, kart.Body.transform.forward, blend);
        }

        /// <summary>Freezes physics and race time while leaving menu input responsive.</summary>
        private void TogglePause()
        {
            itemRequested = false;
            ToggleRacePhases();
            if (Rules.Phase == TrialPhase.Paused)
            {
                if (!input.Drift.IsPressed())
                    kart.CancelDrift();
                previousTimeScale = Time.timeScale;
                Time.timeScale = 0f;
                ownsPause = true;
            }
            else
                RestoreTimeScale();
        }

        private void ToggleRacePhases()
        {
            Rules.TogglePause();
            Rival?.Rules.TogglePause();
        }

        /// <summary>Releases the simulation pause owned by this scene.</summary>
        private void RestoreTimeScale()
        {
            if (!ownsPause)
                return;
            Time.timeScale = previousTimeScale;
            ownsPause = false;
        }

        /// <summary>Starts a clean run from an open pause or result screen.</summary>
        private void Retry()
        {
            if (Rules.Phase == TrialPhase.Finished || Rules.Phase == TrialPhase.Paused)
                BeginCountdown();
        }

        /// <summary>Places the kart at the grid and locks its body until GO.</summary>
        private void BeginCountdown()
        {
            RestoreTimeScale();
            resetRequested = false;
            itemRequested = false;
            kart.ClearItems();
            Items?.Clear();
            if (playerShield) playerShield.enabled = false;
            if (rivalShield) rivalShield.enabled = false;
            Rival?.Kart.ClearItems();
            playerPickups?.Restart();
            rivalPickups?.Restart();
            shownGo = 0f;
            comparison = Rival == null ? records.Load(Circuit) : null;
            checkpointDelta = null;
            result = RecordResult.None;
            RaceResult = null;
            checkpointTimes.Clear();
            poses.Clear();
            Vector3 gridSide = Vector3.Cross(Vector3.up, Circuit.StartForward) * TrialRival.GridOffset;
            Vector3 start = Rival == null ? Circuit.Start : Circuit.Start + gridSide;
            Rules.Restart(start);
            PlaceKart(start, Circuit.StartForward);
            Rival?.Restart(Circuit.Start - gridSide, Circuit.StartForward);
            course.UpdateCrossing(Rules.Elapsed, kart, Rival?.Kart);
            ghost?.Refresh(null, 0, 0, false);
            kart.Freeze();
            course.ShowExpected(0);
        }

        /// <summary>Places the kart, rebases gate sampling, and hard-cuts the camera.</summary>
        private void PlaceKart(Vector3 position, Vector3 forward)
        {
            kart.Place(position, forward);
            Rules.Rebase(position);
            UpdateCamera(position, forward, 1f);
        }

        /// <summary>Eases toward the chase position and draws it in if a barrier blocks the view.</summary>
        private void UpdateCamera(Vector3 position, Vector3 forward, float blend)
        {
            Vector3 focus = position + forward * 3.4f + Vector3.up * 0.6f;
            Vector3 desired = position - forward * 8.8f + Vector3.up * 5.8f;
            Vector3 offset = desired - focus;
            if (Physics.SphereCast(focus, 0.28f, offset.normalized, out RaycastHit hit,
                    offset.magnitude, 1 << 0, QueryTriggerInteraction.Ignore))
                desired = focus + offset.normalized * Mathf.Max(0.65f, hit.distance - 0.25f);
            chaseCamera.position = Vector3.Lerp(chaseCamera.position, desired, blend);
            chaseCamera.rotation = Quaternion.Slerp(chaseCamera.rotation,
                Quaternion.LookRotation(focus - chaseCamera.position), blend);
        }

        /// <summary>Creates a primitive kart and the single collider that controls its handling.</summary>
        private TrialKart BuildKart(string name, uint paintColor, out Material lights)
        {
            var obj = new GameObject(name) { layer = 2 };
            obj.transform.SetParent(raceRoot.transform, false);
            var body = obj.AddComponent<Rigidbody>();
            body.mass = 175f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            var collider = obj.AddComponent<BoxCollider>();
            collider.size = new Vector3(1.46f, 0.71f, 2.45f);
            collider.center = new Vector3(0, -0.145f, 0);
            var grip = new PhysicsMaterial("Kart collision")
            {
                dynamicFriction = 0f, staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum
            };
            collider.sharedMaterial = grip;
            owned.Add(grip);

            Material paint = course.Paint(paintColor);
            Material rubber = course.Paint(0x121b26);
            lights = course.Paint(0xffd47b, true);
            course.Cube("Chassis", new(0, -0.12f, 0), new(1.35f, 0.4f, 2.35f), Quaternion.identity, paint, parent: obj.transform);
            course.Cube("Seat", new(0, 0.18f, -0.28f), new(0.78f, 0.42f, 0.84f), Quaternion.identity, rubber, parent: obj.transform);
            course.Cube("Rear lights", new(0, -0.06f, -1.18f), new(1.15f, 0.11f, 0.1f), Quaternion.identity, lights, parent: obj.transform);
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                GameObject tire = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                tire.name = "Tire";
                tire.transform.SetParent(obj.transform, false);
                tire.transform.SetLocalPositionAndRotation(new Vector3(x * 0.70f, -0.25f, z * 0.79f),
                    Quaternion.Euler(0, 0, 90));
                tire.transform.localScale = new Vector3(0.48f, 0.15f, 0.48f);
                tire.GetComponent<Renderer>().sharedMaterial = rubber;
                tire.GetComponent<Collider>().enabled = false;
            }
            return new TrialKart(body, collider);
        }

        /// <summary>Draws a cyan non-solid outline using the course's existing material shader.</summary>
        private LineRenderer BuildShield(TrialKart target)
        {
            var outline = new GameObject("Pulse Shield outline");
            outline.transform.SetParent(target.Body.transform, false);
            var line = outline.AddComponent<LineRenderer>();
            line.sharedMaterial = course.Paint(0x34f4ff, true);
            line.useWorldSpace = false;
            line.loop = true;
            line.widthMultiplier = 0.065f;
            line.positionCount = 4;
            line.SetPositions(new[] { new Vector3(-0.95f, 0, -1.45f), new Vector3(-0.95f, 0, 1.45f),
                new Vector3(0.95f, 0, 1.45f), new Vector3(0.95f, 0, -1.45f) });
            line.enabled = false;
            return line;
        }

        private Material GhostMaterial()
        {
            Material material = course.Paint(0x8ef5ff);
            material.color = new Color(0.56f, 0.96f, 1f, 0.36f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Mode", 2f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.SetShaderPassEnabled("DepthOnly", false);
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        /// <summary>Creates the race camera and directional lighting.</summary>
        private void BuildView()
        {
            var view = new GameObject("Chase camera") { tag = "MainCamera" };
            chaseCamera = view.transform;
            chaseCamera.SetParent(raceRoot.transform, false);
            Camera camera = view.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.07f, 0.10f, 0.17f);
            camera.fieldOfView = 68f;
            camera.nearClipPlane = 0.15f;
            camera.farClipPlane = 230f;
            view.AddComponent<AudioListener>();

            var sun = new GameObject("Circuit light");
            sun.transform.SetParent(raceRoot.transform, false);
            sun.transform.rotation = Quaternion.Euler(52, -30, 0);
            Light light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.84f, 0.91f, 1f);
            light.intensity = 1.6f;
            light.shadows = LightShadows.Soft;
        }
    }
}
