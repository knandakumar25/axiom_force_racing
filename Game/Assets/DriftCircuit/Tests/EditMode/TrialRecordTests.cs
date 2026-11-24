using System;
using System.IO;
using DriftCircuit;
using NUnit.Framework;
using UnityEngine;

public sealed class TrialRecordTests
{
    private string directory;
    private TrialRecordStore store;

    [SetUp]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), "drift-record-tests-" + Guid.NewGuid());
        store = new TrialRecordStore(directory);
    }

    [TearDown]
    public void CleanUp()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    [Test]
    public void BestLapAndTotalAreIndependentAndScopedToTheCircuit()
    {
        CourseDefinition tokyo = CourseDefinition.Tokyo, viaduct = CourseDefinition.Viaduct;
        Assert.That(store.Load(tokyo), Is.Null);
        Assert.That(Save(tokyo, 10, 11, 11), Is.EqualTo(RecordResult.Lap | RecordResult.Total));
        TrialRecord first = store.Load(tokyo);
        Assert.That(first.BestTotal, Is.EqualTo(32));
        Assert.That(first.BestLap, Is.EqualTo(10));
        Assert.That(store.Load(viaduct), Is.Null);

        Assert.That(Save(tokyo, 8, 16, 15), Is.EqualTo(RecordResult.Lap));
        TrialRecord lapOnly = store.Load(tokyo);
        Assert.That(lapOnly.BestLap, Is.EqualTo(8));
        Assert.That(lapOnly.BestTotal, Is.EqualTo(32));
        Assert.That(lapOnly.Checkpoints, Is.EqualTo(first.Checkpoints));
        Assert.That(lapOnly.Poses, Is.EqualTo(first.Poses));

        Assert.That(Save(tokyo, 9, 10, 10), Is.EqualTo(RecordResult.Total));
        TrialRecord totalOnly = store.Load(tokyo);
        Assert.That(totalOnly.BestLap, Is.EqualTo(8));
        Assert.That(totalOnly.BestTotal, Is.EqualTo(29));
        Assert.That(totalOnly.Checkpoints, Is.Not.EqualTo(first.Checkpoints));
        Assert.That(totalOnly.Poses[^1].Time, Is.EqualTo(29));
        Assert.That(Save(tokyo, 9, 10, 10), Is.EqualTo(RecordResult.None));

        Assert.That(Save(viaduct, 20, 21, 22), Is.EqualTo(RecordResult.Lap | RecordResult.Total));
        Assert.That(store.Load(viaduct).BestTotal, Is.EqualTo(63));
        Assert.That(store.Load(tokyo).BestTotal, Is.EqualTo(29));
    }

    [Test]
    public void InvalidRecordsRecoverAndPartialRunsDoNotWrite()
    {
        CourseDefinition course = CourseDefinition.Tokyo;
        string path = Path.Combine(directory, $"{course.Id}-r{course.Revision}.json");
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, "{ malformed");
        Assert.That(store.Load(course), Is.Null);
        var unfinished = new TrialRules(course.Gates, course.Start);
        Assert.That(store.SaveResult(course, unfinished, null, null), Is.EqualTo(RecordResult.None));
        Assert.That(File.ReadAllText(path), Is.EqualTo("{ malformed"));
        Assert.That(Save(course, 12, 13, 14), Is.EqualTo(RecordResult.Lap | RecordResult.Total));

        TrialRecord record = store.Load(course);
        record.Revision++;
        File.WriteAllText(path, JsonUtility.ToJson(record));
        Assert.That(store.Load(course), Is.Null);

        record.Revision = course.Revision;
        record.BestLap = 20;
        File.WriteAllText(path, JsonUtility.ToJson(record));
        Assert.That(store.Load(course), Is.Null);

        record.BestLap = 12;
        record.Checkpoints[1] = record.Checkpoints[0];
        File.WriteAllText(path, JsonUtility.ToJson(record));
        Assert.That(store.Load(course), Is.Null);
    }

    [Test]
    public void FailedWriteReportsFailureAndCleansTheTemporaryFile()
    {
        CourseDefinition course = CourseDefinition.Tokyo;
        string path = Path.Combine(directory, $"{course.Id}-r{course.Revision}.json");
        Directory.CreateDirectory(path);
        Assert.That(Save(course, 12, 13, 14), Is.EqualTo(RecordResult.SaveFailed));
        Assert.That(Directory.Exists(path), Is.True);
        Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
    }

    [Test]
    public void AnotherWriterCannotEraseAnExistingRecord()
    {
        CourseDefinition course = CourseDefinition.Tokyo;
        Assert.That(Save(course, 12, 13, 14), Is.EqualTo(RecordResult.Lap | RecordResult.Total));
        string lockPath = Path.Combine(directory, TrialRecordStore.WriteLockName);
        using (File.Open(lockPath, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            Assert.That(Save(course, 9, 10, 11), Is.EqualTo(RecordResult.SaveFailed));
            Assert.That(store.Load(course).BestTotal, Is.EqualTo(39));
        }
        Assert.That(Save(course, 9, 10, 11), Is.EqualTo(RecordResult.Lap | RecordResult.Total));
        Assert.That(store.Load(course).BestTotal, Is.EqualTo(30));
    }

    [Test]
    public void PlaybackInterpolatesAndSnapsAtResets()
    {
        var poses = new[]
        {
            new GhostPose(0, Vector3.zero, Quaternion.identity),
            new GhostPose(1, Vector3.right * 10, Quaternion.Euler(0, 90, 0)),
            new GhostPose(1, Vector3.left * 10, Quaternion.identity, reset: true),
            new GhostPose(2, Vector3.zero, Quaternion.identity),
            new GhostPose(3, Vector3.forward, Quaternion.identity, reset: true)
        };
        GhostPose moving = TrialGhost.Sample(poses, 0.5);
        Assert.That(moving.Position, Is.EqualTo(Vector3.right * 5));
        Assert.That(Quaternion.Angle(moving.Rotation, Quaternion.Euler(0, 45, 0)), Is.LessThan(0.01f));
        Assert.That(TrialGhost.Sample(poses, 1).Position, Is.EqualTo(Vector3.left * 10));
        Assert.That(TrialGhost.Sample(poses, 1.5).Position, Is.EqualTo(Vector3.left * 5));
        Assert.That(TrialGhost.Sample(poses, 2.9).Position, Is.EqualTo(Vector3.zero));
        Assert.That(TrialGhost.Sample(poses, 3).Position, Is.EqualTo(Vector3.forward));
    }

    private RecordResult Save(CourseDefinition course, params double[] laps)
    {
        var rules = new TrialRules(course.Gates, course.Start);
        rules.Tick(TrialRules.CountdownLength, course.Start);
        var times = new double[TrialRules.LapCount * course.Gates.Length];
        int index = 0;
        foreach (double lap in laps)
        {
            foreach (Checkpoint gate in course.Gates)
            {
                rules.Rebase(gate.Center - gate.Forward);
                rules.Tick(lap / course.Gates.Length, gate.Center + gate.Forward);
                times[index++] = rules.Elapsed;
            }
        }
        return store.SaveResult(course, rules, times, new[]
        {
            new GhostPose(0, course.Start, Quaternion.LookRotation(course.StartForward)),
            new GhostPose(rules.Elapsed, course.Gates[^1].Center, Quaternion.LookRotation(course.Gates[^1].Forward))
        });
    }
}
