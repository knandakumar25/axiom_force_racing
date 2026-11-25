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
        private readonly Text[] trackRecords;
        private readonly Text ghostLabel;
        private Button[] activeButtons;
        private int selected;
        private bool quickRace;
        private TrialPhase previousPhase = (TrialPhase)(-1);

        /// <summary>Creates the screen-space HUD and binds its pointer buttons.</summary>
        public TrialHud(Transform parent, Action onResume, Action onRetry, Action onTrackSelect,
            Action<CourseDefinition> onStart, Action<CourseDefinition> onQuickRace, Action onGhostToggle)
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
            const float trackRowPitch = 83f;
            const float trackRowHeight = 72f;
            const float bottomTrackRow = 245f;
            float topTrackRow = bottomTrackRow + (CourseDefinition.All.Length - 1) * trackRowPitch;
            RectTransform trackCard = Panel("Circuits", trackScreen, center, Vector2.zero,
                new(580, topTrackRow + trackRowHeight + 112f), Ink);
            Label("Game title", trackCard, topLeft, new(30, -17), new(520, 48), 37, Color.white).text = "DRIFT CIRCUIT";
            Label("Track select title", trackCard, topLeft, new(30, -68), new(520, 32), 19, Aqua).text = "SELECT A CIRCUIT";
            trackButtons = new Button[CourseDefinition.All.Length + 3];
            trackRecords = new Text[CourseDefinition.All.Length];
            for (int i = 0; i < CourseDefinition.All.Length; i++)
            {
                CourseDefinition definition = CourseDefinition.All[i];
                Button button = Button(definition.Name, trackCard, new(30, topTrackRow - i * trackRowPitch),
                    new(520, trackRowHeight), () => onStart(definition), showLabel: false);
                RectTransform row = (RectTransform)button.transform;
                Label("Name", row, topLeft, new(18, -8), new(481, 30), 23, Ink).text = definition.Name;
                trackRecords[i] = Label("Personal bests", row, topLeft, new(18, -43), new(481, 25), 16, Ink);
                trackButtons[i] = button;
            }
            trackButtons[^3] = Button(QuickRaceName(CourseDefinition.Docks), trackCard, new(30, 187), new(520, 44),
                () => onQuickRace(CourseDefinition.Docks));
            trackButtons[^2] = Button(QuickRaceName(CourseDefinition.Rustrail), trackCard, new(30, 132), new(520, 44),
                () => onQuickRace(CourseDefinition.Rustrail));
            Button ghostToggle = Button("Ghost", trackCard, new(30, 66), new(520, 48), onGhostToggle);
            ghostLabel = ghostToggle.GetComponentInChildren<Text>();
            trackButtons[^1] = ghostToggle;
            Label("Selection controls", trackCard, bottomLeft, new(30, 23), new(520, 26), 16, Aqua).text =
                "↑ / ↓ or D-pad   Select      Enter / A   Confirm";

            var eventObject = new GameObject("Time trial input", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventObject.transform.SetParent(parent, false);
            events = eventObject.GetComponent<EventSystem>();
            var module = eventObject.GetComponent<InputSystemUIInputModule>();
            // TrialInput owns navigation and confirm. The UI module handles pointer input.
            module.submit = null;
            module.cancel = null;
            module.move = null;
        }

        public void ShowTracks(TrialRecordStore store, bool ghostEnabled)
        {
            raceHud.SetActive(false);
            menu.SetActive(true);
            for (int i = 0; i < trackRecords.Length; i++)
            {
                TrialRecord record = store.Load(CourseDefinition.All[i]);
                trackRecords[i].text = $"Best lap  {FormatBest(record?.BestLap)}     Best total  {FormatBest(record?.BestTotal)}";
            }
            ShowGhostPreference(ghostEnabled);
            SetButtons(trackButtons);
        }

        public void ShowGhostPreference(bool enabled) => ghostLabel.text = enabled ? "Ghost   ON" : "Ghost   OFF";

        public void ShowCircuit(CourseDefinition definition, bool isQuickRace)
        {
            menu.SetActive(false);
            raceHud.SetActive(true);
            quickRace = isQuickRace;
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
            RecordResult result, QuickRaceResult? quickResult)
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
