using System.Collections.Generic;
using System.IO;
using UnityEngine.Rendering;
using UnityEngine;

namespace DriftCircuit
{
    /// <summary>Runs the track menu and standalone offline time trials.</summary>
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

        internal CourseDefinition Circuit { get; private set; }
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
            hud = new TrialHud(transform, TogglePause, Retry, OpenTrackSelect, StartCircuit, ToggleGhost);
            if (startAtTrackSelect)
                OpenTrackSelect();
            else
                StartCircuit(CourseDefinition.Tokyo);
        }

        private void StartCircuit(CourseDefinition definition)
        {
            Circuit = definition;
            raceRoot = new GameObject(definition.Name);
            raceRoot.transform.SetParent(transform, false);
            course = new TrialCourse(definition, raceRoot.transform, owned);
            course.Build();
            Rules = new TrialRules(definition.Gates, definition.Start);
            BuildKart();
            BuildView();
            ghost = new TrialGhost(raceRoot.transform, kart.Body.transform, GhostMaterial());
            hud.ShowCircuit(definition);
            BeginCountdown();
            hud.Refresh(Rules, kart, false, checkpointDelta, result);
        }

        private void OpenTrackSelect()
        {
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
            input?.Disable();
            if (ownsPause)
            {
                Rules.TogglePause();
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
                resetRequested = true;

            if (Rules == null)
                return;
            if (Rules.Phase == TrialPhase.Racing)
                shownGo = Mathf.Max(0f, shownGo - Time.deltaTime);
            hud.Refresh(Rules, kart, shownGo > 0f, checkpointDelta, result);
        }

        /// <summary>Advances countdown or applies the racing step, including respawn and gate checks.</summary>
        private void FixedUpdate()
        {
            if (Rules == null)
                return;
            if (Rules.Phase == TrialPhase.Countdown)
            {
                Rules.Tick(Time.fixedDeltaTime, kart.Body.position);
                if (Rules.Phase == TrialPhase.Racing)
                {
                    kart.Body.isKinematic = false;
                    shownGo = 0.75f;
                    CapturePose();
                }
                return;
            }
            if (Rules.Phase != TrialPhase.Racing)
                return;

            Vector3 position = kart.Body.position;
            bool outOfBounds = position.y < -1.35f || position.sqrMagnitude > 125f * 125f;
            if (outOfBounds)
                Rules.Rebase(position);
            int oldGate = Rules.NextGate;
            int oldLap = Rules.Laps;
            Rules.Tick(Time.fixedDeltaTime, position);
            if (Rules.NextGate != oldGate || Rules.Laps != oldLap)
            {
                checkpointTimes.Add(Rules.Elapsed);
                if (comparison != null)
                    checkpointDelta = Rules.Elapsed - comparison.Checkpoints[oldLap * Circuit.Gates.Length + oldGate];
                course.ShowExpected(Rules.NextGate);
            }
            if (!outOfBounds)
                CapturePose();
            if (Rules.Phase == TrialPhase.Finished)
            {
                kart.Body.linearVelocity = Vector3.zero;
                kart.Body.angularVelocity = Vector3.zero;
                kart.Body.isKinematic = true;
                result = records.SaveResult(Circuit, Rules, checkpointTimes.ToArray(), poses.ToArray());
                return;
            }

            // Account for legitimate movement before a manual reset, then rebase at the new position.
            if (resetRequested || outOfBounds)
            {
                resetRequested = false;
                var respawn = Circuit.Respawn(Rules.LastGate);
                PlaceKart(respawn.Position, respawn.Forward);
                CapturePose(reset: true);
            }

            Vector3 groundProbe = kart.Body.position - Vector3.up * 0.20f;
            bool grounded = Physics.Raycast(groundProbe, Vector3.down, 0.55f, 1 << 0, QueryTriggerInteraction.Ignore);
            kart.Step(input.Throttle.ReadValue<float>(), input.Brake.ReadValue<float>(),
                input.Steer.ReadValue<float>(), input.Drift.IsPressed(), grounded, Time.fixedDeltaTime);
        }

        private void CapturePose(bool reset = false) =>
            poses.Add(new GhostPose(Rules.Elapsed, kart.Body.position, kart.Body.rotation, reset));

        /// <summary>Follows the interpolated body after frame motion has settled.</summary>
        private void LateUpdate()
        {
            if (Rules == null)
                return;
            if (Rules.Phase != TrialPhase.Paused)
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
            Rules.TogglePause();
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
            shownGo = 0f;
            comparison = records.Load(Circuit);
            checkpointDelta = null;
            result = RecordResult.None;
            checkpointTimes.Clear();
            poses.Clear();
            Rules.Restart(Circuit.Start);
            PlaceKart(Circuit.Start, Circuit.StartForward);
            ghost.Refresh(null, 0, 0, false);
            kart.Body.isKinematic = true;
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
        private void BuildKart()
        {
            var obj = new GameObject("Player kart") { layer = 2 };
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

            Material paint = course.Paint(0x18a4b9);
            Material rubber = course.Paint(0x121b26);
            Material lights = course.Paint(0xffd47b, true);
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
            kart = new TrialKart(body);
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
