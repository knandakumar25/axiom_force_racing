using System.Collections;
using System.IO;
using DriftCircuit;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>Drives the serialized time-trial scene with Input System virtual devices.</summary>
public sealed class TimeTrialPlayTests
{
    private Keyboard keyboard;
    private Gamepad pad;
    private TimeTrial trial;
    private string recordDirectory;
    private int originalGhostPreference;
    private float originalTimeScale;
    private bool originalRunInBackground;
    private InputSettings.BackgroundBehavior originalBackgroundBehavior;
#if UNITY_EDITOR
    private InputSettings.EditorInputBehaviorInPlayMode originalEditorInputBehavior;
#endif

    /// <summary>Opens the same enabled scene a desktop player would load.</summary>
    [UnitySetUp]
    public IEnumerator LoadTrial()
    {
        originalTimeScale = Time.timeScale;
        originalRunInBackground = Application.runInBackground;
        originalBackgroundBehavior = InputSystem.settings.backgroundBehavior;
        Application.runInBackground = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
        // CLI-driven tests can run with Game View unfocused, which normally routes keyboard events to the Editor.
        originalEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.editorInputBehaviorInPlayMode =
            InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
        Time.timeScale = 2f;
        recordDirectory = Path.Combine(Application.temporaryCachePath, "drift-trial-tests-" + System.Guid.NewGuid());
        TimeTrial.RecordDirectoryOverride = recordDirectory;
        originalGhostPreference = PlayerPrefs.GetInt(TimeTrial.GhostPreference, -1);
        PlayerPrefs.DeleteKey(TimeTrial.GhostPreference);
        keyboard = InputSystem.AddDevice<Keyboard>();
        pad = InputSystem.AddDevice<Gamepad>();
        yield return LoadScene(TimeTrial.ScenePath);
    }

    /// <summary>Returns the Editor to its original timing and input configuration.</summary>
    [UnityTearDown]
    public IEnumerator UnloadTrial()
    {
        if (trial)
            yield return SceneManager.UnloadSceneAsync(trial.gameObject.scene);
        TimeTrial.RecordDirectoryOverride = null;
        if (Directory.Exists(recordDirectory))
            Directory.Delete(recordDirectory, true);
        if (originalGhostPreference < 0)
            PlayerPrefs.DeleteKey(TimeTrial.GhostPreference);
        else
            PlayerPrefs.SetInt(TimeTrial.GhostPreference, originalGhostPreference);
        PlayerPrefs.Save();
        InputSystem.RemoveDevice(keyboard);
        InputSystem.RemoveDevice(pad);
#if UNITY_EDITOR
        InputSystem.settings.editorInputBehaviorInPlayMode = originalEditorInputBehavior;
#endif
        InputSystem.settings.backgroundBehavior = originalBackgroundBehavior;
        Application.runInBackground = originalRunInBackground;
        Time.timeScale = originalTimeScale;
        LogAssert.NoUnexpectedReceived();
    }

