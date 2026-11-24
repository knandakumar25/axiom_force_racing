using System;
using System.IO;
using UnityEngine;

namespace DriftCircuit
{
    [Serializable]
    internal struct GhostPose
    {
        public double Time;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool Reset;

        public GhostPose(double time, Vector3 position, Quaternion rotation, bool reset = false)
        {
            Time = time;
            Position = position;
            Rotation = rotation;
            Reset = reset;
        }
    }

    [Serializable]
    internal sealed class TrialRecord
    {
        public int Version;
        public string Circuit;
        public int Revision;
        public double BestLap, BestTotal;
        public double[] Checkpoints;
        public GhostPose[] Poses;
    }

    [Flags]
    internal enum RecordResult { None = 0, Lap = 1, Total = 2, SaveFailed = 4 }

    internal sealed class TrialRecordStore
    {
        private const int Version = 1;
        internal const string WriteLockName = "records.lock";
        private readonly string directory;

        public TrialRecordStore(string directory) => this.directory = directory;

        private string PathFor(CourseDefinition course) =>
            Path.Combine(directory, $"{course.Id}-r{course.Revision}.json");

        public TrialRecord Load(CourseDefinition course)
        {
            try { return Read(course); }
            catch (Exception error) when (IsStorageError(error)) { return null; }
        }

        private TrialRecord Read(CourseDefinition course)
        {
            try
            {
                using var file = File.Open(PathFor(course), FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete);
                using var reader = new StreamReader(file);
                var record = JsonUtility.FromJson<TrialRecord>(reader.ReadToEnd());
                return IsValid(course, record) ? record : null;
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException or ArgumentException)
            {
                return null;
            }
        }

        public RecordResult SaveResult(CourseDefinition course, TrialRules rules, double[] checkpoints,
            GhostPose[] poses)
        {
            if (rules.Phase != TrialPhase.Finished || rules.Laps != TrialRules.LapCount)
                return RecordResult.None;

            try
            {
                Directory.CreateDirectory(directory);
                // Hold an OS file handle across the read, merge and replace so another
                // instance cannot publish an update based on an older record.
                using var writer = File.Open(Path.Combine(directory, WriteLockName), FileMode.OpenOrCreate,
                    FileAccess.Write, FileShare.None);
                return SaveLocked(course, rules, checkpoints, poses);
            }
            catch (Exception error) when (IsStorageError(error))
            {
                return RecordResult.SaveFailed;
            }
        }

        private RecordResult SaveLocked(CourseDefinition course, TrialRules rules, double[] checkpoints, GhostPose[] poses)
        {
            TrialRecord previous = Read(course);
            double fastestLap = rules.LapSplit(0);
            for (int lap = 1; lap < TrialRules.LapCount; lap++)
                fastestLap = Math.Min(fastestLap, rules.LapSplit(lap));

            bool lapRecord = previous == null || fastestLap < previous.BestLap;
            bool totalRecord = previous == null || rules.Elapsed < previous.BestTotal;
            if (!lapRecord && !totalRecord)
                return RecordResult.None;

            var record = new TrialRecord
            {
                Version = Version,
                Circuit = course.Id,
                Revision = course.Revision,
                BestLap = lapRecord ? fastestLap : previous.BestLap,
                BestTotal = totalRecord ? rules.Elapsed : previous.BestTotal,
                Checkpoints = totalRecord ? checkpoints : previous.Checkpoints,
                Poses = totalRecord ? poses : previous.Poses
            };
            if (!IsValid(course, record))
                return RecordResult.SaveFailed;
            Write(course, record);

            return (lapRecord ? RecordResult.Lap : RecordResult.None)
                | (totalRecord ? RecordResult.Total : RecordResult.None);
        }

        private void Write(CourseDefinition course, TrialRecord record)
        {
            string path = PathFor(course);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(record));
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (Exception error) when (IsStorageError(error)) { }
            }
        }

        private static bool IsStorageError(Exception error) =>
            error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException;

        private static bool IsValid(CourseDefinition course, TrialRecord record)
        {
            if (record == null || record.Version != Version || record.Circuit != course.Id
                || record.Revision != course.Revision || !double.IsFinite(record.BestTotal)
                || !double.IsFinite(record.BestLap) || record.BestLap <= 0
                || record.BestTotal <= record.BestLap
                || record.Checkpoints?.Length != course.Gates.Length * TrialRules.LapCount
                || record.Poses == null || record.Poses.Length < 2)
                return false;

            double previous = 0;
            foreach (double time in record.Checkpoints)
            {
                if (!double.IsFinite(time) || time <= previous)
                    return false;
                previous = time;
            }
            if (previous != record.BestTotal || record.Poses[0].Time != 0 || record.Poses[0].Reset)
                return false;

            double lapStart = 0;
            for (int finish = course.Gates.Length - 1; finish < record.Checkpoints.Length; finish += course.Gates.Length)
            {
                double lapFinish = record.Checkpoints[finish];
                if (record.BestLap - (lapFinish - lapStart) > 0.000001)
                    return false;
                lapStart = lapFinish;
            }

            previous = -1;
            foreach (GhostPose pose in record.Poses)
            {
                float rotationNorm = Quaternion.Dot(pose.Rotation, pose.Rotation);
                if (!double.IsFinite(pose.Time) || pose.Time < previous || pose.Time > record.BestTotal
                    || (pose.Time == previous && !pose.Reset)
                    || !float.IsFinite(pose.Position.sqrMagnitude)
                    || !float.IsFinite(rotationNorm) || Mathf.Abs(rotationNorm - 1f) > 0.01f)
                    return false;
                previous = pose.Time;
            }
            return previous == record.BestTotal;
        }
    }
}
