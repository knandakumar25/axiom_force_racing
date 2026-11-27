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
        internal const string BeaconPreference = "DriftCircuit.ShowBeacon";
        internal static string RecordDirectoryOverride { get; set; }
        internal static IRacingSaveStore SaveStoreOverride { get; set; }

        [SerializeField] private bool startAtTrackSelect;
        private readonly List<Object> owned = new();
        private readonly List<double> checkpointTimes = new();
        private readonly List<GhostPose> poses = new();
        private GameObject raceRoot;
        private TrialRecordStore records;
        private IRacingSaveStore saveStore;
        private TrialRecord comparison;
        private TrialGhost ghost;
        private TrialVisualCatalog visuals;
        private RecordResult result;
        private double? checkpointDelta;
        private bool showGhost;
        private bool showBeacon;
        private CourseDefinition.Cup activeCup = CourseDefinition.Cup.City;
        private float authPollTimer;
        private bool authPollBusy;
        private const int TotalFieldRacers = 10;
        private TrialInput input;
        private TrialCourse course;
        private TrialKart kart, player2Kart;
        private TrialRules player2Rules;
        private TrialKart[] playerKarts = System.Array.Empty<TrialKart>();
        private TrialRules[] playerRules = System.Array.Empty<TrialRules>();
        private readonly List<TrialRival> aiRivals = new();
        private readonly List<RelayBoostPickups> aiPickups = new();
        private readonly List<LineRenderer> aiShields = new();
        private TrialHud hud;
        private Transform chaseCamera, chaseCamera2;
        private Transform[] chaseCameras = System.Array.Empty<Transform>();
        private float previousTimeScale;
        private float shownGo;
        private bool ownsPause;
        private readonly bool[] resetRequests = new bool[4];
        private readonly bool[] extraItemRequests = new bool[3];
        private bool resetRequested { get => resetRequests[0]; set => resetRequests[0] = value; }
        private bool itemRequested;
        private bool GetItemRequest(int slot) => slot == 0 ? itemRequested : extraItemRequests[slot - 1];
        private void SetItemRequest(int slot, bool value)
        {
            if (slot == 0) itemRequested = value;
            else extraItemRequests[slot - 1] = value;
        }
        private int playerCount = 1;
        private readonly int[] playerDriverIndices = { 0, 1, 2, 3 };
        private NetRaceClient netClient;
        private readonly Dictionary<int, TrialKart> remoteKarts = new();
        private bool onlineRaceActive;
        private float netSendTimer;
        private RelayBoostPickups playerPickups, rivalPickups;
        private ItemPool playerPool, rivalPool;
        internal RaceItems Items { get; private set; }
        internal IRacingSaveStore SaveStore => saveStore;
        private LineRenderer playerShield, rivalShield;

        internal TrialKart PlayerKart => kart;
        internal TrialKart Player2Kart => player2Kart;
        internal TrialRules Player2Rules => player2Rules;
        internal TrialKart[] PlayerKarts => playerKarts;
        internal int PlayerCount => playerCount;
        internal bool TwoPlayerMode => playerCount >= 2;
        internal DriverProfile P1Driver => DriverProfile.All[playerDriverIndices[0]];
        internal DriverProfile P2Driver => DriverProfile.All[playerDriverIndices[1]];
        internal IReadOnlyList<TrialRival> AiRivals => aiRivals;
        internal bool ItemsEnabled => playerPickups != null;

        internal CourseDefinition Circuit { get; private set; }
        internal TrialRival Rival => aiRivals.Count > 0 ? aiRivals[0] : null;
        internal QuickRaceResult? RaceResult { get; private set; }
        public TrialRules Rules { get; private set; }
        public Rigidbody KartBody => kart?.Body;
        public bool BoostReady => kart?.BoostReady ?? false;
        public bool BoostActive => kart?.BoostActive ?? false;

        /// <summary>Creates the course, kart, view, and HUD from the saved scene entry point.</summary>
        private void Awake()
        {
            visuals = Resources.Load<TrialVisualCatalog>(TrialVisualCatalog.ResourceName);
            input = new TrialInput();
            netClient = new NetRaceClient();
            records = new TrialRecordStore(RecordDirectoryOverride ??
                Path.Combine(Application.persistentDataPath, "time-trial-records"));
            saveStore = SaveStoreOverride ?? new HttpRacingSaveStore();
            showGhost = PlayerPrefs.GetInt(GhostPreference, 1) != 0;
            showBeacon = PlayerPrefs.GetInt(BeaconPreference, 0) != 0;
            hud = new TrialHud(transform, TogglePause, Retry, OpenTrackSelect, StartCircuit,
                definition => StartCircuit(definition, quickRace: true), ToggleGhost, ToggleBeacon,
                ToggleCup, ToggleAuth, ShowLeaderboard, ToggleMode, CycleP1Driver, CycleP2Driver,
                OpenOnlineModal, HostOnlineRoom, JoinOnlineRoom, StartOnlineRace);
            if (startAtTrackSelect)
                OpenTrackSelect();
            else
                StartCircuit(CourseDefinition.Tokyo);
            _ = saveStore.TryRestoreSessionAsync();
        }

        private void StartCircuit(CourseDefinition definition) => StartCircuit(definition, quickRace: false, online: false);
        private void StartCircuit(CourseDefinition definition, bool quickRace) => StartCircuit(definition, quickRace, online: false);

        /// <summary>Creates a solo time trial, 10-racer Quick Race, 2-4P split-screen race, or online race.</summary>
        private void StartCircuit(CourseDefinition definition, bool quickRace, bool online)
        {
            Circuit = definition;
            onlineRaceActive = online;
            raceRoot = new GameObject(definition.Name);
            raceRoot.transform.SetParent(transform, false);
            course = new TrialCourse(definition, raceRoot.transform, owned, visuals);
            course.Build();
            course.ShowRouteBeacon(showBeacon);
            input.SetPlayerCount(playerCount);

            int activePlayers = online ? 1 : playerCount;
            playerKarts = new TrialKart[activePlayers];
            playerRules = new TrialRules[activePlayers];
            for (int p = 0; p < activePlayers; p++)
            {
                DriverProfile driver = DriverProfile.All[playerDriverIndices[p]];
                string kartName = activePlayers == 1 ? "Player kart" : $"Player {p + 1} kart";
                playerKarts[p] = BuildKart(kartName, driver, out _);
                playerRules[p] = new TrialRules(definition.Gates, definition.Start);
            }
            kart = playerKarts[0];
            Rules = playerRules[0];
            player2Kart = activePlayers >= 2 ? playerKarts[1] : null;
            player2Rules = activePlayers >= 2 ? playerRules[1] : null;

            aiRivals.Clear();
            aiPickups.Clear();
            aiShields.Clear();
            remoteKarts.Clear();

            bool spawnField = quickRace || activePlayers > 1 || online;
            bool enableItems = (quickRace || activePlayers > 1) && definition != CourseDefinition.Rustrail && definition != CourseDefinition.Foundry;
            if (enableItems)
            {
                bool randomPool = definition != CourseDefinition.Docks;
                playerPool = randomPool ? new ItemPool(1337) : null;
                rivalPool = randomPool ? new ItemPool(7331) : null;
                playerPickups = new RelayBoostPickups(definition, playerPool);
                rivalPickups = new RelayBoostPickups(definition, rivalPool);
                course.BuildRelayGates();
                Items = new RaceItems(course, definition);
                playerShield = BuildShield(kart);
            }

            if (spawnField)
            {
                int aiCount = online ? 0 : Mathf.Max(0, TotalFieldRacers - activePlayers);
                for (int i = 0; i < aiCount; i++)
                {
                    DriverProfile aiDriver = PickAiDriver(activePlayers, i);
                    TrialKart aiKart = BuildKart(i == 0 ? "Rival kart" : $"Rival {i + 1} kart", aiDriver, out Material tailLights);
                    var rival = new TrialRival(definition, aiKart, tailLights);
                    aiRivals.Add(rival);
                    if (enableItems)
                    {
                        aiPickups.Add(i == 0 ? rivalPickups : new RelayBoostPickups(definition, rivalPool));
                        LineRenderer shield = BuildShield(aiKart);
                        aiShields.Add(shield);
                        if (i == 0) rivalShield = shield;
                    }
                }
                if (enableItems && activePlayers >= 2 && rivalShield == null)
                    rivalShield = BuildShield(player2Kart);

                // Prevent AI-on-AI pileups while preserving human vs rival collisions.
                for (int i = 0; i < aiRivals.Count; i++)
                {
                    Collider colI = aiRivals[i].Kart.Body.GetComponent<Collider>();
                    for (int j = i + 1; j < aiRivals.Count; j++)
                        Physics.IgnoreCollision(colI, aiRivals[j].Kart.Body.GetComponent<Collider>(), true);
                    if (i >= 1)
                    {
                        for (int p = 0; p < playerKarts.Length; p++)
                            Physics.IgnoreCollision(colI, playerKarts[p].Body.GetComponent<Collider>(), true);
                    }
                }
            }
            else
                ghost = new TrialGhost(raceRoot.transform, kart.Body.transform, GhostMaterial());

            BuildView(activePlayers);
            hud.ShowCircuit(definition, quickRace, activePlayers, P1Driver, P2Driver, online);
            BeginCountdown();
            RefreshHud();
        }

        private DriverProfile PickAiDriver(int activePlayers, int aiIndex)
        {
            if (aiIndex == 0 && activePlayers == 1 && P2Driver.Id != P1Driver.Id)
                return P2Driver;
            return DriverProfile.All[(playerDriverIndices[0] + activePlayers + aiIndex) % DriverProfile.All.Length];
        }

        /// <summary>Discards pending gameplay input and releases the current race before showing the menu.</summary>
        private void OpenTrackSelect()
        {
            for (int i = 0; i < 4; i++)
            {
                SetItemRequest(i, false);
                resetRequests[i] = false;
            }
            onlineRaceActive = false;
            Items?.Clear();
            Items = null;
            playerPickups = rivalPickups = null;
            playerPool = rivalPool = null;
            aiRivals.Clear();
            aiPickups.Clear();
            aiShields.Clear();
            remoteKarts.Clear();
            RestoreTimeScale();
            if (raceRoot)
            {
                raceRoot.SetActive(false);
                Destroy(raceRoot);
                ReleaseRaceResources();
            }
            Rules = null;
            player2Rules = null;
            playerRules = System.Array.Empty<TrialRules>();
            playerKarts = System.Array.Empty<TrialKart>();
            Circuit = null;
            kart = null;
            player2Kart = null;
            chaseCamera = chaseCamera2 = null;
            chaseCameras = System.Array.Empty<Transform>();
            RaceResult = null;
            ghost = null;
            comparison = null;
            poses.Clear();
            checkpointTimes.Clear();
            RefreshTrackSelectHud();
        }

        private void RefreshTrackSelectHud() =>
            hud.ShowTracks(records, showGhost, showBeacon, saveStore.StatusText, activeCup,
                saveStore.IsSignedIn, saveStore.SignInPending, playerCount, P1Driver, P2Driver);

        private void ToggleMode()
        {
            playerCount = playerCount >= 4 ? 1 : playerCount + 1;
            input.SetPlayerCount(playerCount);
            RefreshTrackSelectHud();
        }

        private void CycleP1Driver()
        {
            playerDriverIndices[0] = DriverProfile.NextIndex(playerDriverIndices[0]);
            RefreshTrackSelectHud();
        }

        private void CycleP2Driver()
        {
            playerDriverIndices[1] = DriverProfile.NextIndex(playerDriverIndices[1]);
            RefreshTrackSelectHud();
        }

        private void OpenOnlineModal() => hud.ShowOnlineModal(netClient.StatusText);

        private async void HostOnlineRoom()
        {
            CourseDefinition def = activeCup == CourseDefinition.Cup.Skyline ? CourseDefinition.Skyline[0] : CourseDefinition.Tokyo;
            await netClient.HostRoomAsync(def.Id, P1Driver.Id, saveStore.IsSignedIn ? saveStore.Username : P1Driver.DisplayName);
            netClient.DrainUpdates();
            hud.UpdateOnlineStatus(netClient.StatusText);
        }

        private async void JoinOnlineRoom(string code)
        {
            await netClient.JoinRoomAsync(code, P1Driver.Id, saveStore.IsSignedIn ? saveStore.Username : P1Driver.DisplayName);
            netClient.DrainUpdates();
            hud.UpdateOnlineStatus(netClient.StatusText);
        }

        private async void StartOnlineRace()
        {
            await netClient.StartRaceAsync();
            CourseDefinition def = System.Array.Find(CourseDefinition.All, c => c.Id == netClient.RoomCircuit) ?? CourseDefinition.Tokyo;
            StartCircuit(def, quickRace: false, online: true);
        }

        private void ToggleCup()
        {
            activeCup = activeCup == CourseDefinition.Cup.City ? CourseDefinition.Cup.Skyline : CourseDefinition.Cup.City;
            RefreshTrackSelectHud();
        }

        private async void ToggleAuth()
        {
            if (saveStore.IsSignedIn)
            {
                saveStore.SignOut();
            }
            else if (saveStore.SignInPending)
            {
                await saveStore.CancelBrowserSignInAsync();
            }
            else
            {
                string authUrl = await saveStore.StartBrowserSignInAsync();
                if (!string.IsNullOrEmpty(authUrl))
                    Application.OpenURL(authUrl);
            }
            if (Rules == null)
                RefreshTrackSelectHud();
        }

        private async void ShowLeaderboard()
        {
            string board = await saveStore.FetchLeaderboardAsync();
            if (Rules == null)
                hud.ShowLeaderboard(board);
        }

        private void ToggleGhost()
        {
            showGhost = !showGhost;
            PlayerPrefs.SetInt(GhostPreference, showGhost ? 1 : 0);
            PlayerPrefs.Save();
            hud.ShowGhostPreference(showGhost);
            _ = saveStore.SyncSettingsAsync(showGhost, showBeacon);
        }

        private void ToggleBeacon()
        {
            showBeacon = !showBeacon;
            PlayerPrefs.SetInt(BeaconPreference, showBeacon ? 1 : 0);
            PlayerPrefs.Save();
            hud.ShowBeaconPreference(showBeacon);
            _ = saveStore.SyncSettingsAsync(showGhost, showBeacon);
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
            netClient?.Dispose();
            (saveStore as System.IDisposable)?.Dispose();
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
            if (netClient != null)
            {
                netClient.DrainUpdates();
                if (Rules == null)
                    hud.UpdateOnlineStatus(netClient.StatusText);
                if (onlineRaceActive)
                {
                    foreach (var kv in netClient.Remotes)
                    {
                        if (!remoteKarts.TryGetValue(kv.Key, out TrialKart remote))
                        {
                            DriverProfile driver = System.Array.Find(DriverProfile.All, d => d.Id == kv.Value.DriverId)
                                ?? DriverProfile.SuryaPrime;
                            remote = BuildKart($"Remote {kv.Key}", driver, out _);
                            remote.Body.isKinematic = true;
                            remoteKarts[kv.Key] = remote;
                        }
                        remote.Body.transform.position = Vector3.Lerp(remote.Body.transform.position, kv.Value.Position, 0.35f);
                        remote.Body.transform.rotation = Quaternion.Slerp(remote.Body.transform.rotation,
                            Quaternion.Euler(0f, kv.Value.RotationY, 0f), 0.35f);
                    }
                }
            }

            // Latch an edge for exactly one racing physics step. Menu/countdown edges are discarded.
            for (int p = 0; p < playerKarts.Length; p++)
            {
                TrialInput.PlayerSlotActions slot = input.Slot(p);
                if (playerRules[p]?.Phase != TrialPhase.Racing || !ItemsEnabled || resetRequests[p])
                    SetItemRequest(p, false);
                else if (slot.Item.WasPressedThisFrame())
                    SetItemRequest(p, true);
            }

            if (Rules == null || Rules.Phase == TrialPhase.Paused || Rules.Phase == TrialPhase.Finished)
            {
                if (Rules?.Phase == TrialPhase.Paused)
                {
                    for (int p = 0; p < playerKarts.Length; p++)
                    {
                        if (input.Slot(p).Drift.WasReleasedThisFrame())
                            playerKarts[p].CancelDrift();
                    }
                }
                if (Rules?.Phase == TrialPhase.Paused && input.Pause.WasPressedThisFrame())
                    TogglePause();
                else
                    hud.Navigate(input.MenuUp.WasPressedThisFrame(), input.MenuDown.WasPressedThisFrame(),
                        input.Confirm.WasPressedThisFrame());
            }
            else if (input.Pause.WasPressedThisFrame())
                TogglePause();
            else if (Rules.Phase == TrialPhase.Racing)
            {
                for (int p = 0; p < playerKarts.Length; p++)
                {
                    if (playerRules[p]?.Phase == TrialPhase.Racing && input.Slot(p).Reset.WasPressedThisFrame())
                    {
                        resetRequests[p] = true;
                        SetItemRequest(p, false);
                    }
                }
            }

            if (Rules == null)
            {
                if (saveStore.SignInPending && !authPollBusy && (authPollTimer += Time.unscaledDeltaTime) >= 2f)
                {
                    authPollTimer = 0f;
                    _ = PollSignInOnceAsync();
                }
                return;
            }
            if (Rules.Phase == TrialPhase.Racing)
                shownGo = Mathf.Max(0f, shownGo - Time.deltaTime);
            if (onlineRaceActive && Rules.Phase == TrialPhase.Racing && (netSendTimer += Time.deltaTime) >= 0.05f)
            {
                netSendTimer = 0f;
                _ = netClient.SendStateAsync(kart.Body.position, kart.Body.rotation.eulerAngles.y,
                    kart.ForwardSpeed, Rules.Laps, Rules.NextGate, P1Driver.Id);
            }
            RefreshHud();
        }

        private async System.Threading.Tasks.Task PollSignInOnceAsync()
        {
            authPollBusy = true;
            try
            {
                if (await saveStore.PollBrowserSignInAsync() && Rules == null)
                    RefreshTrackSelectHud();
            }
            finally { authPollBusy = false; }
        }

        private void RefreshHud()
        {
            if (playerKarts.Length > 1)
            {
                hud.RefreshMultiPlayer(playerRules, playerKarts, shownGo > 0f, RaceResult,
                    Items?.Incoming(kart) ?? false, Items?.IncomingPulse(kart) ?? false,
                    RaceItems.IsInSlipstream(kart, player2Kart),
                    Items?.Incoming(player2Kart) ?? false, Items?.IncomingPulse(player2Kart) ?? false,
                    RaceItems.IsInSlipstream(player2Kart, kart));
            }
            else
            {
                hud.Refresh(Rules, kart, shownGo > 0f, checkpointDelta, result, RaceResult,
                    Items?.Incoming(kart) ?? false, Items?.IncomingPulse(kart) ?? false,
                    RaceItems.IsInSlipstream(kart, Rival?.Kart));
            }
        }

        /// <summary>Advances countdown or applies the racing step, including respawn and gate checks.</summary>
        private void FixedUpdate()
        {
            if (Rules == null)
                return;
            if (Rules.Phase == TrialPhase.Countdown)
            {
                playerPickups?.Sample(kart, Rules);
                for (int i = 0; i < aiRivals.Count; i++)
                {
                    if (i < aiPickups.Count)
                        aiPickups[i]?.Sample(aiRivals[i].Kart, aiRivals[i].Rules);
                }
                for (int p = 0; p < playerKarts.Length; p++)
                    playerRules[p].Tick(Time.fixedDeltaTime, playerKarts[p].Body.position);
                for (int i = 0; i < aiRivals.Count; i++)
                    aiRivals[i].Rules.Tick(Time.fixedDeltaTime, aiRivals[i].Kart.Body.position);

                if (Rules.Phase == TrialPhase.Racing)
                {
                    for (int p = 0; p < playerKarts.Length; p++)
                        playerKarts[p].Body.isKinematic = false;
                    for (int i = 0; i < aiRivals.Count; i++)
                        aiRivals[i].Kart.Body.isKinematic = false;
                    if (playerKarts.Length == 1 && aiRivals.Count == 0 && !onlineRaceActive)
                        CapturePose();
                    shownGo = 0.75f;
                }
                return;
            }
            if (Rules.Phase != TrialPhase.Racing)
                return;

            bool soloTrial = playerKarts.Length == 1 && aiRivals.Count == 0 && !onlineRaceActive;
            bool[] pOutOfBounds = new bool[playerKarts.Length];
            for (int p = 0; p < playerKarts.Length; p++)
            {
                if (playerRules[p].Phase != TrialPhase.Racing)
                    continue;
                pOutOfBounds[p] = playerKarts[p].OutOfBounds;
                int oldGate = playerRules[p].NextGate;
                int oldLap = playerRules[p].Laps;
                Sample(playerRules[p], playerKarts[p], pOutOfBounds[p]);
                if (p == 0 && (playerRules[0].NextGate != oldGate || playerRules[0].Laps != oldLap))
                {
                    if (soloTrial)
                    {
                        checkpointTimes.Add(Rules.Elapsed);
                        if (comparison != null)
                            checkpointDelta = Rules.Elapsed - comparison.Checkpoints[oldLap * Circuit.Gates.Length + oldGate];
                    }
                    course.ShowExpected(Rules.NextGate);
                }
                if (p > 0 && playerRules[p].Phase == TrialPhase.Finished)
                {
                    Items?.RemoveOwned(playerKarts[p]);
                    playerKarts[p].Freeze();
                }
            }
            if (soloTrial && !pOutOfBounds[0])
                CapturePose();

            bool[] aiOutOfBounds = new bool[aiRivals.Count];
            for (int i = 0; i < aiRivals.Count; i++)
            {
                TrialRival ai = aiRivals[i];
                if (ai.Rules.Phase != TrialPhase.Racing)
                    continue;
                aiOutOfBounds[i] = ai.Kart.OutOfBounds;
                Sample(ai.Rules, ai.Kart, aiOutOfBounds[i]);
                if (ai.Rules.Phase == TrialPhase.Finished)
                {
                    Items?.RemoveOwned(ai.Kart);
                    ai.Retire();
                }
            }

            if (Rules.Phase == TrialPhase.Finished)
            {
                for (int p = 0; p < playerKarts.Length; p++)
                {
                    SetItemRequest(p, false);
                    playerKarts[p].Freeze();
                }
                if (soloTrial)
                {
                    result = records.SaveResult(Circuit, Rules, checkpointTimes.ToArray(), poses.ToArray(),
                        P1Driver.Id, P1Driver.DisplayName);
                    if (result != RecordResult.SaveFailed)
                    {
                        TrialRecord latest = records.Load(Circuit);
                        if (latest != null) _ = saveStore.SyncRecordAsync(Circuit, latest);
                    }
                }
                else
                {
                    for (int i = 0; i < aiRivals.Count; i++)
                        aiRivals[i].Kart.Freeze();
                    double? secondTime = playerKarts.Length >= 2
                        ? (playerRules[1].Phase == TrialPhase.Finished ? playerRules[1].Elapsed : null)
                        : (Rival != null && Rival.Rules.Phase == TrialPhase.Finished ? Rival.Rules.Elapsed : null);
                    RaceResult = new QuickRaceResult(Rules.Elapsed, secondTime);
                }
                return;
            }

            TrialKart primaryOther = playerKarts.Length >= 2 ? player2Kart : Rival?.Kart;
            for (int p = 0; p < playerKarts.Length; p++)
            {
                if (playerRules[p].Phase != TrialPhase.Racing)
                    continue;
                if (resetRequests[p] || pOutOfBounds[p])
                {
                    SetItemRequest(p, false);
                    TrialKart respawnOther = p == 0 ? primaryOther : kart;
                    if (TryRespawn(playerKarts[p], playerRules[p], respawnOther, out Vector3 respawn, out Vector3 forward))
                    {
                        resetRequests[p] = false;
                        pOutOfBounds[p] = false;
                        PlaceKartForSlot(p, respawn, forward);
                        Items?.Recover(playerKarts[p]);
                        if (p == 0 && soloTrial)
                            CapturePose(reset: true);
                    }
                }
            }

            bool[] aiReady = new bool[aiRivals.Count];
            for (int i = 0; i < aiRivals.Count; i++)
            {
                TrialRival ai = aiRivals[i];
                aiReady[i] = ai.Rules.Phase == TrialPhase.Racing;
                if (aiReady[i] && (aiOutOfBounds[i] || ai.NeedsRecovery))
                {
                    aiReady[i] = TryRespawn(ai.Kart, ai.Rules, kart, out Vector3 respawn, out Vector3 forward);
                    if (aiReady[i])
                    {
                        ai.Recover(respawn, forward);
                        Items?.Recover(ai.Kart);
                    }
                }
            }

            // Both recovery attempts precede occupancy checks and all driving input.
            TrialKart activeOther = playerKarts.Length >= 2 && playerRules[1].Phase == TrialPhase.Racing
                ? player2Kart
                : (aiRivals.Count > 0 && aiReady[0] ? aiRivals[0].Kart : null);
            course.UpdateCrossing(Rules.Elapsed, kart, activeOther);

            bool p1Trailing = activeOther != null && ((playerKarts.Length >= 2 &&
                (Rules.Laps * 10 + Rules.NextGate) < (player2Rules.Laps * 10 + player2Rules.NextGate))
                || (Rival != null && (Rules.Laps * 10 + Rules.NextGate) < (Rival.Rules.Laps * 10 + Rival.Rules.NextGate)));
            playerPickups?.Sample(kart, Rules, p1Trailing);
            for (int i = 0; i < aiRivals.Count; i++)
            {
                if (aiReady[i] && i < aiPickups.Count && aiPickups[i] != null)
                {
                    bool aiTrailing = (aiRivals[i].Rules.Laps * 10 + aiRivals[i].Rules.NextGate) < (Rules.Laps * 10 + Rules.NextGate);
                    aiPickups[i].Sample(aiRivals[i].Kart, aiRivals[i].Rules, aiTrailing);
                }
            }

            for (int i = 0; i < aiRivals.Count; i++)
            {
                if (aiReady[i])
                    aiRivals[i].Decide(kart, Time.fixedDeltaTime, course.Crossing, ItemsEnabled);
            }

            for (int p = 0; p < playerKarts.Length; p++)
            {
                if (playerRules[p].Phase == TrialPhase.Racing && GetItemRequest(p) && !pOutOfBounds[p])
                {
                    float pBrake = input.Slot(p).Brake.ReadValue<float>();
                    Items?.Activate(playerKarts[p], playerKarts[p].Grounded, pBrake);
                }
                SetItemRequest(p, false);
            }

            for (int i = 0; i < aiRivals.Count; i++)
            {
                if (aiReady[i]) aiRivals[i].ActivateItem(kart, Items);
            }
            for (int p = 0; p < playerKarts.Length; p++)
            {
                if (playerRules[p].Phase == TrialPhase.Racing && !pOutOfBounds[p])
                    Items?.Resolve(playerKarts[p]);
            }
            for (int i = 0; i < aiRivals.Count; i++)
            {
                if (aiReady[i]) Items?.Resolve(aiRivals[i].Kart);
            }

            for (int p = 0; p < playerKarts.Length; p++)
            {
                if (playerRules[p].Phase != TrialPhase.Racing || pOutOfBounds[p])
                    continue;
                TrialInput.PlayerSlotActions slot = input.Slot(p);
                float pThrottle = slot.Throttle.ReadValue<float>();
                float pBrake = slot.Brake.ReadValue<float>();
                float pSteer = slot.Steer.ReadValue<float>();
                bool pDrift = slot.Drift.IsPressed();
                TrialKart draftTarget = p == 0 ? activeOther : kart;
                playerKarts[p].Step(pThrottle, pBrake, pSteer, pDrift, playerKarts[p].Grounded,
                    Time.fixedDeltaTime, false, RaceItems.IsInSlipstream(playerKarts[p], draftTarget));
                playerKarts[p].AdvanceEffects(Time.fixedDeltaTime);
            }
            for (int i = 0; i < aiRivals.Count; i++)
            {
                if (aiReady[i])
                {
                    aiRivals[i].Apply(Time.fixedDeltaTime, false, RaceItems.IsInSlipstream(aiRivals[i].Kart, kart));
                    aiRivals[i].Kart.AdvanceEffects(Time.fixedDeltaTime);
                }
            }
            Items?.Advance(Time.fixedDeltaTime);
            if (playerShield) playerShield.enabled = kart.ShieldActive;
            if (rivalShield)
                rivalShield.enabled = (playerKarts.Length >= 2 && player2Kart.ShieldActive)
                    || (aiRivals.Count > 0 && aiReady[0] && aiRivals[0].Kart.ShieldActive);
            for (int i = 1; i < aiShields.Count; i++)
            {
                if (aiShields[i])
                    aiShields[i].enabled = i < aiRivals.Count && aiReady[i] && aiRivals[i].Kart.ShieldActive;
            }
            bool anyBeacon = showBeacon || kart.BeaconActive || (player2Kart != null && player2Kart.BeaconActive);
            course.ShowRouteBeacon(anyBeacon);
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
            for (int p = 0; p < chaseCameras.Length && p < playerKarts.Length; p++)
            {
                if (chaseCameras[p] && playerKarts[p] != null)
                    UpdateCamera(chaseCameras[p], playerKarts[p].Body.transform.position,
                        playerKarts[p].Body.transform.forward, blend);
            }
        }

        /// <summary>Freezes physics and race time while leaving menu input responsive.</summary>
        private void TogglePause()
        {
            for (int i = 0; i < 4; i++) SetItemRequest(i, false);
            ToggleRacePhases();
            if (Rules.Phase == TrialPhase.Paused)
            {
                for (int p = 0; p < playerKarts.Length; p++)
                {
                    if (!input.Slot(p).Drift.IsPressed())
                        playerKarts[p].CancelDrift();
                }
                previousTimeScale = Time.timeScale;
                Time.timeScale = 0f;
                ownsPause = true;
            }
            else
                RestoreTimeScale();
        }

        private void ToggleRacePhases()
        {
            for (int p = 0; p < playerRules.Length; p++)
                playerRules[p].TogglePause();
            for (int i = 0; i < aiRivals.Count; i++)
                aiRivals[i].Rules.TogglePause();
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

        /// <summary>Places the karts on a 2-wide staggered grid and locks bodies until GO.</summary>
        private void BeginCountdown()
        {
            RestoreTimeScale();
            for (int i = 0; i < 4; i++)
            {
                resetRequests[i] = false;
                SetItemRequest(i, false);
            }
            for (int p = 0; p < playerKarts.Length; p++)
                playerKarts[p].ClearItems();
            Items?.Clear();
            if (playerShield) playerShield.enabled = false;
            if (rivalShield) rivalShield.enabled = false;
            for (int i = 0; i < aiShields.Count; i++)
                if (aiShields[i]) aiShields[i].enabled = false;
            for (int i = 0; i < aiRivals.Count; i++)
                aiRivals[i].Kart.ClearItems();
            playerPickups?.Restart();
            rivalPickups?.Restart();
            for (int i = 0; i < aiPickups.Count; i++)
                aiPickups[i]?.Restart();
            playerPool?.Reset();
            rivalPool?.Reset();
            shownGo = 0f;
            comparison = playerKarts.Length == 1 && aiRivals.Count == 0 && !onlineRaceActive ? records.Load(Circuit) : null;
            checkpointDelta = null;
            result = RecordResult.None;
            RaceResult = null;
            checkpointTimes.Clear();
            poses.Clear();

            Vector3 gridSide = Vector3.Cross(Vector3.up, Circuit.StartForward) * TrialRival.GridOffset;
            bool multiGrid = playerKarts.Length > 1 || aiRivals.Count > 0;
            for (int p = 0; p < playerKarts.Length; p++)
            {
                Vector3 pos = !multiGrid ? Circuit.Start : GridPosition(p, gridSide);
                playerRules[p].Restart(pos);
                PlaceKartForSlot(p, pos, Circuit.StartForward);
                playerKarts[p].Freeze();
            }
            for (int i = 0; i < aiRivals.Count; i++)
            {
                int gridSlot = playerKarts.Length + i;
                Vector3 pos = GridPosition(gridSlot, gridSide);
                aiRivals[i].Restart(pos, Circuit.StartForward);
            }
            course.UpdateCrossing(Rules.Elapsed, kart, playerKarts.Length >= 2 ? player2Kart : Rival?.Kart);
            ghost?.Refresh(null, 0, 0, false);
            course.ShowExpected(0);
        }

        private Vector3 GridPosition(int slotIndex, Vector3 gridSide)
        {
            int row = slotIndex / 2;
            float side = slotIndex % 2 == 0 ? 1f : -1f;
            return Circuit.Start + gridSide * side - Circuit.StartForward * (row * 3.8f);
        }

        /// <summary>Places a player's kart, rebases gate sampling, and hard-cuts its camera.</summary>
        private void PlaceKartForSlot(int slot, Vector3 position, Vector3 forward)
        {
            playerKarts[slot].Place(position, forward);
            playerRules[slot].Rebase(position);
            if (slot < chaseCameras.Length && chaseCameras[slot])
                UpdateCamera(chaseCameras[slot], position, forward, 1f);
        }

        /// <summary>Eases toward the chase position and draws it in if a barrier blocks the view.</summary>
        private static void UpdateCamera(Transform cam, Vector3 position, Vector3 forward, float blend)
        {
            if (!cam) return;
            Vector3 focus = position + forward * 3.4f + Vector3.up * 0.6f;
            Vector3 desired = position - forward * 8.8f + Vector3.up * 5.8f;
            Vector3 offset = desired - focus;
            if (Physics.SphereCast(focus, 0.28f, offset.normalized, out RaycastHit hit,
                    offset.magnitude, 1 << 0, QueryTriggerInteraction.Ignore))
                desired = focus + offset.normalized * Mathf.Max(0.65f, hit.distance - 0.25f);
            cam.position = Vector3.Lerp(cam.position, desired, blend);
            cam.rotation = Quaternion.Slerp(cam.rotation,
                Quaternion.LookRotation(focus - cam.position), blend);
        }

        /// <summary>Creates the shared kart physics and applies the selected driver's visual model.</summary>
        private TrialKart BuildKart(string name, DriverProfile driver, out Material lights)
        {
            driver ??= DriverProfile.Default;
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

            lights = course.Paint(0xffd47b, true);
            if (visuals && visuals.TryPlace(driver.Chassis.ToString(), obj.transform, new(0, 0.06f, 0),
                new(1.49f, 1.12f, 2.44f), Quaternion.identity, "Driver vehicle", out GameObject model))
            {
                BuildTailLamps(driver, model.transform, lights);
            }
            else
            {
                BuildPrimitiveKart(driver, obj.transform, lights);
            }
            return new TrialKart(body, collider, driver);
        }

        /// <summary>Mounts two tail lamps on the fitted body's rear edge with the driver's accent color.</summary>
        private void BuildTailLamps(DriverProfile driver, Transform model, Material lights)
        {
            Transform kartRoot = model.parent;
            Material accent = course.Paint(driver.AccentColor);
            foreach (string name in new[] { "Left tail lamp mount", "Right tail lamp mount" })
            {
                Transform mount = model.Find(name);
                if (!mount) continue;
                Vector3 point = kartRoot.InverseTransformPoint(mount.position);
                course.Cube("Tail lamp trim", point, new(0.22f, 0.086f, 0.054f),
                    Quaternion.identity, accent, parent: kartRoot);
                course.Cube(name == "Left tail lamp mount" ? "Rear lights" : "Right tail lamp",
                    point - Vector3.forward * 0.026f, new(0.17f, 0.050f, 0.021f),
                    Quaternion.identity, lights, parent: kartRoot);
            }
        }

        /// <summary>Provides the original kart visuals when the local art catalog is absent.</summary>
        private void BuildPrimitiveKart(DriverProfile driver, Transform parent, Material lights)
        {
            Material paint = course.Paint(driver.PrimaryColor);
            Material accent = course.Paint(driver.AccentColor);
            Material rubber = course.Paint(0x121b26);
            course.Cube("Chassis", new(0, -0.12f, 0), new(1.35f, 0.4f, 2.35f), Quaternion.identity, paint, parent: parent);
            course.Cube("Seat", new(0, 0.18f, -0.28f), new(0.78f, 0.42f, 0.84f), Quaternion.identity, rubber, parent: parent);
            course.Cube("Rear lights", new(0, -0.06f, -1.18f), new(1.15f, 0.11f, 0.1f), Quaternion.identity, lights, parent: parent);
            BuildDriverParts(driver.Chassis, parent, accent);
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                GameObject tire = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                tire.name = "Tire";
                tire.transform.SetParent(parent, false);
                tire.transform.SetLocalPositionAndRotation(new Vector3(x * 0.70f, -0.25f, z * 0.79f),
                    Quaternion.Euler(0, 0, 90));
                tire.transform.localScale = new Vector3(0.48f, 0.15f, 0.48f);
                tire.GetComponent<Renderer>().sharedMaterial = rubber;
                tire.GetComponent<Collider>().enabled = false;
            }
        }

        private void BuildDriverParts(ChassisStyle style, Transform parent, Material accent)
        {
            switch (style)
            {
                case ChassisStyle.KatanaFin:
                    course.Cube("Katana Fin L", new(-0.48f, 0.28f, -0.95f), new(0.08f, 0.34f, 0.45f), Quaternion.Euler(-15, 0, 0), accent, parent: parent);
                    course.Cube("Katana Fin R", new(0.48f, 0.28f, -0.95f), new(0.08f, 0.34f, 0.45f), Quaternion.Euler(-15, 0, 0), accent, parent: parent);
                    break;
                case ChassisStyle.SolarRam:
                    course.Cube("Solar Ram", new(0, -0.10f, 1.24f), new(1.42f, 0.28f, 0.26f), Quaternion.identity, accent, parent: parent);
                    course.Cube("Solar Spine", new(0, 0.14f, 0.55f), new(0.32f, 0.14f, 1.15f), Quaternion.identity, accent, parent: parent);
                    break;
                case ChassisStyle.HydroFoil:
                    course.Cube("Hydro Pod L", new(-0.74f, -0.06f, 0f), new(0.16f, 0.20f, 1.45f), Quaternion.identity, accent, parent: parent);
                    course.Cube("Hydro Pod R", new(0.74f, -0.06f, 0f), new(0.16f, 0.20f, 1.45f), Quaternion.identity, accent, parent: parent);
                    break;
                case ChassisStyle.PhantomDart:
                    course.Cube("Dart Nose", new(0, -0.06f, 1.26f), new(0.55f, 0.22f, 0.48f), Quaternion.identity, accent, parent: parent);
                    course.Cube("Stealth Wing", new(0, 0.26f, -1.04f), new(1.30f, 0.08f, 0.32f), Quaternion.identity, accent, parent: parent);
                    break;
                case ChassisStyle.TitanCoil:
                    course.Cube("EM Bumper", new(0, -0.12f, 1.22f), new(1.48f, 0.34f, 0.28f), Quaternion.identity, accent, parent: parent);
                    course.Cube("Coil Pod L", new(-0.66f, 0.14f, -0.45f), new(0.24f, 0.28f, 0.58f), Quaternion.identity, accent, parent: parent);
                    course.Cube("Coil Pod R", new(0.66f, 0.14f, -0.45f), new(0.24f, 0.28f, 0.58f), Quaternion.identity, accent, parent: parent);
                    break;
                case ChassisStyle.PrismWing:
                    course.Cube("Prism Wing", new(0, 0.44f, -0.96f), new(1.36f, 0.07f, 0.36f), Quaternion.Euler(10, 0, 0), accent, parent: parent);
                    course.Cube("Prism Strut", new(0, 0.22f, -0.96f), new(0.18f, 0.36f, 0.22f), Quaternion.identity, accent, parent: parent);
                    break;
            }
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

        /// <summary>Creates 1-4 split-screen race cameras and directional lighting.</summary>
        private void BuildView(int activePlayers)
        {
            chaseCameras = new Transform[activePlayers];
            Rect[] rects = activePlayers switch
            {
                2 => new[] { new Rect(0f, 0f, 0.5f, 1f), new Rect(0.5f, 0f, 0.5f, 1f) },
                3 => new[] { new Rect(0f, 0.5f, 0.5f, 0.5f), new Rect(0.5f, 0.5f, 0.5f, 0.5f), new Rect(0f, 0f, 0.5f, 0.5f) },
                4 => new[] { new Rect(0f, 0.5f, 0.5f, 0.5f), new Rect(0.5f, 0.5f, 0.5f, 0.5f), new Rect(0f, 0f, 0.5f, 0.5f), new Rect(0.5f, 0f, 0.5f, 0.5f) },
                _ => new[] { new Rect(0f, 0f, 1f, 1f) }
            };
            for (int p = 0; p < activePlayers; p++)
            {
                var view = new GameObject(p == 0 ? "Chase camera" : $"Chase camera P{p + 1}");
                if (p == 0) view.tag = "MainCamera";
                chaseCameras[p] = view.transform;
                chaseCameras[p].SetParent(raceRoot.transform, false);
                Camera cam = view.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.07f, 0.10f, 0.17f);
                cam.fieldOfView = 68f;
                cam.nearClipPlane = 0.15f;
                cam.farClipPlane = 230f;
                cam.rect = rects[p];
                if (p == 0)
                    view.AddComponent<AudioListener>();
            }
            chaseCamera = chaseCameras[0];
            chaseCamera2 = activePlayers >= 2 ? chaseCameras[1] : null;

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