    /// <summary>Checks keyboard handling, boost, reset, pause, retry, and an out-of-bounds recovery.</summary>
    [UnityTest]
    public IEnumerator KeyboardHandlesCountdownDriftAndRecovery()
    {
        Vector3 start = trial.KartBody.position;
        Keys(Key.W, Key.Space);
        yield return new WaitForSeconds(0.35f);
        Assert.That(trial.Rules.Phase, Is.EqualTo(TrialPhase.Countdown));
        Assert.That(trial.KartBody.position, Is.EqualTo(start));
        Assert.That(trial.Rules.Elapsed, Is.Zero);
        Keys();
        yield return new WaitUntil(() => trial.Rules.Phase == TrialPhase.Racing);
        Keys(Key.W);
        Rigidbody body = trial.KartBody;
        float speedBeforeDrift = 0f;
        for (int step = 0; step < 50 && speedBeforeDrift < 5f; step++)
        {
            yield return new WaitForFixedUpdate();
            speedBeforeDrift = Vector3.Dot(body.linearVelocity, body.rotation * Vector3.forward);
        }
        Assert.That(speedBeforeDrift, Is.GreaterThanOrEqualTo(5f), "W did not accelerate the kart to drift speed.");

        // Hold one turn from above the speed threshold so charge can accumulate continuously.
        Keys(Key.W, Key.Space, Key.D);
        yield return WaitForCharge();
        Assert.That(trial.BoostReady, Is.True, $"Keyboard drift did not charge at kart position {body.position}.");
        Keys(Key.W);
        yield return new WaitForSeconds(0.12f);
        Assert.That(trial.BoostActive, Is.True);

        Keys(Key.Escape);
        yield return null;
        yield return null;
        Assert.That(trial.Rules.Phase, Is.EqualTo(TrialPhase.Paused));
        double pausedAt = trial.Rules.Elapsed;
        Vector3 pausedPosition = trial.KartBody.position;
        yield return new WaitForSecondsRealtime(0.2f);
        Assert.That(trial.Rules.Elapsed, Is.EqualTo(pausedAt));
        Assert.That(trial.KartBody.position, Is.EqualTo(pausedPosition));
        Assert.That(trial.BoostActive, Is.True);
        Keys();
        yield return null;
        Keys(Key.Escape);
        yield return null;
        yield return null;
        Keys(Key.R, Key.Space);
        yield return new WaitForSeconds(0.1f);
        Assert.That(Vector3.Distance(trial.KartBody.position, start), Is.LessThan(0.3));
        Assert.That(trial.BoostActive || trial.BoostReady, Is.False);
        Assert.That(trial.Rules.Elapsed, Is.GreaterThan(pausedAt));

        Keys(Key.DownArrow, Key.LeftArrow);
        yield return new WaitForSeconds(0.35f);
        Assert.That(Vector3.Dot(trial.KartBody.linearVelocity, trial.KartBody.transform.forward), Is.LessThan(-1));
        Assert.That(Vector3.Angle(trial.KartBody.rotation * Vector3.forward, Vector3.right), Is.GreaterThan(5));
        Keys();
        trial.KartBody.position = new Vector3(0, -3, 0);
        yield return new WaitForSeconds(0.12f);
        Assert.That(Vector3.Distance(trial.KartBody.position, start), Is.LessThan(0.3));
        Assert.That(trial.Rules.NextGate, Is.Zero);
        Keys(Key.UpArrow);
        yield return new WaitForSeconds(0.25f);
        Assert.That(Vector3.Dot(body.linearVelocity, body.rotation * Vector3.forward), Is.GreaterThan(1f));
        Keys(Key.Escape);
        yield return null;
        yield return null;
        Keys(Key.Enter);
        yield return null;
        yield return null;
        Assert.That(trial.Rules.Phase, Is.EqualTo(TrialPhase.Countdown));
        Assert.That(trial.Rules.Elapsed, Is.Zero);
    }

