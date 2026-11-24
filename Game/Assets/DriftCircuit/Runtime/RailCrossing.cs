using System;
using UnityEngine;

namespace DriftCircuit
{
    /// <summary>State shared by the barrier and the two road signs.</summary>
    internal enum CrossingPhase { Open, Closing, Closed }

    /// <summary>Keeps the Rustrail shortcut barrier and both approach signals in sync.</summary>
    internal sealed class RailCrossing
    {
        internal static readonly Vector3 BarrierCenter = new(-90, 0.65f, -12);
        private static readonly Vector3 BarrierSize = new(7.2f, 1.3f, 0.55f);
        private static readonly Color[] SignalColors =
        {
            new(0.39f, 1f, 0.71f), new(1f, 0.82f, 0.32f), new(1f, 0.48f, 0.43f)
        };

        private readonly GameObject barrier;
        private readonly Bounds barrierBounds = new(BarrierCenter, BarrierSize);
        private readonly TextMesh advance, entrance;
        private CrossingPhase phase = (CrossingPhase)(-1);

        /// <summary>Builds the branch barrier and roadside gantries from runtime primitives.</summary>
        internal RailCrossing(TrialCourse course, Material board, Material support)
        {
            Material red = course.Paint(0xff473f, true);
            Material cream = course.Paint(0xffefde, true);
            barrier = course.Cube("Crossing barrier", BarrierCenter, BarrierSize, Quaternion.identity, red, true);
            for (int i = -2; i <= 2; i++)
                course.Cube("Barrier stripe", new(i * 0.19f, 0, 0), new(0.065f, 1.02f, 1.02f),
                    Quaternion.identity, cream, parent: barrier.transform);

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // Raise the advance board above the entrance text in the chase sightline.
            Vector3 advancePosition = new(-84, 7.9f, 37);
            BuildGantry(course, "Advance signal", -91.7f, -76.3f, advancePosition.y, advancePosition.z, support);
            BuildGantry(course, "Fork signal", -94.3f, -73.6f, 4.4f, 17, support);
            advance = BuildSignal(course, "Advance signal", "RAIL CROSSING", advancePosition, board, font);
            entrance = BuildSignal(course, "Entrance signal", "CROSSING RIGHT", new(-88, 4.4f, 17), board, font);
            Vector3 mainSign = new(-76.5f, 4.4f, 17);
            course.Cube("Switchback sign", mainSign, new(8.3f, 1.6f, 0.24f), Quaternion.identity, board);
            AddLabel(course, "Switchback direction", "SWITCHBACK LEFT", mainSign + Vector3.forward * 0.14f,
                0.13f, font).color = new Color(0.5f, 1f, 1f);
        }

        /// <summary>Resolves the schedule from active race seconds, rounding before the cycle wraps.</summary>
        internal static CrossingPhase ScheduledPhase(double elapsed)
        {
            long milliseconds = (long)Math.Round(elapsed * 1000d, MidpointRounding.AwayFromZero) % 24000;
            return milliseconds < 12000 ? CrossingPhase.Open
                : milliseconds < 15000 ? CrossingPhase.Closing
                : CrossingPhase.Closed;
        }

        /// <summary>Changes both signs and the solid barrier, deferring a new closure while occupied.</summary>
        internal void Update(double elapsed, Bounds kartBounds)
        {
            CrossingPhase next = ScheduledPhase(elapsed);
            if (next == CrossingPhase.Closed && phase != CrossingPhase.Closed && barrierBounds.Intersects(kartBounds))
                next = CrossingPhase.Closing;
            if (next == phase)
                return;

            phase = next;
            barrier.SetActive(phase == CrossingPhase.Closed);
            string state = phase switch
            {
                CrossingPhase.Open => "OPEN",
                CrossingPhase.Closing => "CLOSING",
                _ => "CLOSED"
            };
            advance.text = entrance.text = state;
            advance.color = entrance.color = SignalColors[(int)phase];
        }

        /// <summary>Supports the signs with posts outside the driveable lanes.</summary>
        private static void BuildGantry(TrialCourse course, string name, float west, float east,
            float height, float z, Material support)
        {
            course.Cube(name + " beam", new((west + east) * 0.5f, height, z - 0.22f),
                new(east - west, 0.24f, 0.3f), Quaternion.identity, support);
            course.Cube(name + " west support", new(west, height * 0.5f, z - 0.22f),
                new(0.24f, height, 0.34f), Quaternion.identity, support);
            course.Cube(name + " east support", new(east, height * 0.5f, z - 0.22f),
                new(0.24f, height, 0.34f), Quaternion.identity, support);
        }

        /// <summary>Places the title and the live phase on the face of the board.</summary>
        private static TextMesh BuildSignal(TrialCourse course, string name, string title, Vector3 position,
            Material board, Font font)
        {
            course.Cube(name + " board", position, new(9.5f, 3.1f, 0.24f), Quaternion.identity, board);
            AddLabel(course, name + " title", title, position + new Vector3(0, 0.85f, 0.14f),
                0.145f, font).color = Color.white;
            return AddLabel(course, name + " state", "", position + new Vector3(0, -0.42f, 0.14f), 0.23f, font);
        }

        /// <summary>Creates centered text facing the southbound approach.</summary>
        private static TextMesh AddLabel(TrialCourse course, string name, string text, Vector3 position,
            float characterSize, Font font)
        {
            var label = new GameObject(name).AddComponent<TextMesh>();
            label.transform.SetParent(course.Root, false);
            label.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0, 180, 0));
            label.font = font;
            label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            label.fontSize = 64;
            label.characterSize = characterSize;
            label.fontStyle = FontStyle.Bold;
            label.anchor = TextAnchor.MiddleCenter;
            label.text = text;
            return label;
        }
    }
}
