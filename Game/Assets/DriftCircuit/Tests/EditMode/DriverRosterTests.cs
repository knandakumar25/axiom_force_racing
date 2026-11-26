using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace DriftCircuit.EditModeTests
{
    public sealed class DriverRosterTests
    {
        [Test]
        public void AllDriversMatchSixAxiomForceOperatives()
        {
            Assert.That(DriverProfile.All.Length, Is.EqualTo(6));
            var expectedNames = new[]
            {
                "Cyber Shogun",
                "Surya Prime",
                "Oceanus",
                "Mirage Blade",
                "Apex Titan",
                "Aura Phoenicia"
            };
            var ids = new HashSet<string>();
            for (int i = 0; i < DriverProfile.All.Length; i++)
            {
                DriverProfile driver = DriverProfile.All[i];
                Assert.That(driver.DisplayName, Is.EqualTo(expectedNames[i]));
                Assert.That(ids.Add(driver.Id), Is.True);
                Assert.That(driver.TopSpeed, Is.GreaterThan(0f));
                Assert.That(driver.BoostSpeed, Is.GreaterThan(driver.TopSpeed));
                Assert.That(driver.Acceleration, Is.GreaterThan(0f));
                Assert.That(driver.SteeringScale, Is.GreaterThan(0f));
                Assert.That(driver.DriftChargeRate, Is.GreaterThan(0f));
                Assert.That(driver.BoostDuration, Is.GreaterThan(0f));
            }
        }

        [Test]
        public void RivalForAlwaysSelectsDifferentDriver()
        {
            foreach (DriverProfile driver in DriverProfile.All)
            {
                DriverProfile rival = DriverProfile.RivalFor(driver);
                Assert.That(rival.Id, Is.Not.EqualTo(driver.Id));
            }
        }

        [Test]
        public void DriverStatsChangeTopSpeedAccelerationAndDriftCharge()
        {
            var objA = new GameObject("TitanKart");
            var objB = new GameObject("MirageKart");
            var objC = new GameObject("AuraKart");
            try
            {
                var titan = CreateKart(objA, DriverProfile.ApexTitan);
                var mirage = CreateKart(objB, DriverProfile.MirageBlade);
                var aura = CreateKart(objC, DriverProfile.AuraPhoenicia);

                // Short burst tests acceleration difference (Mirage > Titan).
                for (int i = 0; i < 20; i++)
                {
                    titan.Step(1f, 0f, 0f, false, true, 0.02f);
                    mirage.Step(1f, 0f, 0f, false, true, 0.02f);
                }
                Assert.That(mirage.ForwardSpeed, Is.GreaterThan(titan.ForwardSpeed));

                // Long straight tests top speed difference (Titan > Mirage).
                for (int i = 0; i < 200; i++)
                {
                    titan.Step(1f, 0f, 0f, false, true, 0.02f);
                    mirage.Step(1f, 0f, 0f, false, true, 0.02f);
                }
                Assert.That(titan.ForwardSpeed, Is.GreaterThan(mirage.ForwardSpeed));

                // Drift charge rate difference (Aura > Titan).
                // One non-drift step arms driftAllowed after Place().
                aura.Place(Vector3.zero, Vector3.forward);
                aura.Body.linearVelocity = Vector3.forward * 15f;
                titan.Place(Vector3.zero, Vector3.forward);
                titan.Body.linearVelocity = Vector3.forward * 15f;
                aura.Step(1f, 0f, 0f, false, true, 0.02f);
                titan.Step(1f, 0f, 0f, false, true, 0.02f);
                for (int i = 0; i < 21; i++)
                {
                    aura.Step(1f, 0f, 1f, true, true, 0.02f);
                    titan.Step(1f, 0f, 1f, true, true, 0.02f);
                }
                Assert.That(aura.BoostReady, Is.True);
                Assert.That(titan.BoostReady, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(objA);
                Object.DestroyImmediate(objB);
                Object.DestroyImmediate(objC);
            }
        }

        [Test]
        public void RecordStorePersistsDriverIdAndNameWithCircuitRecord()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "drift-driver-rec-" + System.Guid.NewGuid());
            try
            {
                var store = new TrialRecordStore(dir);
                var course = CourseDefinition.Tokyo;
                var rules = new TrialRules(course.Gates, course.Start);
                rules.Restart(course.Start);
                for (int i = 0; i < 31; i++) rules.Tick(0.1f, course.Start); // finish countdown

                var checkpoints = new double[course.Gates.Length * TrialRules.LapCount];
                double t = 0;
                for (int lap = 0; lap < TrialRules.LapCount; lap++)
                {
                    for (int g = 0; g < course.Gates.Length; g++)
                    {
                        rules.Tick(1.0f, course.Gates[g].Center);
                        t = rules.Elapsed;
                        checkpoints[lap * course.Gates.Length + g] = t;
                    }
                }
                var poses = new[] { new GhostPose(0, Vector3.zero, Quaternion.identity), new GhostPose(t, Vector3.forward, Quaternion.identity) };
                RecordResult saved = store.SaveResult(course, rules, checkpoints, poses,
                    DriverProfile.MirageBlade.Id, DriverProfile.MirageBlade.DisplayName);
                Assert.That(saved, Is.EqualTo(RecordResult.Lap | RecordResult.Total));

                TrialRecord loaded = store.Load(course);
                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded.DriverId, Is.EqualTo(DriverProfile.MirageBlade.Id));
                Assert.That(loaded.DriverName, Is.EqualTo(DriverProfile.MirageBlade.DisplayName));
            }
            finally
            {
                if (System.IO.Directory.Exists(dir))
                    System.IO.Directory.Delete(dir, true);
            }
        }

        private static TrialKart CreateKart(GameObject obj, DriverProfile driver)
        {
            var body = obj.AddComponent<Rigidbody>();
            var collider = obj.AddComponent<BoxCollider>();
            return new TrialKart(body, collider, driver);
        }
    }
}