    /// <summary>Completes three physical laps through the main route, shortcut, then main route.</summary>
    [UnityTest, Timeout(150000)]
    public IEnumerator VirtualGamepadCompletesBothRoutesAndRetries()
    {
        yield return new WaitUntil(() => trial.Rules.Phase == TrialPhase.Racing);
        Pad(new GamepadState().WithButton(GamepadButton.Start));
        yield return null;
        yield return null;
        Assert.That(trial.Rules.Phase, Is.EqualTo(TrialPhase.Paused));
        Pad(default);
        yield return null;
        Pad(new GamepadState().WithButton(GamepadButton.Start));
        yield return null;
        yield return null;
        Assert.That(trial.Rules.Phase, Is.EqualTo(TrialPhase.Racing));

        Pad(new GamepadState { rightTrigger = 1f });
        yield return new WaitForSeconds(0.48f);
        Pad(new GamepadState { rightTrigger = 1f, leftStick = new Vector2(0.25f, 0) }
            .WithButton(GamepadButton.RightShoulder));
        yield return WaitForCharge();
        Assert.That(trial.BoostReady, Is.True);
        Pad(new GamepadState { rightTrigger = 1f });
        yield return new WaitForSeconds(0.1f);
        Assert.That(trial.BoostActive, Is.True);
        Pad(new GamepadState().WithButton(GamepadButton.North));
        yield return new WaitForSeconds(0.1f);
        Assert.That(trial.BoostActive, Is.False);
        Assert.That(Vector3.Distance(trial.KartBody.position, CourseDefinition.Tokyo.Start), Is.LessThan(0.3f));
        Pad(new GamepadState { leftTrigger = 1f });
        yield return new WaitForSeconds(0.25f);
        Assert.That(Vector3.Dot(trial.KartBody.linearVelocity, trial.KartBody.rotation * Vector3.forward), Is.LessThan(-1f));
        Pad(new GamepadState().WithButton(GamepadButton.North));
        yield return new WaitForSeconds(0.1f);

        yield return DriveLap(false, 1);
        yield return DriveLap(true, 2);
        yield return DriveLap(false, 3);
        Assert.That(trial.Rules.Phase, Is.EqualTo(TrialPhase.Finished));
        double result = trial.Rules.Elapsed;
        double sum = 0;
        for (int lap = 0; lap < 3; lap++)
        {
            Assert.That(trial.Rules.LapSplit(lap), Is.Positive);
            sum += trial.Rules.LapSplit(lap);
        }
        Assert.That(sum, Is.EqualTo(result).Within(0.001));
        yield return new WaitForSeconds(0.2f);
        Assert.That(trial.Rules.Laps, Is.EqualTo(3));
        Assert.That(trial.Rules.Elapsed, Is.EqualTo(result));
        TrialRecord savedRun = new TrialRecordStore(recordDirectory).Load(CourseDefinition.Tokyo);
        Assert.That(System.Array.Exists(savedRun.Poses, pose => pose.Reset), Is.True);
        Debug.Log($"Three-lap virtual gamepad run completed in {result:F2}s, including both routes.");
        Pad(new GamepadState().WithButton(GamepadButton.South));
        yield return null;
        yield return null;
        Assert.That(trial.Rules.Phase, Is.EqualTo(TrialPhase.Countdown));
        Assert.That(trial.Rules.Elapsed, Is.Zero);
    }

    [UnityTest, Timeout(150000)]
    public IEnumerator VirtualGamepadCompletesViaductBoulevardTunnelBoulevard()
    {
        yield return LoadScene(TimeTrial.MenuScenePath);
        Assert.That(trial.Rules, Is.Null);
        Pad(new GamepadState().WithButton(GamepadButton.DpadDown));
        yield return null;
        yield return null;
        Pad(new GamepadState().WithButton(GamepadButton.South));
        yield return null;
        yield return null;
        Pad(default);
        Assert.That(trial.Circuit, Is.SameAs(CourseDefinition.Viaduct));

        yield return new WaitUntil(() => trial.Rules.Phase == TrialPhase.Racing);
        yield return DriveLap(false, 1);
        yield return DriveLap(true, 2);
        yield return DriveLap(false, 3);
        Assert.That(trial.Rules.Phase, Is.EqualTo(TrialPhase.Finished));
        double elapsed = trial.Rules.Elapsed;
        yield return null;
        Assert.That(Label("Record status").text, Does.Contain("NEW BEST LAP").And.Contain("NEW BEST TOTAL"));
        Assert.That(Label("Details").text, Does.Contain("Lap 1").And.Contain("Lap 2").And.Contain("Lap 3"));
        var store = new TrialRecordStore(recordDirectory);
        TrialRecord record = store.Load(CourseDefinition.Viaduct);
        Assert.That(record.BestTotal, Is.EqualTo(elapsed).Within(0.00001));
        Assert.That(record.Checkpoints, Has.Length.EqualTo(TrialRules.LapCount * CourseDefinition.Viaduct.Gates.Length));
        Assert.That(store.Load(CourseDefinition.Tokyo), Is.Null);
        Debug.Log($"Concrete Wards three-lap run completed in {elapsed:F2}s through boulevard, tunnel, and boulevard.");

        Pad(new GamepadState().WithButton(GamepadButton.DpadDown));
        yield return null;
        yield return null;
        Pad(new GamepadState().WithButton(GamepadButton.South));
        yield return null;
        yield return null;
        Assert.That(trial.Rules, Is.Null);
        var times = trial.GetComponentsInChildren<Text>();
        Assert.That(System.Array.Exists(times, text => text.name == "Personal bests" &&
            !text.text.Contains("Best total  —")), Is.True);
    }

