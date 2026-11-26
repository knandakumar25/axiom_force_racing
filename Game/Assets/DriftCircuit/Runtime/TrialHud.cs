using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DriftCircuit
{
    /// <summary>Displays timing, handling, countdown, and the pause and result cards.</summary>
    internal sealed class TrialHud
    {
        internal static string QuickRaceName(CourseDefinition circuit) => "Quick Race · " + circuit.Name;
        private static readonly Color Ink = new(0.035f, 0.07f, 0.11f, 0.94f);
        private static readonly Color Aqua = new(0.4f, 1f, 0.85f);
        private static readonly Color Gold = new(1f, 0.8f, 0.4f);
        private readonly Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        private readonly Text circuit, lap, timer, nextGate, delta, boost, speed, banner, modalTitle, modalBody, recordStatus;
        private readonly GameObject menu, raceHud, modal;
        private readonly EventSystem events;
        private readonly Button resume;
        private readonly Button[] trackButtons, pauseButtons, resultButtons;
        private readonly Text[] trackRecords, trackNames, quickNames;
        private readonly Button[] trialButtons, quickButtons;
        private readonly Text ghostLabel, beaconLabel, apiStatusLabel, cupLabel, authLabel, boardBody;
        private readonly Text modeLabel, p1DriverLabel, p2DriverLabel, driverStatsLabel;
        private readonly GameObject boardModal, onlineModal;
        private readonly GameObject splitDivider, splitHDivider, p2HudRoot, p2ItemPanel, p3HudRoot, p4HudRoot;
        private readonly Text p2Lap, p2Timer, p2NextGate, p2Speed, p2Boost, p2Item, p2Effects;
        private readonly Text p3Lap, p3Speed, p4Lap, p4Speed, onlineStatusLabel;
        private readonly InputField joinCodeInput;
        private readonly RectTransform raceInfoRect, kartInfoRect, itemPanelRect, controlsRect;
        private readonly Text controlsKeyboard, controlsGamepad;
        private CourseDefinition[] currentCircuits = CourseDefinition.All;
        private Button[] activeButtons;
        private int selected;
        private bool quickRace;
        private int playerCount = 1;
        private DriverProfile p1Driver = DriverProfile.CyberShogun;
        private DriverProfile p2Driver = DriverProfile.SuryaPrime;
        private readonly Text item, itemBinding, effects;
        private readonly GameObject itemPanel;
        private TrialPhase previousPhase = (TrialPhase)(-1);

        /// <summary>Creates the screen-space HUD and binds its pointer buttons.</summary>
        public TrialHud(Transform parent, Action onResume, Action onRetry, Action onTrackSelect,
            Action<CourseDefinition> onStart, Action<CourseDefinition> onQuickRace, Action onGhostToggle,
            Action onBeaconToggle = null, Action onCupToggle = null, Action onAuthAction = null, Action onLeaderboard = null,
            Action onModeToggle = null, Action onP1DriverCycle = null, Action onP2DriverCycle = null,
            Action onOnlineOpen = null, Action onOnlineHost = null, Action<string> onOnlineJoin = null, Action onOnlineStart = null)
        {
            var screen = new GameObject("Time trial HUD", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            screen.transform.SetParent(parent, false);
            screen.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = screen.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            RectTransform canvas = (RectTransform)screen.transform;

            var topLeft = new Vector2(0, 1);
            var bottomLeft = Vector2.zero;
            var bottomRight = new Vector2(1, 0);
            var center = Vector2.one * 0.5f;
            RectTransform raceCanvas = Fill("Race HUD", canvas, Color.clear);
            raceHud = raceCanvas.gameObject;
            raceInfoRect = Panel("Race information", raceCanvas, topLeft, new(24, -22), new(315, 170), Ink);
            circuit = Label("Circuit", raceInfoRect, topLeft, new(17, -9), new(285, 28), 18, Aqua);
            lap = Label("Lap", raceInfoRect, topLeft, new(17, -37), new(257, 30), 23, Color.white);
            timer = Label("Race time", raceInfoRect, topLeft, new(17, -66), new(257, 41), 34, Color.white);
            nextGate = Label("Next checkpoint", raceInfoRect, topLeft, new(17, -113), new(257, 22), 16, Aqua);

            delta = Label("Checkpoint delta", raceInfoRect, topLeft, new(17, -137), new(285, 24), 16, Aqua);

            kartInfoRect = Panel("Kart information", raceCanvas, bottomRight, new(-24, 24), new(221, 83), Ink);
            speed = Label("Speed", kartInfoRect, topLeft, new(16, -8), new(190, 34), 27, Color.white);
            boost = Label("Boost", kartInfoRect, topLeft, new(16, -46), new(190, 27), 17, Aqua);

            itemPanelRect = Panel("Held item", raceCanvas, bottomRight, new(-24, 119), new(290, 145), Ink);
            itemPanel = itemPanelRect.gameObject;
            item = Label("Item", itemPanelRect, topLeft, new(14, -7), new(239, 25), 17, Gold);
            itemBinding = Label("Item binding", itemPanelRect, topLeft, new(14, -35), new(239, 24), 15, Color.white);
            itemBinding.text = "E / West button (X / Square)  Use";
            effects = Label("Item effects", itemPanelRect, topLeft, new(14, -64), new(268, 76), 16, Aqua, TextAnchor.UpperLeft);

            controlsRect = Panel("Controls", raceCanvas, bottomLeft, new(24, 24), new(534, 62), Ink);
            controlsKeyboard = Label("Keyboard", controlsRect, topLeft, new(14, -7), new(515, 23), 16, Color.white);
            controlsKeyboard.text = "WASD / arrows  Drive     Space  Drift     R  Reset     Esc  Pause";
            controlsGamepad = Label("Gamepad", controlsRect, topLeft, new(14, -32), new(515, 23), 16, Aqua);
            controlsGamepad.text = "Stick + triggers  Drive     RB  Drift     Y  Reset     Start  Pause";

            RectTransform dividerRect = Panel("Split divider", raceCanvas, center, Vector2.zero, new(4, 720), new Color(0.4f, 1f, 0.85f, 0.55f));
            splitDivider = dividerRect.gameObject;
            splitDivider.SetActive(false);
            RectTransform hDividerRect = Panel("Split H divider", raceCanvas, center, Vector2.zero, new(1280, 4), new Color(0.4f, 1f, 0.85f, 0.55f));
            splitHDivider = hDividerRect.gameObject;
            splitHDivider.SetActive(false);

            RectTransform p2Root = Fill("P2 HUD", raceCanvas, Color.clear);
            p2HudRoot = p2Root.gameObject;
            var topRight = new Vector2(1, 1);
            RectTransform p2Info = Panel("P2 Race information", p2Root, topRight, new(-24, -22), new(295, 142), Ink);
            Label("P2 Tag", p2Info, topLeft, new(17, -9), new(260, 24), 16, Gold).text = "PLAYER 2";
            p2Lap = Label("P2 Lap", p2Info, topLeft, new(17, -34), new(257, 28), 21, Color.white);
            p2Timer = Label("P2 Race time", p2Info, topLeft, new(17, -62), new(257, 36), 30, Color.white);
            p2NextGate = Label("P2 Next checkpoint", p2Info, topLeft, new(17, -104), new(257, 22), 15, Aqua);

            RectTransform p2Handling = Panel("P2 Kart information", p2Root, bottomRight, new(-24, 24), new(221, 83), Ink);
            p2Speed = Label("P2 Speed", p2Handling, topLeft, new(16, -8), new(190, 34), 27, Color.white);
            p2Boost = Label("P2 Boost", p2Handling, topLeft, new(16, -46), new(190, 27), 17, Aqua);

            RectTransform p2Items = Panel("P2 Held item", p2Root, bottomRight, new(-24, 119), new(270, 128), Ink);
            p2ItemPanel = p2Items.gameObject;
            p2Item = Label("P2 Item", p2Items, topLeft, new(14, -7), new(240, 24), 16, Gold);
            Label("P2 Item binding", p2Items, topLeft, new(14, -33), new(240, 22), 14, Color.white).text = "/ or Numpad0  Use   RShift  Drift";
            p2Effects = Label("P2 Item effects", p2Items, topLeft, new(14, -58), new(244, 64), 15, Aqua, TextAnchor.UpperLeft);
            p2HudRoot.SetActive(false);

            RectTransform p3Root = Fill("P3 HUD", raceCanvas, Color.clear);
            p3HudRoot = p3Root.gameObject;
            RectTransform p3Info = Panel("P3 Race information", p3Root, bottomLeft, new(24, 24), new(250, 74), Ink);
            p3Lap = Label("P3 Lap", p3Info, topLeft, new(14, -8), new(222, 26), 18, Gold);
            p3Speed = Label("P3 Speed", p3Info, topLeft, new(14, -38), new(222, 26), 18, Color.white);
            p3HudRoot.SetActive(false);

            RectTransform p4Root = Fill("P4 HUD", raceCanvas, Color.clear);
            p4HudRoot = p4Root.gameObject;
            RectTransform p4Info = Panel("P4 Race information", p4Root, bottomRight, new(-24, 24), new(250, 74), Ink);
            p4Lap = Label("P4 Lap", p4Info, topLeft, new(14, -8), new(222, 26), 18, Gold);
            p4Speed = Label("P4 Speed", p4Info, topLeft, new(14, -38), new(222, 26), 18, Color.white);
            p4HudRoot.SetActive(false);

            banner = Label("Countdown", raceCanvas, center, new(0, 105), new(330, 105), 76, Aqua, TextAnchor.MiddleCenter);
            var shadow = banner.gameObject.AddComponent<Shadow>();
            shadow.effectColor = Color.black;
            shadow.effectDistance = new Vector2(3, -3);

            RectTransform shade = Fill("Pause and result", raceCanvas, new Color(0, 0, 0, 0.63f));
            shade.GetComponent<Image>().raycastTarget = true;
            modal = shade.gameObject;
            RectTransform card = Panel("Result card", shade, center, Vector2.zero, new(578, 332), Ink);
            modalTitle = Label("Title", card, topLeft, new(29, -20), new(520, 49), 32, Aqua);
            modalBody = Label("Details", card, topLeft, new(29, -79), new(520, 119), 22, Color.white, TextAnchor.UpperLeft);
            recordStatus = Label("Record status", card, topLeft, new(29, -200), new(520, 37), 18, Gold);
            Button retry = Button("Retry", card, new(29, 23), new(160, 48), onRetry);
            resume = Button("Resume", card, new(206, 23), new(160, 48), onResume);
            Button trackSelect = Button("Track Select", card, new(383, 23), new(166, 48), onTrackSelect);
            pauseButtons = new[] { retry, resume, trackSelect };
            resultButtons = new[] { retry, trackSelect };

            RectTransform trackScreen = Fill("Track select", canvas, new Color(0.025f, 0.045f, 0.075f));
            menu = trackScreen.gameObject;
            const float trackRowPitch = 76f;
            const float trackRowHeight = 66f;
            const float topTrackRow = 434f;
            RectTransform trackCard = Panel("Circuits", trackScreen, center, Vector2.zero, new(900, 672), Ink);
            Label("Game title", trackCard, topLeft, new(30, -14), new(420, 44), 34, Color.white).text = "DRIFT CIRCUIT";
            Label("Track select title", trackCard, topLeft, new(30, -60), new(405, 26), 18, Aqua).text = "TIME TRIALS";
            Label("Quick race title", trackCard, topLeft, new(465, -60), new(405, 26), 18, Gold).text = "QUICK RACES (VS RIVAL)";
            apiStatusLabel = Label("API status", trackCard, topLeft, new(465, -20), new(405, 28), 16, Aqua, TextAnchor.MiddleRight);
            driverStatsLabel = Label("Driver stats", trackCard, topLeft, new(30, -90), new(840, 24), 15, Gold);
            int count = CourseDefinition.All.Length;
            trackButtons = new Button[count * 2 + 9];
            trackRecords = new Text[count];
            trackNames = new Text[count];
            quickNames = new Text[count];
            trialButtons = new Button[count];
            quickButtons = new Button[count];
            for (int i = 0; i < count; i++)
            {
                int slot = i;
                float y = topTrackRow - i * trackRowPitch;
                Button button = Button(CourseDefinition.All[i].Name, trackCard, new(30, y), new(405, trackRowHeight),
                    () => onStart(currentCircuits[slot]), showLabel: false);
                RectTransform row = (RectTransform)button.transform;
                trackNames[i] = Label("Name", row, topLeft, new(16, -7), new(373, 28), 21, Ink);
                trackNames[i].text = CourseDefinition.All[i].Name;
                trackRecords[i] = Label("Personal bests", row, topLeft, new(16, -38), new(373, 24), 15, Ink);
                trialButtons[i] = button;
                trackButtons[i] = button;

                Button quick = Button(QuickRaceName(CourseDefinition.All[i]), trackCard, new(465, y),
                    new(405, trackRowHeight), () => onQuickRace(currentCircuits[slot]));
                quickNames[i] = quick.GetComponentInChildren<Text>();
                quickButtons[i] = quick;
                trackButtons[count + i] = quick;
            }
            Button modeToggle = Button("Mode", trackCard, new(30, 158), new(190, 40), onModeToggle ?? (() => { }));
            modeLabel = modeToggle.GetComponentInChildren<Text>();
            trackButtons[^9] = modeToggle;
            Button p1Toggle = Button("P1 Driver", trackCard, new(232, 158), new(310, 40), onP1DriverCycle ?? (() => { }));
            p1DriverLabel = p1Toggle.GetComponentInChildren<Text>();
            trackButtons[^8] = p1Toggle;
            Button p2Toggle = Button("P2 Driver", trackCard, new(554, 158), new(316, 40), onP2DriverCycle ?? (() => { }));
            p2DriverLabel = p2Toggle.GetComponentInChildren<Text>();
            trackButtons[^7] = p2Toggle;

            Button cupToggle = Button("Cup", trackCard, new(30, 108), new(195, 38), onCupToggle ?? (() => { }));
            cupLabel = cupToggle.GetComponentInChildren<Text>();
            trackButtons[^6] = cupToggle;
            Button authButton = Button("Sign In", trackCard, new(245, 108), new(195, 38), onAuthAction ?? (() => { }));
            authLabel = authButton.GetComponentInChildren<Text>();
            trackButtons[^5] = authButton;
            Button onlineButton = Button("Online", trackCard, new(460, 108), new(195, 38), onOnlineOpen ?? (() => { }));
            trackButtons[^4] = onlineButton;
            Button boardButton = Button("Leaderboard", trackCard, new(675, 108), new(195, 38), onLeaderboard ?? (() => { }));
            trackButtons[^3] = boardButton;

            Button beaconToggle = Button("Route Beacon", trackCard, new(30, 54), new(405, 44), onBeaconToggle ?? (() => { }));
            beaconLabel = beaconToggle.GetComponentInChildren<Text>();
            trackButtons[^2] = beaconToggle;
            Button ghostToggle = Button("Ghost", trackCard, new(465, 54), new(405, 44), onGhostToggle);
            ghostLabel = ghostToggle.GetComponentInChildren<Text>();
            trackButtons[^1] = ghostToggle;
            Label("Selection controls", trackCard, bottomLeft, new(30, 18), new(840, 26), 16, Aqua).text =
                "↑ / ↓ or D-pad   Select      Enter / A   Confirm";

            RectTransform boardShade = Fill("Leaderboard modal", trackScreen, new Color(0, 0, 0, 0.72f));
            boardShade.GetComponent<Image>().raycastTarget = true;
            boardModal = boardShade.gameObject;
            RectTransform boardCard = Panel("Leaderboard card", boardShade, center, Vector2.zero, new(560, 340), Ink);
            Label("Board title", boardCard, topLeft, new(28, -18), new(504, 40), 28, Aqua).text = "LEADERBOARD";
            boardBody = Label("Board body", boardCard, topLeft, new(28, -68), new(504, 195), 18, Color.white, TextAnchor.UpperLeft);
            Button("Close", boardCard, new(195, 22), new(170, 44), () => boardModal.SetActive(false));
            boardModal.SetActive(false);

            RectTransform onlineShade = Fill("Online modal", trackScreen, new Color(0, 0, 0, 0.72f));
            onlineShade.GetComponent<Image>().raycastTarget = true;
            onlineModal = onlineShade.gameObject;
            RectTransform onlineCard = Panel("Online card", onlineShade, center, Vector2.zero, new(560, 320), Ink);
            Label("Online title", onlineCard, topLeft, new(28, -18), new(504, 36), 26, Aqua).text = "ONLINE MULTIPLAYER";
            onlineStatusLabel = Label("Online status", onlineCard, topLeft, new(28, -64), new(504, 44), 17, Gold, TextAnchor.UpperLeft);
            onlineStatusLabel.text = "Host a room or enter a 4-character code to join.";
            RectTransform codeBg = Panel("Code input", onlineCard, bottomLeft, new(28, 128), new(240, 42), new Color(0.10f, 0.16f, 0.24f));
            codeBg.GetComponent<Image>().raycastTarget = true;
            joinCodeInput = codeBg.gameObject.AddComponent<InputField>();
            Text codeText = Label("Code text", codeBg, topLeft, new(10, -4), new(220, 34), 20, Color.white);
            joinCodeInput.textComponent = codeText;
            joinCodeInput.characterLimit = 4;
            Button("Join Room", onlineCard, new(284, 128), new(248, 42), () => onOnlineJoin?.Invoke(joinCodeInput.text));
            Button("Host Room", onlineCard, new(28, 74), new(240, 42), onOnlineHost ?? (() => { }));
            Button("Start Online Race", onlineCard, new(284, 74), new(248, 42), onOnlineStart ?? (() => { }));
            Button("Close Online", onlineCard, new(195, 20), new(170, 42), () => onlineModal.SetActive(false));
            onlineModal.SetActive(false);

            var eventObject = new GameObject("Time trial input", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventObject.transform.SetParent(parent, false);
            events = eventObject.GetComponent<EventSystem>();
            var module = eventObject.GetComponent<InputSystemUIInputModule>();
            // TrialInput owns navigation and confirm. The UI module handles pointer input.
            module.submit = null;
            module.cancel = null;
            module.move = null;
        }

        public void ShowTracks(TrialRecordStore store, bool ghostEnabled, bool beaconEnabled = false,
            string apiStatus = "Offline (local records)", CourseDefinition.Cup cup = CourseDefinition.Cup.City,
            bool signedIn = false, bool signInPending = false,
            int players = 1, DriverProfile p1 = null, DriverProfile p2 = null)
        {
            raceHud.SetActive(false);
            menu.SetActive(true);
            boardModal.SetActive(false);
            onlineModal.SetActive(false);
            playerCount = Math.Clamp(players, 1, 4);
            p1Driver = p1 ?? DriverProfile.CyberShogun;
            p2Driver = p2 ?? DriverProfile.SuryaPrime;
            currentCircuits = cup == CourseDefinition.Cup.Skyline ? CourseDefinition.Skyline : CourseDefinition.All;
            for (int i = 0; i < currentCircuits.Length; i++)
            {
                CourseDefinition def = currentCircuits[i];
                trialButtons[i].gameObject.name = def.Name;
                trackNames[i].text = def.Name;
                quickButtons[i].gameObject.name = QuickRaceName(def);
                quickNames[i].text = QuickRaceName(def);
                TrialRecord record = store.Load(def);
                string driverTag = !string.IsNullOrEmpty(record?.DriverName) ? $"  ({record.DriverName})" : "";
                trackRecords[i].text = $"Best lap  {FormatBest(record?.BestLap)}     Best total  {FormatBest(record?.BestTotal)}{driverTag}";
            }
            modeLabel.text = playerCount == 1 ? "Mode: 1P Solo" : $"Mode: {playerCount}P Split";
            p1DriverLabel.text = $"P1: {p1Driver.DisplayName}";
            p2DriverLabel.text = playerCount > 1 ? $"P2: {p2Driver.DisplayName}" : $"Rival: {p2Driver.DisplayName}";
            driverStatsLabel.text = $"P1 {p1Driver.DisplayName} ({p1Driver.Role}): {p1Driver.StatSummary}";
            cupLabel.text = cup == CourseDefinition.Cup.Skyline ? "Cup: Skyline Cup" : "Cup: City Cup";
            authLabel.text = signedIn ? "Sign Out" : signInPending ? "Cancel Sign-In" : "Sign In";
            ShowGhostPreference(ghostEnabled);
            ShowBeaconPreference(beaconEnabled);
            apiStatusLabel.text = apiStatus;
            SetButtons(trackButtons);
        }

        public void ShowOnlineModal(string statusText = null)
        {
            if (!string.IsNullOrEmpty(statusText))
                onlineStatusLabel.text = statusText;
            onlineModal.SetActive(true);
        }

        public void UpdateOnlineStatus(string statusText)
        {
            if (!string.IsNullOrEmpty(statusText))
                onlineStatusLabel.text = statusText;
        }

        public void ShowLeaderboard(string bodyText)
        {
            boardBody.text = bodyText;
            boardModal.SetActive(true);
        }

        public void ShowGhostPreference(bool enabled) => ghostLabel.text = enabled ? "Ghost   ON" : "Ghost   OFF";

        public void ShowBeaconPreference(bool enabled) => beaconLabel.text = enabled ? "Route Beacon   ON" : "Route Beacon   OFF";

        /// <summary>Sets the race title and exposes item feedback for item-enabled Quick Races or split-screen.</summary>
        public void ShowCircuit(CourseDefinition definition, bool isQuickRace, int players = 1,
            DriverProfile p1 = null, DriverProfile p2 = null, bool isOnline = false)
        {
            menu.SetActive(false);
            raceHud.SetActive(true);
            quickRace = isQuickRace;
            playerCount = Math.Clamp(players, 1, 4);
            p1Driver = p1 ?? DriverProfile.CyberShogun;
            p2Driver = p2 ?? DriverProfile.SuryaPrime;
            bool split = playerCount > 1;
            bool hasItems = (isQuickRace || split) && definition != CourseDefinition.Rustrail && definition != CourseDefinition.Foundry;
            itemPanel.SetActive(hasItems);
            p2ItemPanel.SetActive(hasItems && playerCount == 2);
            splitDivider.SetActive(split);
            splitHDivider.SetActive(playerCount >= 3);
            p2HudRoot.SetActive(split);
            p3HudRoot.SetActive(playerCount >= 3);
            p4HudRoot.SetActive(playerCount >= 4);
            circuit.fontSize = quickRace || split || isOnline ? 14 : 18;
            circuit.text = isOnline
                ? $"ONLINE · {definition.Name}".ToUpperInvariant()
                : split
                    ? $"{playerCount}P SPLIT · {definition.Name}".ToUpperInvariant()
                    : (quickRace ? QuickRaceName(definition) : definition.Name).ToUpperInvariant();

            if (split)
            {
                SetAnchor(kartInfoRect, Vector2.zero, playerCount >= 3 ? new(24, 384) : new(24, 24));
                SetAnchor(itemPanelRect, Vector2.zero, playerCount >= 3 ? new(24, 474) : new(24, 119));
                controlsRect.gameObject.SetActive(false);
            }
            else
            {
                SetAnchor(kartInfoRect, new(1, 0), new(-24, 24));
                SetAnchor(itemPanelRect, new(1, 0), new(-24, 119));
                controlsRect.gameObject.SetActive(true);
            }
            previousPhase = (TrialPhase)(-1);
        }

        private static void SetAnchor(RectTransform rect, Vector2 anchor, Vector2 position)
        {
            rect.pivot = rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
        }

        public void Navigate(bool up, bool down, bool confirm)
        {
            if (activeButtons == null || (!up && !down && !confirm))
                return;

            int pointerSelection = Array.FindIndex(activeButtons,
                button => button.gameObject == events.currentSelectedGameObject);
            if (pointerSelection >= 0)
                selected = pointerSelection;
            if (up != down)
                selected = (selected + (down ? 1 : activeButtons.Length - 1)) % activeButtons.Length;
            events.SetSelectedGameObject(activeButtons[selected].gameObject);
            if (confirm)
                activeButtons[selected].onClick.Invoke();
        }

        private void SetButtons(Button[] buttons)
        {
            activeButtons = buttons;
            selected = 0;
            events.SetSelectedGameObject(buttons?[0].gameObject);
        }

        /// <summary>Refreshes live readings and updates the result card when its phase changes.</summary>
        public void Refresh(TrialRules rules, TrialKart kart, bool showGo, double? checkpointDelta,
            RecordResult result, QuickRaceResult? quickResult, bool incomingRibbon = false,
            bool incomingPulse = false, bool inSlipstream = false)
        {
            RefreshSingleHud(rules, kart, showGo, checkpointDelta, incomingRibbon, incomingPulse, inSlipstream,
                lap, timer, nextGate, delta, speed, boost, item, effects);

            bool finished = rules.Phase == TrialPhase.Finished;
            bool split = playerCount > 1;
            string finishTitle = split ? $"{playerCount}P RACE COMPLETE" : quickRace ? "QUICK RACE COMPLETE" : "TIME TRIAL COMPLETE";
            if (rules.Phase == previousPhase)
                return;
            previousPhase = rules.Phase;
            bool paused = rules.Phase == TrialPhase.Paused;
            modal.SetActive(paused || finished);
            resume.gameObject.SetActive(paused);
            SetButtons(paused ? pauseButtons : finished ? resultButtons : null);
            modalTitle.text = finished ? finishTitle : "PAUSED";
            if (quickResult.HasValue)
                modalBody.text = FormatPlaces(quickResult.Value, split, p1Driver.DisplayName, p2Driver.DisplayName);
            else if (finished)
                modalBody.text = $"Total     {Format(rules.Elapsed)}\nLap 1     {Format(rules.LapSplit(0))}\nLap 2     {Format(rules.LapSplit(1))}\nLap 3     {Format(rules.LapSplit(2))}";
            else
                modalBody.text = "Esc / Start   Resume";
            recordStatus.text = result switch
            {
                RecordResult.SaveFailed => "Personal best could not be saved",
                RecordResult.Lap | RecordResult.Total => "NEW BEST LAP  /  NEW BEST TOTAL",
                RecordResult.Lap => "NEW BEST LAP",
                RecordResult.Total => "NEW BEST TOTAL",
                _ => ""
            };
        }

        /// <summary>Refreshes 2P/3P/4P HUD panels in split-screen mode.</summary>
        public void RefreshMultiPlayer(TrialRules[] allRules, TrialKart[] allKarts,
            bool showGo, QuickRaceResult? splitResult,
            bool p1IncomingRibbon, bool p1IncomingPulse, bool p1Slipstream,
            bool p2IncomingRibbon, bool p2IncomingPulse, bool p2Slipstream)
        {
            Refresh(allRules[0], allKarts[0], showGo, null, RecordResult.None, splitResult,
                p1IncomingRibbon, p1IncomingPulse, p1Slipstream);
            if (allRules.Length > 1 && allRules[1] != null && allKarts[1] != null)
            {
                RefreshSingleHud(allRules[1], allKarts[1], showGo, null, p2IncomingRibbon, p2IncomingPulse, p2Slipstream,
                    p2Lap, p2Timer, p2NextGate, null, p2Speed, p2Boost, p2Item, p2Effects);
            }
            if (allRules.Length > 2 && allRules[2] != null && allKarts[2] != null)
            {
                p3Lap.text = $"P3  LAP {Math.Min(allRules[2].Laps + 1, TrialRules.LapCount)}/{TrialRules.LapCount}";
                p3Speed.text = $"{Mathf.RoundToInt(Mathf.Abs(allKarts[2].ForwardSpeed) * 3.6f)} km/h";
            }
            if (allRules.Length > 3 && allRules[3] != null && allKarts[3] != null)
            {
                p4Lap.text = $"P4  LAP {Math.Min(allRules[3].Laps + 1, TrialRules.LapCount)}/{TrialRules.LapCount}";
                p4Speed.text = $"{Mathf.RoundToInt(Mathf.Abs(allKarts[3].ForwardSpeed) * 3.6f)} km/h";
            }
        }

        private void RefreshSingleHud(TrialRules rules, TrialKart kart, bool showGo, double? checkpointDelta,
            bool incomingRibbon, bool incomingPulse, bool inSlipstream,
            Text lapText, Text timerText, Text gateText, Text deltaText, Text speedText, Text boostText,
            Text itemText, Text effectsText)
        {
            bool finished = rules.Phase == TrialPhase.Finished;
            string finishTitle = playerCount > 1 ? $"{playerCount}P RACE COMPLETE" : quickRace ? "QUICK RACE COMPLETE" : "TIME TRIAL COMPLETE";
            lapText.text = $"LAP {Math.Min(rules.Laps + 1, TrialRules.LapCount)} / {TrialRules.LapCount}";
            timerText.text = Format(rules.Elapsed);
            gateText.text = finished ? finishTitle : "NEXT  " + rules.ExpectedGate.Name;
            if (deltaText != null)
            {
                deltaText.text = checkpointDelta.HasValue
                    ? $"PB  {(checkpointDelta.Value < 0 ? "−" : "+")}{Math.Abs(checkpointDelta.Value):0.000}s" : "";
                deltaText.color = checkpointDelta > 0 ? Gold : Aqua;
            }
            speedText.text = $"{Mathf.RoundToInt(Mathf.Abs(kart.ForwardSpeed) * 3.6f)} km/h";
            bool racing = rules.Phase == TrialPhase.Racing || rules.Phase == TrialPhase.Paused;
            bool active = racing && kart.BoostActive;
            bool ready = racing && kart.BoostReady;
            boostText.text = active ? "BOOST ACTIVE" : ready ? "BOOST READY" : "DRIFT TO CHARGE";
            boostText.color = active || ready ? Gold : Aqua;
            itemText.text = kart.HeldItem switch
            {
                RaceItem.RelayBoost => "RELAY BOOST READY",
                RaceItem.FluxRibbon => "FLUX RIBBON READY",
                RaceItem.PulseShield => "PULSE SHIELD READY",
                RaceItem.SignalTap => "SIGNAL TAP READY",
                RaceItem.SlipstreamLink => "SLIPSTREAM LINK READY",
                RaceItem.RouteBeacon => "ROUTE BEACON READY",
                _ => "ITEM SLOT EMPTY"
            };
            effectsText.text = (kart.ShieldActive ? $"SHIELD {kart.ShieldRemaining:0.0}s\n" : "") +
                (kart.GripLossRemaining > 0.0000001d ? $"GRIP LOSS {kart.GripLossRemaining:0.0}s\n" : "") +
                (kart.AccelTapped ? $"TAP -ACCEL {kart.AccelTapRemaining:0.0}s\n" : "") +
                (kart.SlipstreamActive ? $"SLIPSTREAM {(inSlipstream ? "DRAFT " : "")}{kart.SlipstreamRemaining:0.0}s\n" : "") +
                (kart.BeaconActive ? $"BEACON {kart.BeaconRemaining:0.0}s\n" : "") +
                (kart.DisruptionImmune ? $"IMMUNITY {kart.ImmunityRemaining:0.0}s\n" : "") +
                (incomingRibbon ? "INCOMING RIBBON\n" : "") +
                (incomingPulse ? "INCOMING PULSE" : "");
            banner.text = rules.Phase == TrialPhase.Countdown ? Math.Ceiling(rules.Countdown).ToString()
                : rules.Phase == TrialPhase.Racing && showGo ? "GO" : "";
        }

        private static string FormatPlaces(QuickRaceResult result, bool twoPlayerMode = false,
            string p1Name = "Player", string p2Name = "Rival")
        {
            string label1 = twoPlayerMode ? $"P1 ({p1Name})" : "Player";
            string label2 = twoPlayerMode ? $"P2 ({p2Name})" : "Rival";
            string player = $"{result.PlayerPlace}    {label1}     {Format(result.PlayerTime)}";
            string rivalTime = result.RivalTime.HasValue ? Format(result.RivalTime.Value) : "Still racing";
            string rival = $"{result.RivalPlace}    {label2}       {rivalTime}";
            string places = result.PlayerPlace == 2 ? $"{rival}\n{player}" : $"{player}\n{rival}";
            return result.RivalPlace == 1 && result.PlayerPlace == 1 ? $"{places}\nShared first" : places;
        }

        private static string FormatBest(double? seconds) => seconds.HasValue ? Format(seconds.Value) : "—";

        /// <summary>Formats clock values as minutes, seconds, and milliseconds.</summary>
        private static string Format(double seconds)
        {
            long ms = (long)Math.Round(seconds * 1000);
            return $"{ms / 60000:00}:{ms / 1000 % 60:00}.{ms % 1000:000}";
        }

        /// <summary>Creates a pointer button with an explicit keyboard and controller label.</summary>
        private Button Button(string text, RectTransform parent, Vector2 position, Vector2 size, Action click,
            bool showLabel = true)
        {
            RectTransform rect = Panel(text, parent, Vector2.zero, position, size, Aqua);
            var image = rect.GetComponent<Image>();
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.Explicit };
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.70f, 0.77f, 0.77f);
            colors.selectedColor = new Color(1f, 1f, 1f);
            colors.highlightedColor = new Color(0.92f, 0.96f, 0.96f);
            button.colors = colors;
            button.onClick.AddListener(() => click());
            if (showLabel)
                Label("Label", rect, Vector2.one * 0.5f, Vector2.zero, size, 18, Ink, TextAnchor.MiddleCenter).text = text;
            return button;
        }

        /// <summary>Creates a text label that leaves hit testing to its parent control.</summary>
        private Text Label(string name, RectTransform parent, Vector2 anchor, Vector2 position,
            Vector2 size, int fontSize, Color color, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var label = Rect(name, parent, anchor, position, size).gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            return label;
        }

        private static RectTransform Fill(string name, RectTransform parent, Color color)
        {
            var rect = Panel(name, parent, Vector2.one * 0.5f, Vector2.zero, Vector2.zero, color);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            return rect;
        }

        /// <summary>Creates a flat panel that can optionally receive pointer events.</summary>
        private static RectTransform Panel(string name, RectTransform parent, Vector2 anchor,
            Vector2 position, Vector2 size, Color color)
        {
            RectTransform rect = Rect(name, parent, anchor, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        /// <summary>Places an element relative to a canvas or panel anchor.</summary>
        private static RectTransform Rect(string name, RectTransform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.pivot = rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }
    }
}
