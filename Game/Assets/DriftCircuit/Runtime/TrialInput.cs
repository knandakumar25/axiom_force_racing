using System;
using UnityEngine.InputSystem;

namespace DriftCircuit
{
    /// <summary>Owns the desktop bindings for 1P, 2P, 3P, and 4P split-screen time trials.</summary>
    internal sealed class TrialInput : IDisposable
    {
        internal sealed class PlayerSlotActions
        {
            public readonly InputActionMap Map;
            public readonly InputAction Steer, Throttle, Brake, Drift, Item, Reset;

            public PlayerSlotActions(string name, string left, string right, string up, string down,
                string drift, string item, string reset)
            {
                Map = new InputActionMap(name);
                Steer = Map.AddAction($"{name} Steer", InputActionType.Value, expectedControlLayout: "Axis");
                Steer.AddCompositeBinding("1DAxis").With("Negative", left).With("Positive", right);
                Throttle = Bind(Map, $"{name} Throttle", up);
                Brake = Bind(Map, $"{name} Brake", down);
                Drift = Bind(Map, $"{name} Drift", drift);
                Item = Bind(Map, $"{name} Item", item);
                Reset = Bind(Map, $"{name} Reset", reset);
            }
        }

        private readonly InputActionMap sharedMap = new("Time Trial");
        private readonly PlayerSlotActions[] slots;

        public readonly InputAction Pause, Confirm, MenuUp, MenuDown;
        public InputAction Steer => slots[0].Steer;
        public InputAction Throttle => slots[0].Throttle;
        public InputAction Brake => slots[0].Brake;
        public InputAction Drift => slots[0].Drift;
        public InputAction Item => slots[0].Item;
        public InputAction Reset => slots[0].Reset;

        public InputAction P2Steer => slots[1].Steer;
        public InputAction P2Throttle => slots[1].Throttle;
        public InputAction P2Brake => slots[1].Brake;
        public InputAction P2Drift => slots[1].Drift;
        public InputAction P2Item => slots[1].Item;
        public InputAction P2Reset => slots[1].Reset;

        private int playerCount = 1;

        /// <summary>Creates keyboard and gamepad bindings for 1P-4P modes.</summary>
        public TrialInput()
        {
            Pause = Bind(sharedMap, "Pause", "<Keyboard>/escape", "<Gamepad>/start");
            Confirm = Bind(sharedMap, "Confirm menu", "<Keyboard>/enter", "<Gamepad>/buttonSouth");
            MenuUp = Bind(sharedMap, "Menu up", "<Keyboard>/upArrow", "<Keyboard>/w", "<Gamepad>/dpad/up", "<Gamepad>/leftStick/up");
            MenuDown = Bind(sharedMap, "Menu down", "<Keyboard>/downArrow", "<Keyboard>/s", "<Gamepad>/dpad/down", "<Gamepad>/leftStick/down");

            slots = new[]
            {
                new PlayerSlotActions("Player 1", "<Keyboard>/a", "<Keyboard>/d", "<Keyboard>/w", "<Keyboard>/s",
                    "<Keyboard>/space", "<Keyboard>/e", "<Keyboard>/r"),
                new PlayerSlotActions("Player 2", "<Keyboard>/leftArrow", "<Keyboard>/rightArrow", "<Keyboard>/upArrow", "<Keyboard>/downArrow",
                    "<Keyboard>/rightShift", "<Keyboard>/slash", "<Keyboard>/backspace"),
                new PlayerSlotActions("Player 3", "<Keyboard>/j", "<Keyboard>/l", "<Keyboard>/i", "<Keyboard>/k",
                    "<Keyboard>/u", "<Keyboard>/o", "<Keyboard>/p"),
                new PlayerSlotActions("Player 4", "<Keyboard>/numpad4", "<Keyboard>/numpad6", "<Keyboard>/numpad8", "<Keyboard>/numpad5",
                    "<Keyboard>/numpad0", "<Keyboard>/numpadEnter", "<Keyboard>/numpadPeriod")
            };

            RebuildP1Bindings(false);
        }

        public PlayerSlotActions Slot(int index) => slots[Math.Clamp(index, 0, slots.Length - 1)];

        /// <summary>Switches between combined 1P controls and 2P/3P/4P split controls.</summary>
        public void SetPlayerCount(int count)
        {
            count = Math.Clamp(count, 1, 4);
            if (playerCount == count)
                return;
            bool wasEnabled = sharedMap.enabled;
            Disable();
            playerCount = count;
            RebuildP1Bindings(count > 1);
            if (wasEnabled)
                Enable();
        }

        public void SetTwoPlayer(bool enabled) => SetPlayerCount(enabled ? 2 : 1);

        private void RebuildP1Bindings(bool split)
        {
            PlayerSlotActions p1 = slots[0];
            while (p1.Steer.bindings.Count > 0)
                p1.Steer.ChangeBinding(0).Erase();

            p1.Steer.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/a").With("Positive", "<Keyboard>/d");
            if (!split)
                p1.Steer.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/leftArrow").With("Positive", "<Keyboard>/rightArrow");
            p1.Steer.AddBinding("<Gamepad>/leftStick/x");

            ResetAction(p1.Throttle, split
                ? new[] { "<Keyboard>/w", "<Gamepad>/rightTrigger" }
                : new[] { "<Keyboard>/w", "<Keyboard>/upArrow", "<Gamepad>/rightTrigger" });
            ResetAction(p1.Brake, split
                ? new[] { "<Keyboard>/s", "<Gamepad>/leftTrigger" }
                : new[] { "<Keyboard>/s", "<Keyboard>/downArrow", "<Gamepad>/leftTrigger" });
            ResetAction(p1.Drift, new[] { "<Keyboard>/space", "<Gamepad>/rightShoulder" });
            ResetAction(p1.Item, new[] { "<Keyboard>/e", "<Gamepad>/buttonWest" });
            ResetAction(p1.Reset, new[] { "<Keyboard>/r", "<Gamepad>/buttonNorth" });
        }

        private static void ResetAction(InputAction action, string[] paths)
        {
            while (action.bindings.Count > 0)
                action.ChangeBinding(0).Erase();
            foreach (string path in paths)
                action.AddBinding(path);
        }

        /// <summary>Routes equivalent controls to the same named action.</summary>
        private static InputAction Bind(InputActionMap map, string name, params string[] paths)
        {
            var action = map.AddAction(name, InputActionType.Button);
            foreach (string path in paths)
                action.AddBinding(path);
            return action;
        }

        /// <summary>Keeps menu actions available throughout the trial.</summary>
        public void Enable()
        {
            sharedMap.Enable();
            for (int i = 0; i < playerCount; i++)
                slots[i].Map.Enable();
        }

        /// <summary>Stops listening when the scene controller becomes inactive.</summary>
        public void Disable()
        {
            sharedMap.Disable();
            for (int i = 0; i < slots.Length; i++)
                slots[i].Map.Disable();
        }

        /// <summary>Releases the generated action maps.</summary>
        public void Dispose()
        {
            sharedMap.Dispose();
            for (int i = 0; i < slots.Length; i++)
                slots[i].Map.Dispose();
        }
    }
}