    [UnityTest]
    public IEnumerator KeyboardPreferencesAndPauseReturnToTrackSelect()
    {
        SeedGhost(CourseDefinition.Tokyo);
        yield return LoadScene(TimeTrial.MenuScenePath);
        Assert.That(Label("Label", "Ghost").text, Is.EqualTo("Ghost   ON"));
        Keys(Key.UpArrow);
        yield return null;
        yield return null;
        Keys(Key.Enter);
        yield return null;
        yield return null;
        Assert.That(Label("Label", "Ghost").text, Is.EqualTo("Ghost   OFF"));
        Assert.That(PlayerPrefs.GetInt(TimeTrial.GhostPreference), Is.Zero);
        Keys();
        yield return LoadScene(TimeTrial.MenuScenePath);
        Assert.That(Label("Label", "Ghost").text, Is.EqualTo("Ghost   OFF"));

        Keys(Key.Enter);
        yield return null;
        yield return null;
        Assert.That(trial.Circuit, Is.SameAs(CourseDefinition.Tokyo));
        yield return new WaitUntil(() => trial.Rules.Elapsed > 0.1);
        Assert.That(trial.transform.Find(trial.Circuit.Name + "/Personal best ghost").gameObject.activeSelf, Is.False);
        Keys(Key.Escape);
        yield return null;
        yield return null;
        Assert.That(trial.Rules.Phase, Is.EqualTo(TrialPhase.Paused));
        Keys(Key.UpArrow);
        yield return null;
        yield return null;
        Keys(Key.Enter);
        yield return null;
        yield return null;
        Assert.That(trial.Rules, Is.Null);
        Assert.That(Time.timeScale, Is.EqualTo(2f));
    }

