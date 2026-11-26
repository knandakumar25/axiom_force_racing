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
        private readonly GameObject boardModal;
        private CourseDefinition[] currentCircuits = CourseDefinition.All;
        private Button[] activeButtons;
        private int selected;
        private bool quickRace;
        private readonly Text item, itemBinding, effects;
        private readonly GameObject itemPanel;
        private TrialPhase previousPhase = (TrialPhase)(-1);

        /// <summary>Creates the screen-space HUD and binds its pointer buttons.</summary>
        public TrialHud(Transform parent, Action onResume, Action onRetry, Action onTrackSelect,
            Action<CourseDefinition> onStart, Action<CourseDefinition> onQuickRace, Action onGhostToggle,
            Action onBeaconToggle = null, Action onCupToggle = null, Action onAuthAction = null, Action onLeaderboard = null)
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
            RectTransform race = Panel("Race information", raceCanvas, topLeft, new(24, -22), new(315, 170), Ink);
            circuit = Label("Circuit", race, topLeft, new(17, -9), new(285, 28), 18, Aqua);
            lap = Label("Lap", race, topLeft, new(17, -37), new(257, 30), 23, Color.white);
            timer = Label("Race time", race, topLeft, new(17, -66), new(257, 41), 34, Color.white);
            nextGate = Label("Next checkpoint", race, topLeft, new(17, -113), new(257, 22), 16, Aqua);

            delta = Label("Checkpoint delta", race, topLeft, new(17, -137), new(285, 24), 16, Aqua);

            RectTransform handling = Panel("Kart information", raceCanvas, bottomRight, new(-24, 24), new(221, 83), Ink);
            speed = Label("Speed", handling, topLeft, new(16, -8), new(190, 34), 27, Color.white);
            boost = Label("Boost", handling, topLeft, new(16, -46), new(190, 27), 17, Aqua);

            RectTransform items = Panel("Held item", raceCanvas, bottomRight, new(-24, 119), new(290, 145), Ink);
            itemPanel = items.gameObject;
            item = Label("Item", items, topLeft, new(14, -7), new(239, 25), 17, Gold);
            itemBinding = Label("Item binding", items, topLeft, new(14, -35), new(239, 24), 15, Color.white);
            itemBinding.text = "E / West button (X / Square)  Use";
            effects = Label("Item effects", items, topLeft, new(14, -64), new(268, 76), 16, Aqua, TextAnchor.UpperLeft);

            RectTransform help = Panel("Controls", raceCanvas, bottomLeft, new(24, 24), new(534, 62), Ink);
            Label("Keyboard", help, topLeft, new(14, -7), new(515, 23), 16, Color.white).text =
                "WASD / arrows  Drive     Space  Drift     R  Reset     Esc  Pause";
            Label("Gamepad", help, topLeft, new(14, -32), new(515, 23), 16, Aqua).text =
                "Stick + triggers  Drive     RB  Drift     Y  Reset     Start  Pause";

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
            const float trackRowPitch = 78f;
            const float trackRowHeight = 68f;
            const float topTrackRow = 384f;
            RectTransform trackCard = Panel("Circuits", trackScreen, center, Vector2.zero, new(900, 620), Ink);
            Label("Game title", trackCard, topLeft, new(30, -16), new(420, 44), 34, Color.white).text = "DRIFT CIRCUIT";
            Label("Track select title", trackCard, topLeft, new(30, -64), new(405, 28), 18, Aqua).text = "TIME TRIALS";
            Label("Quick race title", trackCard, topLeft, new(465, -64), new(405, 28), 18, Gold).text = "QUICK RACES (VS RIVAL)";
            apiStatusLabel = Label("API status", trackCard, topLeft, new(465, -22), new(405, 28), 16, Aqua, TextAnchor.MiddleRight);
            int count = CourseDefinition.All.Length;
            trackButtons = new Button[count * 2 + 5];
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
            Button cupToggle = Button("Cup", trackCard, new(30, 108), new(265, 38), onCupToggle ?? (() => { }));
            cupLabel = cupToggle.GetComponentInChildren<Text>();
            trackButtons[^5] = cupToggle;
            Button authButton = Button("Sign In", trackCard, new(317, 108), new(265, 38), onAuthAction ?? (() => { }));
            authLabel = authButton.GetComponentInChildren<Text>();
            trackButtons[^4] = authButton;
            Button boardButton = Button("Leaderboard", trackCard, new(605, 108), new(265, 38), onLeaderboard ?? (() => { }));
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
            bool signedIn = false, bool signInPending = false)
        {
            raceHud.SetActive(false);
            menu.SetActive(true);
            boardModal.SetActive(false);
            currentCircuits = cup == CourseDefinition.Cup.Skyline ? CourseDefinition.Skyline : CourseDefinition.All;
            for (int i = 0; i < currentCircuits.Length; i++)
            {
                CourseDefinition def = currentCircuits[i];
                trialButtons[i].gameObject.name = def.Name;
                trackNames[i].text = def.Name;
                quickButtons[i].gameObject.name = QuickRaceName(def);
                quickNames[i].text = QuickRaceName(def);
                TrialRecord record = store.Load(def);
                trackRecords[i].text = $"Best lap  {FormatBest(record?.BestLap)}     Best total  {FormatBest(record?.BestTotal)}";
            }
            cupLabel.text = cup == CourseDefinition.Cup.Skyline ? "Cup: Skyline Cup" : "Cup: City Cup";
            authLabel.text = signedIn ? "Sign Out" : signInPending ? "Cancel Sign-In" : "Sign In";
            ShowGhostPreference(ghostEnabled);
            ShowBeaconPreference(beaconEnabled);
            apiStatusLabel.text = apiStatus;
            SetButtons(trackButtons);
        }

        public void ShowLeaderboard(string bodyText)
        {
            boardBody.text = bodyText;
            boardModal.SetActive(true);
        }

        public void ShowGhostPreference(bool enabled) => ghostLabel.text = enabled ? "Ghost   ON" : "Ghost   OFF";

        public void ShowBeaconPreference(bool enabled) => beaconLabel.text = enabled ? "Route Beacon   ON" : "Route Beacon   OFF";

        /// <summary>Sets the race title and exposes item feedback for item-enabled Quick Races.</summary>
        public void ShowCircuit(CourseDefinition definition, bool isQuickRace)
        {
            menu.SetActive(false);
            raceHud.SetActive(true);
            quickRace = isQuickRace;
            itemPanel.SetActive(isQuickRace && definition != CourseDefinition.Rustrail && definition != CourseDefinition.Foundry);
            circuit.fontSize = quickRace ? 14 : 18;
            circuit.text = (quickRace ? QuickRaceName(definition) : definition.Name).ToUpperInvariant();
            previousPhase = (TrialPhase)(-1);
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
            bool finished = rules.Phase == TrialPhase.Finished;
            string finishTitle = quickRace ? "QUICK RACE COMPLETE" : "TIME TRIAL COMPLETE";
            lap.text = $"LAP {Math.Min(rules.Laps + 1, TrialRules.LapCount)} / {TrialRules.LapCount}";
            timer.text = Format(rules.Elapsed);
            nextGate.text = finished ? finishTitle : "NEXT  " + rules.ExpectedGate.Name;
            delta.text = checkpointDelta.HasValue
                ? $"PB  {(checkpointDelta.Value < 0 ? "−" : "+")}{Math.Abs(checkpointDelta.Value):0.000}s" : "";
            delta.color = checkpointDelta > 0 ? Gold : Aqua;
            speed.text = $"{Mathf.RoundToInt(Mathf.Abs(kart.ForwardSpeed) * 3.6f)} km/h";
            bool racing = rules.Phase == TrialPhase.Racing || rules.Phase == TrialPhase.Paused;
            bool active = racing && kart.BoostActive;
            bool ready = racing && kart.BoostReady;
            boost.text = active ? "BOOST ACTIVE" : ready ? "BOOST READY" : "DRIFT TO CHARGE";
            boost.color = active || ready ? Gold : Aqua;
            item.text = kart.HeldItem switch
            {
                RaceItem.RelayBoost => "RELAY BOOST READY",
                RaceItem.FluxRibbon => "FLUX RIBBON READY",
                RaceItem.PulseShield => "PULSE SHIELD READY",
                RaceItem.SignalTap => "SIGNAL TAP READY",
                RaceItem.SlipstreamLink => "SLIPSTREAM LINK READY",
                RaceItem.RouteBeacon => "ROUTE BEACON READY",
                _ => "ITEM SLOT EMPTY"
            };
            effects.text = (kart.ShieldActive ? $"SHIELD {kart.ShieldRemaining:0.0}s\n" : "") +
                (kart.GripLossRemaining > 0.0000001d ? $"GRIP LOSS {kart.GripLossRemaining:0.0}s\n" : "") +
                (kart.AccelTapped ? $"TAP -ACCEL {kart.AccelTapRemaining:0.0}s\n" : "") +
                (kart.SlipstreamActive ? $"SLIPSTREAM {(inSlipstream ? "DRAFT " : "")}{kart.SlipstreamRemaining:0.0}s\n" : "") +
                (kart.BeaconActive ? $"BEACON {kart.BeaconRemaining:0.0}s\n" : "") +
                (kart.DisruptionImmune ? $"IMMUNITY {kart.ImmunityRemaining:0.0}s\n" : "") +
                (incomingRibbon ? "INCOMING RIBBON\n" : "") +
                (incomingPulse ? "INCOMING PULSE" : "");
            banner.text = rules.Phase == TrialPhase.Countdown ? Math.Ceiling(rules.Countdown).ToString()
                : rules.Phase == TrialPhase.Racing && showGo ? "GO" : "";

            if (rules.Phase == previousPhase)
                return;
            previousPhase = rules.Phase;
            bool paused = rules.Phase == TrialPhase.Paused;
            modal.SetActive(paused || finished);
            resume.gameObject.SetActive(paused);
            SetButtons(paused ? pauseButtons : finished ? resultButtons : null);
            modalTitle.text = finished ? finishTitle : "PAUSED";
            if (quickResult.HasValue)
                modalBody.text = FormatPlaces(quickResult.Value);
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

        private static string FormatPlaces(QuickRaceResult result)
        {
            string player = $"{result.PlayerPlace}    Player     {Format(result.PlayerTime)}";
            string rivalTime = result.RivalTime.HasValue ? Format(result.RivalTime.Value) : "Still racing";
            string rival = $"{result.RivalPlace}    Rival       {rivalTime}";
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
