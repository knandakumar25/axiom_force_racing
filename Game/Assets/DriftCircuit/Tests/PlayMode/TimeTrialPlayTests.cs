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

    /// <summary>Checks the signals and actual collider at the clock boundaries with and without an occupying kart.</summary>
    [UnityTest]
    public IEnumerator RustrailCrossingFollowsFixedStepsAndBlocksOnlyTheBranch()
    {
        yield return SelectRustrail();
        AssertCrossing("OPEN", false);
        yield return new WaitUntil(() => trial.Rules.Phase == TrialPhase.Racing);
        yield return WaitForRaceTime(11.98);
        AssertCrossing("OPEN", false);
        yield return WaitForRaceTime(12);
        AssertCrossing("CLOSING", false);

        yield return PressPad(GamepadButton.Start);
        Assert.That(trial.Rules.Phase, Is.EqualTo(TrialPhase.Paused));
        double paused = trial.Rules.Elapsed;
        yield return new WaitForSecondsRealtime(0.2f);
        Assert.That(trial.Rules.Elapsed, Is.EqualTo(paused));
        AssertCrossing("CLOSING", false);
        yield return PressPad(GamepadButton.Start);

        // Only the nose overlaps the blocking volume at the closure boundary.
        yield return WaitForRaceTime(14.84);
        PlaceForCrossingCheck(new(-90, 0.52f, -10.7f), Vector3.back);
        yield return WaitForRaceTime(15);
        AssertCrossing("CLOSING", false);
        yield return new WaitForFixedUpdate();
        AssertCrossing("CLOSING", false);
        PlaceForCrossingCheck(new(-90, 0.52f, -7), Vector3.back);
        yield return new WaitForFixedUpdate();
        AssertCrossing("CLOSED", true);
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 end = RailCrossing.BarrierCenter + new Vector3(3.6f * side, -0.25f, 0);
            Collider[] meeting = Physics.OverlapBox(end, Vector3.one * 0.05f);
            Assert.That(System.Array.Exists(meeting, collider => collider.name == "Barrier"), Is.True,
                $"The crossing must meet the rail at its {side} end.");
        }

        for (int side = -1; side <= 1; side += 2)
        {
            PlaceForCrossingCheck(new(-90, 0.52f, -12 + side * 5), Vector3.back * side);
            Pad(new GamepadState { rightTrigger = 1 });
            yield return new WaitForSeconds(1.25f);
            float gap = (trial.KartBody.position.z + 12) * side;
            Assert.That(gap, Is.InRange(1.30f, 1.65f),
                $"Kart did not stop at the solid barrier from side {side}.");
        }

        PlaceForCrossingCheck(new(-66, 0.52f, 5.67f), new(24, 0, 4));
        yield return new WaitForSeconds(1.25f);
        Pad(default);
        Assert.That(trial.KartBody.position.x, Is.GreaterThan(-58f));
        AssertCrossing("CLOSED", true);
        yield return WaitForRaceTime(24);
        AssertCrossing("OPEN", false);

        // A kart still on the barrier at wraparound never gets trapped by a deferred closure.
        yield return WaitForRaceTime(38.9);
        PlaceForCrossingCheck(new(-90, 0.52f, -12), Vector3.back);
        yield return WaitForRaceTime(39);
        AssertCrossing("CLOSING", false);
        yield return WaitForRaceTime(48);
        AssertCrossing("OPEN", false);
        PlaceForCrossingCheck(new(-90, 0.52f, -7), Vector3.back);
        yield return WaitForRaceTime(63);
        AssertCrossing("CLOSED", true);
        yield return PressPad(GamepadButton.Start);
        yield return PressPad(GamepadButton.South);
        Assert.That(trial.Rules.Phase, Is.EqualTo(TrialPhase.Countdown));
        Assert.That(trial.Rules.Elapsed, Is.Zero);
        AssertCrossing("OPEN", false);
    }

    /// <summary>Drives both Rustrail routes, then verifies record isolation, ghost replay and shortcut reset.</summary>
    [UnityTest, Timeout(180000)]
    public IEnumerator VirtualGamepadCompletesRustrailSwitchbackCrossingSwitchback()
    {
        var otherRecords = new System.Collections.Generic.Dictionary<string, byte[]>();
        foreach (CourseDefinition circuit in CourseDefinition.All)
        {
            if (circuit == CourseDefinition.Rustrail)
                continue;
            SeedGhost(circuit);
            string path = Path.Combine(recordDirectory, circuit.Id + "-r1.json");
            otherRecords.Add(path, File.ReadAllBytes(path));
        }
        yield return SelectRustrail();
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

        Assert.That(File.Exists(Path.Combine(recordDirectory, "rustrail-junction-r1.json")), Is.True);
        TrialRecord saved = new TrialRecordStore(recordDirectory).Load(CourseDefinition.Rustrail);
        Assert.That(saved, Is.Not.Null);
        Assert.That(saved.BestTotal, Is.EqualTo(elapsed).Within(0.00001));
        Assert.That(saved.Checkpoints, Has.Length.EqualTo(12));
        for (int i = 1; i < saved.Checkpoints.Length; i++)
            Assert.That(saved.Checkpoints[i], Is.GreaterThan(saved.Checkpoints[i - 1]));
        Assert.That(System.Array.Exists(saved.Poses, pose => pose.Reset), Is.False);
        double first = saved.Checkpoints[3] - saved.Checkpoints[2];
        double crossing = saved.Checkpoints[7] - saved.Checkpoints[6];
        double last = saved.Checkpoints[11] - saved.Checkpoints[10];
        Assert.That(crossing, Is.LessThan(System.Math.Min(first, last)));
        foreach (var record in otherRecords)
            Assert.That(File.ReadAllBytes(record.Key), Is.EqualTo(record.Value));
        Debug.Log($"Rustrail Junction finished in {elapsed:F2}s. Crossing Approach to Finish: " +
            $"switchback {first:F2}s, crossing {crossing:F2}s, switchback {last:F2}s.");

        yield return null;
        Assert.That(Label("Details").text, Does.Contain("Lap 1").And.Contain("Lap 2").And.Contain("Lap 3"));
        Assert.That(EventSystem.current.currentSelectedGameObject.name, Is.EqualTo("Retry"));
        yield return PressPad(GamepadButton.South);
        Assert.That(trial.Rules.Phase, Is.EqualTo(TrialPhase.Countdown));
        AssertCrossing("OPEN", false);
        yield return new WaitUntil(() => trial.Rules.Elapsed > 0.2);
        Transform ghost = trial.transform.Find("Rustrail Junction/Personal best ghost");
        Assert.That(ghost.gameObject.activeSelf, Is.True);
        Assert.That(ghost.position.x, Is.GreaterThan(CourseDefinition.Rustrail.Start.x));

        yield return DriveLap(true, 1, stopOnRoute: true);
        Assert.That(trial.Rules.LastGate, Is.EqualTo(2));
        double beforeReset = trial.Rules.Elapsed;
        AssertCrossing("CLOSING", false);
        yield return PressPad(GamepadButton.North);
        yield return new WaitForFixedUpdate();
        var respawn = CourseDefinition.Rustrail.Respawn(2);
        Assert.That(Vector3.Distance(trial.KartBody.position, respawn.Position), Is.LessThan(0.25f));
        Assert.That(Vector3.Angle(trial.KartBody.rotation * Vector3.forward, respawn.Forward), Is.LessThan(1f));
        Assert.That(trial.Rules.Elapsed, Is.GreaterThan(beforeReset));
        Assert.That(trial.Rules.LastGate, Is.EqualTo(2));
        Assert.That(trial.Rules.NextGate, Is.EqualTo(3));
        Assert.That(trial.Rules.Laps, Is.Zero);
        AssertCrossing("CLOSING", false);
    }

    /// <summary>Opens Rustrail through the startup menu with the virtual gamepad.</summary>
    private IEnumerator SelectRustrail()
    {
        yield return LoadScene(TimeTrial.MenuScenePath);
        for (int i = 0; i < 3; i++)
            yield return PressPad(GamepadButton.DpadDown);
        yield return PressPad(GamepadButton.South);
        Assert.That(trial.Circuit, Is.SameAs(CourseDefinition.Rustrail));
    }

    /// <summary>Gives the input system time to receive a button press and release.</summary>
    private IEnumerator PressPad(GamepadButton button)
    {
        Pad(new GamepadState().WithButton(button));
        yield return null;
        yield return null;
        Pad(default);
        yield return null;
    }

    /// <summary>Waits for the runtime's millisecond boundary through real physics ticks.</summary>
    private IEnumerator WaitForRaceTime(double elapsed)
    {
        // Fixed steps originate as floats and can land a fraction of a millisecond below the target.
        while (trial.Rules.Elapsed + 0.0005 < elapsed)
            yield return new WaitForFixedUpdate();
    }

    /// <summary>Reads the live advance sign from the scene.</summary>
    private string CrossingState() =>
        trial.transform.Find("Rustrail Junction/Advance signal state").GetComponent<TextMesh>().text;

    /// <summary>Checks that both signs and the barrier agree about the phase.</summary>
    private void AssertCrossing(string state, bool solid)
    {
        Transform course = trial.transform.Find("Rustrail Junction");
        TextMesh entrance = course.Find("Entrance signal state").GetComponent<TextMesh>();
        Assert.That(CrossingState(), Is.EqualTo(state), $"Race time: {trial.Rules.Elapsed:F3}");
        Assert.That(entrance.text, Is.EqualTo(state));
        Assert.That(entrance.color, Is.EqualTo(course.Find("Advance signal state").GetComponent<TextMesh>().color));
        BoxCollider barrier = course.Find("Crossing barrier").GetComponent<BoxCollider>();
        Assert.That(barrier.enabled && barrier.gameObject.activeInHierarchy, Is.EqualTo(solid));
    }

    /// <summary>Stages a collision case without registering a checkpoint crossing or changing the race clock.</summary>
    private void PlaceForCrossingCheck(Vector3 position, Vector3 forward)
    {
        Rigidbody body = trial.KartBody;
        body.position = position;
        body.rotation = Quaternion.LookRotation(forward);
        body.transform.SetPositionAndRotation(position, body.rotation);
        body.linearVelocity = body.angularVelocity = Vector3.zero;
        trial.Rules.Rebase(position);
    }

    [UnityTest]
    public IEnumerator QuickRaceStartsTogetherPausesAndMakesPhysicalContact()
    {
        yield return SelectQuickRace();
        var rival = trial.Rival;
        Rigidbody other = rival.Kart.Body;
        Vector3 start = trial.Circuit.Start;
        Assert.That(trial.KartBody.position, Is.EqualTo(start + Vector3.back * 1.6f));
        Assert.That(other.position, Is.EqualTo(start + Vector3.forward * 1.6f));
        Assert.That(Vector3.Angle(other.rotation * Vector3.forward, Vector3.right), Is.LessThan(0.01f));
        while (trial.Rules.Phase == TrialPhase.Countdown)
        {
            Assert.That(other.isKinematic && trial.KartBody.isKinematic, Is.True);
            Assert.That(rival.Rules.Countdown, Is.EqualTo(trial.Rules.Countdown));
            yield return new WaitForFixedUpdate();
        }
        Assert.That(rival.Rules.Phase, Is.EqualTo(TrialPhase.Racing));
        Assert.That(trial.Rules.Elapsed, Is.Zero);
        Assert.That(rival.Rules.Elapsed, Is.Zero);
        Assert.That(other.isKinematic || trial.KartBody.isKinematic, Is.False);

        yield return PressPad(GamepadButton.Start);
        Assert.That(rival.Rules.Phase, Is.EqualTo(TrialPhase.Paused));
        Vector3 stopped = other.position;
        double pausedAt = rival.Rules.Elapsed;
        yield return new WaitForSecondsRealtime(0.1f);
        Assert.That(other.position, Is.EqualTo(stopped));
        Assert.That(rival.Rules.Elapsed, Is.EqualTo(pausedAt));
        yield return PressPad(GamepadButton.Start);
        Assert.That(rival.Rules.Phase, Is.EqualTo(TrialPhase.Racing));
        Assert.That(rival.Rules.Elapsed, Is.EqualTo(trial.Rules.Elapsed));

        PlaceForCrossingCheck(start + Vector3.right * 6f, Vector3.right);
        rival.Kart.Place(start + Vector3.right * 8.55f, Vector3.right);
        rival.Rules.Rebase(other.position);
        trial.KartBody.linearVelocity = Vector3.right * 12f;
        for (int step = 0; step < 5 && other.linearVelocity.x <= 2f; step++)
            yield return new WaitForFixedUpdate();
        Assert.That(other.linearVelocity.x, Is.GreaterThan(2f), "The player should transfer momentum to the rival.");
        Assert.That(trial.KartBody.linearVelocity.x, Is.LessThan(10f));

        yield return PressPad(GamepadButton.Start);
        yield return PressPad(GamepadButton.DpadUp);
        yield return PressPad(GamepadButton.South);
        Assert.That(trial.Rules, Is.Null);
        Assert.That(trial.Rival, Is.Null);
        Assert.That(other == null || !other.gameObject.activeInHierarchy, Is.True);
        Assert.That(Time.timeScale, Is.EqualTo(2f));
    }

    [UnityTest]
    public IEnumerator QuickRaceRecoversBothKartsAndWaitsForFreePlacement()
    {
        yield return SelectQuickRace();
        yield return new WaitUntil(() => trial.Rules.Phase == TrialPhase.Racing);
        TrialRival rival = trial.Rival;
        Rigidbody other = rival.Kart.Body;
        Checkpoint gate = trial.Circuit.Gates[0];
        var respawn = trial.Circuit.Respawn(0);
        StageGate(trial.KartBody, trial.Rules, gate, 0);
        StageGate(other, rival.Rules, gate, 3.5f);
        yield return new WaitForFixedUpdate();
        Assert.That(trial.Rules.LastGate, Is.Zero);
        Assert.That(rival.Rules.LastGate, Is.Zero);

        PlaceForCrossingCheck(respawn.Position, respawn.Forward);
        BoxCollider blocker = trial.KartBody.GetComponent<BoxCollider>();
        Vector3 originalSize = blocker.size;
        blocker.size = new Vector3(8f, originalSize.y, originalSize.z);
        other.position = respawn.Position - Vector3.up * 3f;
        double before = rival.Rules.Elapsed;
        yield return new WaitForFixedUpdate();
        Assert.That(other.position.y, Is.LessThan(-1.35f));
        Assert.That(rival.Recoveries, Is.Zero);
        blocker.size = originalSize;
        yield return new WaitForFixedUpdate();
        Assert.That(other.position.x, Is.EqualTo(respawn.Position.x + 2.4f).Within(0.08f));
        Assert.That(rival.Recoveries, Is.EqualTo(1));
        Assert.That(rival.Rules.LastGate, Is.Zero);
        Assert.That(rival.Rules.NextGate, Is.EqualTo(1));
        Assert.That(rival.Rules.Elapsed, Is.GreaterThan(before));
        yield return new WaitForSeconds(0.5f);
        Assert.That(other.position.z, Is.GreaterThan(respawn.Position.z + 0.5f),
            "The rival should drive toward the next checkpoint after rebasing its line cursor.");

        rival.Kart.Place(respawn.Position, respawn.Forward);
        rival.Rules.Rebase(other.position);
        trial.KartBody.position = respawn.Position - Vector3.up * 3f;
        before = trial.Rules.Elapsed;
        yield return new WaitForFixedUpdate();
        Assert.That(trial.KartBody.position.x, Is.EqualTo(respawn.Position.x + 2.4f).Within(0.08f));
        Assert.That(trial.Rules.LastGate, Is.Zero);
        Assert.That(trial.Rules.NextGate, Is.EqualTo(1));
        Assert.That(trial.Rules.Elapsed, Is.GreaterThan(before));

        // Pin the rival while its driver calls for forward movement.
        other.constraints = RigidbodyConstraints.FreezeAll;
        yield return new WaitUntil(() => rival.Recoveries == 2 || rival.Rules.Elapsed > before + 3f);
        Assert.That(rival.Recoveries, Is.EqualTo(2));
        Assert.That(rival.Rules.NextGate, Is.EqualTo(1));
    }

    [UnityTest, Timeout(180000)]
    public IEnumerator QuickRaceRivalFinishesThreeYardLapsWithNoPlayerInput()
    {
        yield return SelectQuickRace();
        yield return new WaitUntil(() => trial.Rules.Phase == TrialPhase.Racing);
        TrialRival rival = trial.Rival;
        Material tailLights = rival.Kart.Body.transform.Find("Rear lights").GetComponent<Renderer>().sharedMaterial;
        bool brakedInChicane = false;
        int yardLaps = 0;
        while (rival.Rules.Phase == TrialPhase.Racing && trial.Rules.Elapsed <= 175)
        {
            Vector3 position = rival.Kart.Body.position;
            Vector2 flat = new(position.x, position.z);
            if (position.y > 0.3f && (flat - new Vector2(-61, -12)).sqrMagnitude < 9f)
                yardLaps |= 1 << rival.Rules.Laps;
            if (position.x > 70f && position.z is > -26f and < 46f)
                brakedInChicane |= tailLights.color.r > 0.85f;
            Assert.That((flat - new Vector2(-91, -12)).sqrMagnitude, Is.GreaterThanOrEqualTo(9f));
            yield return new WaitForFixedUpdate();
        }
        Assert.That(rival.Rules.Laps, Is.EqualTo(3),
            $"Rival position {rival.Kart.Body.position}, gate {rival.Rules.NextGate}, recoveries {rival.Recoveries}.");
        Assert.That(yardLaps, Is.EqualTo(0b111));
        Assert.That(brakedInChicane, Is.True, "The rival's brake light should be visible in the chicane.");
        Assert.That(rival.Recoveries, Is.Zero);
        Assert.That(rival.Rules.Elapsed, Is.LessThanOrEqualTo(175));
        Assert.That(rival.Kart.Body.gameObject.activeInHierarchy, Is.False);
        Assert.That(trial.Rules.Phase, Is.EqualTo(TrialPhase.Racing));
        double rivalTime = rival.Rules.Elapsed;
        Debug.Log($"Quick Race rival completed three loading-yard laps in {rivalTime:F2}s with zero recoveries.");

        for (int i = 0; i < trial.Circuit.Gates.Length * TrialRules.LapCount; i++)
        {
            StageGate(trial.KartBody, trial.Rules, trial.Rules.ExpectedGate, 0);
            yield return new WaitForFixedUpdate();
        }
        yield return null;
        Assert.That(trial.RaceResult.Value.PlayerPlace, Is.EqualTo(2));
        Assert.That(trial.RaceResult.Value.RivalTime, Is.EqualTo(rivalTime));
        Assert.That(Label("Details").text, Does.StartWith("1    Rival").And.Contain("2    Player"));
        yield return PressPad(GamepadButton.South);
        Assert.That(trial.RaceResult, Is.Null);
        Assert.That(trial.Rules.Phase, Is.EqualTo(TrialPhase.Countdown));
        Assert.That(rival.Rules.Phase, Is.EqualTo(TrialPhase.Countdown));
        Assert.That(trial.Rules.Elapsed + rival.Rules.Elapsed, Is.Zero);
        Assert.That(trial.Rules.Laps + rival.Rules.Laps, Is.Zero);
        Assert.That(rival.Kart.Body.gameObject.activeInHierarchy, Is.True);
    }

    [UnityTest, Timeout(180000)]
    public IEnumerator QuickRaceVirtualGamepadTakesQuayAndPreservesTimeTrialFiles()
    {
        PlayerPrefs.SetInt(TimeTrial.GhostPreference, 1);
        var startingFiles = new System.Collections.Generic.Dictionary<string, byte[]>();
        foreach (CourseDefinition definition in CourseDefinition.All)
        {
            SeedGhost(definition, gateInterval: 60);
            string path = Path.Combine(recordDirectory, $"{definition.Id}-r{definition.Revision}.json");
            startingFiles.Add(path, File.ReadAllBytes(path));
        }

        yield return SelectQuickRace();
        yield return new WaitUntil(() => trial.Rules.Phase == TrialPhase.Racing);
        TrialRival rival = trial.Rival;
        int yardLaps = 0;
        void WatchRival()
        {
            Vector3 position = rival.Kart.Body.position;
            Vector2 flat = new(position.x, position.z);
            if (position.y > 0.3f && (flat - new Vector2(-61, -12)).sqrMagnitude < 9f)
                yardLaps |= 1 << rival.Rules.Laps;
            Assert.That((flat - new Vector2(-91, -12)).sqrMagnitude, Is.GreaterThanOrEqualTo(9f));
        }
        yield return DriveLap(false, 1, observe: WatchRival);
        yield return DriveLap(true, 2, observe: WatchRival);
        yield return DriveLap(false, 3, observe: WatchRival);
        yield return null;

        Assert.That(trial.Rules.Phase, Is.EqualTo(TrialPhase.Finished));
        Assert.That(yardLaps & 0b011, Is.EqualTo(0b011));
        Assert.That(trial.RaceResult.Value.PlayerPlace, Is.EqualTo(1),
            $"Player {trial.Rules.Elapsed:F2}s. Rival {rival.Rules.Elapsed:F2}s, laps {rival.Rules.Laps}.");
        Assert.That(trial.RaceResult.Value.RivalPlace, Is.EqualTo(2));
        Assert.That(trial.RaceResult.Value.RivalTime, Is.Null);
        Assert.That(Label("Details").text, Does.StartWith("1    Player").And.Contain("Still racing"));
        Assert.That(Label("Checkpoint delta").text, Is.Empty);
        Assert.That(trial.transform.Find("Stratos Docks/Personal best ghost"), Is.Null);
        Assert.That(PlayerPrefs.GetInt(TimeTrial.GhostPreference), Is.EqualTo(1));
        foreach (var file in startingFiles)
            Assert.That(File.ReadAllBytes(file.Key), Is.EqualTo(file.Value), file.Key);
        Debug.Log($"Quick Race player finished yard, quay, yard in {trial.Rules.Elapsed:F2}s.");
    }

    [UnityTest]
    public IEnumerator QuickRaceSameTickFinishAwardsSharedFirst()
    {
        yield return SelectQuickRace();
        yield return new WaitUntil(() => trial.Rules.Phase == TrialPhase.Racing);
        TrialRival rival = trial.Rival;
        for (int i = 0; i < trial.Circuit.Gates.Length * TrialRules.LapCount; i++)
        {
            StageGate(trial.KartBody, trial.Rules, trial.Rules.ExpectedGate, 1.6f);
            StageGate(rival.Kart.Body, rival.Rules, rival.Rules.ExpectedGate, -1.6f);
            yield return new WaitForFixedUpdate();
        }
        yield return null;
        Assert.That(trial.RaceResult.Value.PlayerPlace, Is.EqualTo(1));
        Assert.That(trial.RaceResult.Value.RivalPlace, Is.EqualTo(1));
        Assert.That(trial.RaceResult.Value.RivalTime, Is.EqualTo(trial.RaceResult.Value.PlayerTime));
        Assert.That(Label("Details").text, Does.Contain("Shared first"));
    }

    private IEnumerator SelectQuickRace()
    {
        yield return LoadScene(TimeTrial.MenuScenePath);
        for (int i = 0; i < CourseDefinition.All.Length; i++)
            yield return PressPad(GamepadButton.DpadDown);
        yield return PressPad(GamepadButton.South);
        Assert.That(trial.Circuit, Is.SameAs(CourseDefinition.Docks));
        Assert.That(trial.Rival, Is.Not.Null);
        Assert.That(Label("Circuit").text, Is.EqualTo("QUICK RACE · STRATOS DOCKS"));
    }

    private static void StageGate(Rigidbody body, TrialRules rules, Checkpoint gate, float lane)
    {
        Vector3 center = gate.Center + Vector3.up * 0.58f + Vector3.Cross(Vector3.up, gate.Forward) * lane;
        rules.Rebase(center - gate.Forward);
        body.position = center + gate.Forward;
        body.rotation = Quaternion.LookRotation(gate.Forward);
        body.linearVelocity = body.angularVelocity = Vector3.zero;
    }

    [UnityTest]
    public IEnumerator AllTrackSelectControlsAreReachable()
    {
        string[] names = { "Little Tokyo–Skyport", "Concrete Wards Viaduct", "Stratos Docks", "Rustrail Junction",
            TrialHud.QuickRaceName, "Ghost" };
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
            yield return ClickMenuButton(mouse, Label("Label", TrialHud.QuickRaceName));
            Assert.That(trial.Rival, Is.Not.Null);
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

    private void SeedGhost(CourseDefinition circuit, double gateInterval = 0.5)
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
                rules.Tick(gateInterval, gate.Center + gate.Forward);
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
    private IEnumerator DriveLap(bool shortcut, int lap, bool stopOnRoute = false, System.Action observe = null)
    {
        Vector3[] line = TrialCourse.DrivingLine(trial.Circuit, shortcut);
        bool docks = trial.Circuit == CourseDefinition.Docks;
        bool rustrail = trial.Circuit == CourseDefinition.Rustrail;
        bool alignRustrail = rustrail && (shortcut || lap == 1);
        double departure = shortcut ? 8.25 : 15.2;
        bool aligned = false;
        bool passedClosedFork = false;
        int segment = 0;
        bool usedRoute = false;
        float waterfrontSpeed = 0f;
        double cutoff = trial.Rules.Elapsed + (rustrail ? 90 : docks ? 65 : 47);
        while (trial.Rules.Laps < lap && trial.Rules.Elapsed < cutoff)
        {
            observe?.Invoke();
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
            if (trial.Rival != null && target.z < -62f && target.x is > -15f and < 55f)
                target.z -= TrialRival.GridOffset * Mathf.Clamp01(Mathf.Min((target.x + 15f) / 10f, (55f - target.x) / 15f));
            Vector3 forward = body.rotation * Vector3.forward;
            float angle = Vector3.SignedAngle(forward, target - position, Vector3.up);
            float speed = Vector3.Dot(body.linearVelocity, forward);
            if (alignRustrail && !aligned && trial.Rules.NextGate == 2 && position.z < 56f)
            {
                Assert.That(position.z, Is.GreaterThan(43f), "The wait must finish before the timing gate.");
                if (speed > 0.45f)
                    Pad(new GamepadState { leftTrigger = 1f });
                else
                {
                    Pad(default);
                    // Reach the branch during its warning or take the switchback with the barrier down.
                    double cycle = trial.Rules.Elapsed % 24;
                    aligned = cycle >= departure && cycle < departure + 0.1;
                }
                yield return new WaitForFixedUpdate();
                continue;
            }
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
            if (rustrail)
            {
                if (!shortcut && lap == 1 && !passedClosedFork && trial.Rules.NextGate == 3 && position.z < 20f)
                {
                    AssertCrossing("CLOSED", true);
                    passedClosedFork = true;
                }
                Vector3 branchPoint = shortcut ? new(-90, 0, -12) : new(-46, 0, -2);
                if (!usedRoute && (position - branchPoint).sqrMagnitude < 9f && body.position.y > 0.3f)
                {
                    usedRoute = true;
                    if (shortcut)
                        Assert.That(CrossingState(), Is.EqualTo("CLOSING"));
                }
            }
            else if (docks)
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
        if (rustrail && !shortcut && lap == 1)
            Assert.That(passedClosedFork, Is.True);
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