    [UnityTest, Timeout(180000)]
    public IEnumerator VirtualGamepadCompletesDocksYardQuayYardAndRetriesWithGhost()
    {
        SeedGhost(CourseDefinition.Tokyo);
        SeedGhost(CourseDefinition.Viaduct);
        string tokyo = Path.Combine(recordDirectory, "little-tokyo-skyport-r1.json");
        string viaduct = Path.Combine(recordDirectory, "concrete-wards-viaduct-r1.json");
        string tokyoBefore = File.ReadAllText(tokyo);
        string viaductBefore = File.ReadAllText(viaduct);

        string startupScene = SceneUtility.GetScenePathByBuildIndex(0);
        Assert.That(startupScene, Is.EqualTo(TimeTrial.MenuScenePath));
        yield return LoadScene(startupScene);
        for (int i = 0; i < 2; i++)
        {
            Pad(new GamepadState().WithButton(GamepadButton.DpadDown));
            yield return null;
            yield return null;
            Pad(default);
            yield return null;
        }
        Pad(new GamepadState().WithButton(GamepadButton.South));
        yield return null;
        yield return null;
        Pad(default);
        Assert.That(trial.Circuit, Is.SameAs(CourseDefinition.Docks));
        yield return new WaitUntil(() => trial.Rules.Phase == TrialPhase.Racing);

        yield return DriveLap(false, 1);
        yield return DriveLap(true, 2);
        yield return DriveLap(false, 3);
        Assert.That(trial.Rules.Phase, Is.EqualTo(TrialPhase.Finished));
        double elapsed = trial.Rules.Elapsed;
        double splitTotal = 0;
        for (int lap = 0; lap < TrialRules.LapCount; lap++)
        {
            Assert.That(trial.Rules.LapSplit(lap), Is.Positive);
            splitTotal += trial.Rules.LapSplit(lap);
        }
        Assert.That(splitTotal, Is.EqualTo(elapsed).Within(0.001));
        yield return null;
        Assert.That(Label("Details").text, Does.Contain("Lap 1").And.Contain("Lap 2").And.Contain("Lap 3"));
        Assert.That(Label("Record status").text, Does.Contain("NEW BEST LAP").And.Contain("NEW BEST TOTAL"));
        Assert.That(File.Exists(Path.Combine(recordDirectory, "stratos-docks-r1.json")), Is.True);
        TrialRecord savedRun = new TrialRecordStore(recordDirectory).Load(CourseDefinition.Docks);
        Assert.That(savedRun.BestTotal, Is.EqualTo(elapsed).Within(0.00001));
        Assert.That(System.Array.Exists(savedRun.Poses, pose => pose.Reset), Is.False);
        Assert.That(savedRun.Checkpoints, Has.Length.EqualTo(12));
        for (int i = 1; i < savedRun.Checkpoints.Length; i++)
            Assert.That(savedRun.Checkpoints[i], Is.GreaterThan(savedRun.Checkpoints[i - 1]));
        double yardFirst = savedRun.Checkpoints[3] - savedRun.Checkpoints[2];
        double quay = savedRun.Checkpoints[7] - savedRun.Checkpoints[6];
        double yardLast = savedRun.Checkpoints[11] - savedRun.Checkpoints[10];
        Assert.That(quay, Is.LessThan(System.Math.Min(yardFirst, yardLast)));
        Assert.That(File.ReadAllText(tokyo), Is.EqualTo(tokyoBefore));
        Assert.That(File.ReadAllText(viaduct), Is.EqualTo(viaductBefore));
        Debug.Log($"Stratos Docks finished in {elapsed:F2}s. Quay Approach to Finish: yard {yardFirst:F2}s, " +
            $"quay {quay:F2}s, yard {yardLast:F2}s.");

        Pad(new GamepadState().WithButton(GamepadButton.South));
        yield return null;
        yield return null;
        Pad(default);
        Assert.That(trial.Rules.Phase, Is.EqualTo(TrialPhase.Countdown));
        Assert.That(trial.Circuit, Is.SameAs(CourseDefinition.Docks));
        yield return new WaitUntil(() => trial.Rules.Elapsed > 0.1);
        Transform ghost = trial.transform.Find("Stratos Docks/Personal best ghost");
        Assert.That(ghost.gameObject.activeSelf, Is.True);
        Assert.That(ghost.position.x, Is.GreaterThan(CourseDefinition.Docks.Start.x));

        yield return DriveLap(true, 1, stopOnRoute: true);
        Assert.That(trial.Rules.LastGate, Is.EqualTo(2));
        var respawn = CourseDefinition.Docks.Respawn(2);
        Assert.That(Vector3.Distance(trial.KartBody.position, respawn.Position), Is.GreaterThan(30f));
        Pad(new GamepadState().WithButton(GamepadButton.North));
        yield return new WaitForSeconds(0.1f);
        Pad(default);
        Assert.That(Vector3.Distance(trial.KartBody.position, respawn.Position), Is.LessThan(0.25f));
        Assert.That(Vector3.Angle(trial.KartBody.rotation * Vector3.forward, respawn.Forward), Is.LessThan(1f));
        Assert.That(trial.Rules.LastGate, Is.EqualTo(2));
        Assert.That(trial.Rules.NextGate, Is.EqualTo(3));
        Assert.That(trial.Rules.Laps, Is.Zero);
        Assert.That(Label("Checkpoint delta").text, Does.StartWith("PB"));
    }

    [UnityTest]
    public IEnumerator AllTrackSelectControlsAreReachable()
    {
        string[] names = { "Little Tokyo–Skyport", "Concrete Wards Viaduct", "Stratos Docks", "Ghost" };
        for (int device = 0; device < 2; device++)
        {
            yield return LoadScene(TimeTrial.MenuScenePath);
            foreach (string name in names)
            {
                Assert.That(EventSystem.current.currentSelectedGameObject.name, Is.EqualTo(name));
                if (device == 0)
                    Keys(Key.DownArrow);
                else
                    Pad(new GamepadState().WithButton(GamepadButton.DpadDown));
                yield return null;
                yield return null;
                Keys();
                Pad(default);
                yield return null;
            }
            Assert.That(EventSystem.current.currentSelectedGameObject.name, Is.EqualTo(names[0]));
        }

        var mouse = InputSystem.AddDevice<Mouse>();
        try
        {
            foreach (CourseDefinition definition in CourseDefinition.All)
            {
                yield return LoadScene(TimeTrial.MenuScenePath);
                yield return ClickMenuButton(mouse, Label("Name", definition.Name));
                Assert.That(trial.Circuit, Is.SameAs(definition), $"Mouse click did not select {definition.Name}.");
            }
            yield return LoadScene(TimeTrial.MenuScenePath);
            yield return ClickMenuButton(mouse, Label("Label", "Ghost"));
            Assert.That(Label("Label", "Ghost").text, Is.EqualTo("Ghost   OFF"));
        }
        finally
        {
            InputSystem.RemoveDevice(mouse);
        }
    }

    private static IEnumerator ClickMenuButton(Mouse mouse, Text label)
    {
        Canvas.ForceUpdateCanvases();
        var button = (RectTransform)label.transform.parent;
        var corners = new Vector3[4];
        button.GetWorldCorners(corners);
        foreach (Vector3 corner in corners)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, corner);
            Assert.That(screen.x, Is.InRange(0f, (float)Screen.width));
            Assert.That(screen.y, Is.InRange(0f, (float)Screen.height));
        }
        Vector2 point = RectTransformUtility.WorldToScreenPoint(null, button.TransformPoint(button.rect.center));
        var state = new MouseState { position = point };
        InputSystem.QueueStateEvent(mouse, state);
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(mouse, state.WithButton(MouseButton.Left));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(mouse, state.WithButton(MouseButton.Left, false));
        yield return null;
        yield return null;
    }

    [UnityTest]
    public IEnumerator GhostUsesRaceClockAndCannotAffectPhysicsOrProgress()
    {
        CourseDefinition circuit = CourseDefinition.Tokyo;
        SeedGhost(circuit);
        yield return LoadScene(TimeTrial.ScenePath);
        Transform ghost = trial.transform.Find(circuit.Name + "/Personal best ghost");
        Assert.That(ghost.gameObject.activeSelf, Is.False);
        Assert.That(ghost.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
        Assert.That(ghost.GetComponentsInChildren<Collider>(true), Is.Empty);
        yield return new WaitUntil(() => trial.Rules.Elapsed >= 0.45);
        yield return null;
        Assert.That(ghost.gameObject.activeSelf, Is.True);
        Assert.That(ghost.position.x, Is.GreaterThan(5));
        Vector3 player = trial.KartBody.position;
        Assert.That(player.x, Is.EqualTo(circuit.Start.x).Within(0.02));
        Assert.That(trial.Rules.NextGate, Is.Zero);

        Keys(Key.Escape);
        yield return null;
        yield return null;
        double pausedAt = trial.Rules.Elapsed;
        Vector3 pausedPose = ghost.position;
        yield return new WaitForSecondsRealtime(0.12f);
        Assert.That(trial.Rules.Elapsed, Is.EqualTo(pausedAt));
        Assert.That(ghost.position, Is.EqualTo(pausedPose));
        Keys(Key.Enter);
        yield return null;
        yield return null;
        Assert.That(ghost.gameObject.activeSelf, Is.False);
        Assert.That(trial.Rules.Elapsed, Is.Zero);

        Keys();
        yield return new WaitUntil(() => trial.Rules.Elapsed >= 0.12);
        yield return null;
        Assert.That(ghost.gameObject.activeSelf, Is.True);
        Assert.That(ghost.position.x, Is.LessThan(pausedPose.x));
        Keys(Key.R);
        yield return new WaitUntil(() => trial.Rules.Elapsed >= 0.4);
        Assert.That(trial.Rules.NextGate, Is.Zero);
        Assert.That(ghost.position.x, Is.GreaterThan(circuit.Start.x));

        // Cross the first gate on each lap and check the delta against that lap's recorded split.
        Keys();
        var record = new TrialRecordStore(recordDirectory).Load(circuit);
        for (int lap = 0; lap < 2; lap++)
        {
            for (int gate = 0; gate < circuit.Gates.Length; gate++)
            {
                Checkpoint checkpoint = circuit.Gates[gate];
                Vector3 height = Vector3.up * 0.58f;
                trial.Rules.Rebase(checkpoint.Center - checkpoint.Forward + height);
                trial.KartBody.position = checkpoint.Center + checkpoint.Forward + height;
                yield return new WaitForFixedUpdate();
                double delta = trial.Rules.Elapsed - record.Checkpoints[lap * circuit.Gates.Length + gate];
                yield return null;
                Assert.That(Label("Checkpoint delta").text, Is.EqualTo(
                    $"PB  {(delta < 0 ? "−" : "+")}{System.Math.Abs(delta):0.000}s"));
            }
        }
        yield return new WaitUntil(() => trial.Rules.Elapsed >= record.BestTotal);
        yield return null;
        Assert.That(ghost.gameObject.activeSelf, Is.False);
        Assert.That(trial.Rules.Phase, Is.EqualTo(TrialPhase.Racing));
    }

    private void SeedGhost(CourseDefinition circuit)
    {
        var rules = new TrialRules(circuit.Gates, circuit.Start);
        rules.Tick(TrialRules.CountdownLength, circuit.Start);
        var times = new double[TrialRules.LapCount * circuit.Gates.Length];
        int index = 0;
        for (int lap = 0; lap < TrialRules.LapCount; lap++)
        {
            foreach (Checkpoint gate in circuit.Gates)
            {
                rules.Rebase(gate.Center - gate.Forward);
                rules.Tick(0.5, gate.Center + gate.Forward);
                times[index++] = rules.Elapsed;
            }
        }
        Quaternion rotation = Quaternion.LookRotation(circuit.StartForward);
        var poses = new[]
        {
            new GhostPose(0, circuit.Start, rotation),
            new GhostPose(1, circuit.Start + Vector3.right * 10, rotation),
            new GhostPose(1, circuit.Start, rotation, reset: true),
            new GhostPose(rules.Elapsed, circuit.Start + Vector3.right * 10, rotation)
        };
        Assert.That(new TrialRecordStore(recordDirectory).SaveResult(circuit, rules, times, poses),
            Is.EqualTo(RecordResult.Lap | RecordResult.Total));
    }

    private Text Label(string name, string parent = null) =>
        System.Array.Find(trial.GetComponentsInChildren<Text>(),
            text => text.name == name && (parent == null || text.transform.parent.name == parent));

    private IEnumerator LoadScene(string path)
    {
        if (trial)
            yield return SceneManager.UnloadSceneAsync(trial.gameObject.scene);
        yield return SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
        Scene scene = SceneManager.GetSceneByPath(path);
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.TryGetComponent(out trial))
                break;
        }
        Assert.That(trial, Is.Not.Null);
    }

    /// <summary>Follows course waypoints with stick and triggers while the real kart handles collisions.</summary>
    private IEnumerator DriveLap(bool shortcut, int lap, bool stopOnRoute = false)
    {
        Vector3[] line = TrialCourse.DrivingLine(trial.Circuit, shortcut);
        bool docks = trial.Circuit == CourseDefinition.Docks;
        int segment = 0;
        bool usedRoute = false;
        float waterfrontSpeed = 0f;
        double cutoff = trial.Rules.Elapsed + (docks ? 65 : 47);
        while (trial.Rules.Laps < lap && trial.Rules.Elapsed < cutoff)
        {
            if (stopOnRoute && usedRoute)
            {
                Pad(default);
                yield break;
            }
            Rigidbody body = trial.KartBody;
            var position = new Vector3(body.position.x, 0, body.position.z);
            for (int i = 0; i < 8 && segment < line.Length - 1; i++)
            {
                Vector3 vertex = line[segment + 1];
                Vector3 tangent = line[(segment + 2) % line.Length] - line[segment];
                if (Vector3.Dot(position - vertex, tangent) < 0)
                    break;
                segment++;
            }
            Vector3 from = line[segment], to = line[(segment + 1) % line.Length];
            Vector3 edge = to - from;
            float projection = Mathf.Clamp01(Vector3.Dot(position - from, edge) / edge.sqrMagnitude);
            Vector3 target = Vector3.Lerp(from, to, projection);
            float lookAhead = 3.8f;
            int ahead = segment;
            while (Vector3.Distance(target, to) < lookAhead)
            {
                lookAhead -= Vector3.Distance(target, to);
                target = to;
                to = line[(++ahead + 1) % line.Length];
            }
            target = Vector3.MoveTowards(target, to, lookAhead);
            Vector3 forward = body.rotation * Vector3.forward;
            float angle = Vector3.SignedAngle(forward, target - position, Vector3.up);
            float speed = Vector3.Dot(body.linearVelocity, forward);
            bool waterfront = docks && position.z < -60f && position.x is > 0f and < 49f;
            if (waterfront)
                waterfrontSpeed = Mathf.Max(waterfrontSpeed, speed);
            float desiredSpeed = Mathf.Lerp(waterfront ? 25f : 14f, 6f, Mathf.Abs(angle) / 37f);
            Pad(new GamepadState
            {
                leftStick = new Vector2(Mathf.Clamp(angle / 33f, -1, 1), 0),
                rightTrigger = Mathf.Clamp01(desiredSpeed / 25f + (desiredSpeed - speed) * 0.16f),
                leftTrigger = speed > desiredSpeed + 2.1f ? 0.75f : 0
            });
            if (docks)
            {
                Vector3 branchPoint = shortcut ? new(-91, 0, -12) : new(-61, 0, -12);
                usedRoute |= (position - branchPoint).sqrMagnitude < 9f && body.position.y > 0.3f;
            }
            else if (position.z is < -8 and > -20)
            {
                if (trial.Circuit == CourseDefinition.Viaduct)
                    usedRoute |= shortcut ? position.x is > -54 and < -44 : position.x < -61;
                else
                    usedRoute |= shortcut ? position.x is > -51 and < -39 : position.x < -54;
            }
            yield return new WaitForFixedUpdate();
        }
        Assert.That(trial.Rules.Laps, Is.EqualTo(lap),
            $"Lap {lap}, shortcut {shortcut}, segment {segment}/{line.Length}, kart {trial.KartBody.position}, gate {trial.Rules.NextGate}.");
        Assert.That(usedRoute, Is.True, $"Lap {lap} missed its route witness (shortcut {shortcut}).");
        if (docks)
        {
            Assert.That(waterfrontSpeed, Is.GreaterThanOrEqualTo(22f));
            Debug.Log($"Stratos Docks lap {lap} waterfront peak: {waterfrontSpeed:F2} m/s.");
        }
        Pad(default);
    }

    /// <summary>Waits for fixed-step charge after queued input reaches the player loop.</summary>
    private IEnumerator WaitForCharge()
    {
        float timeout = Time.realtimeSinceStartup + 2f;
        while (!trial.BoostReady && Time.realtimeSinceStartup < timeout)
            yield return null;
    }

    /// <summary>Queues a full keyboard state on the virtual device.</summary>
    private void Keys(params Key[] pressed) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(pressed));

    /// <summary>Queues a full controller state on the virtual device.</summary>
    private void Pad(GamepadState state) => InputSystem.QueueStateEvent(pad, state);
}
