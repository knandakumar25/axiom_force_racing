using System;
using UnityEngine.InputSystem;

namespace DriftCircuit
{
    /// <summary>Owns the desktop bindings for a single time trial.</summary>
    internal sealed class TrialInput : IDisposable
    {
        private readonly InputActionMap map = new("Time Trial");
        public readonly InputAction Steer, Throttle, Brake, Drift, Reset, Pause, Confirm, MenuUp, MenuDown;

        /// <summary>Creates both digital keyboard and analog gamepad bindings.</summary>
        public TrialInput()
        {
            Steer = map.AddAction("Steer", InputActionType.Value, expectedControlLayout: "Axis");
            Steer.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/a").With("Positive", "<Keyboard>/d");
            Steer.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/leftArrow").With("Positive", "<Keyboard>/rightArrow");
            Steer.AddBinding("<Gamepad>/leftStick/x");
            Throttle = Bind("Throttle", "<Keyboard>/w", "<Keyboard>/upArrow", "<Gamepad>/rightTrigger");
            Brake = Bind("Brake", "<Keyboard>/s", "<Keyboard>/downArrow", "<Gamepad>/leftTrigger");
            Drift = Bind("Drift", "<Keyboard>/space", "<Gamepad>/rightShoulder");
            Reset = Bind("Reset", "<Keyboard>/r", "<Gamepad>/buttonNorth");
            Pause = Bind("Pause", "<Keyboard>/escape", "<Gamepad>/start");
            Confirm = Bind("Confirm menu", "<Keyboard>/enter", "<Gamepad>/buttonSouth");
            MenuUp = Bind("Menu up", "<Keyboard>/upArrow", "<Keyboard>/w", "<Gamepad>/dpad/up", "<Gamepad>/leftStick/up");
            MenuDown = Bind("Menu down", "<Keyboard>/downArrow", "<Keyboard>/s", "<Gamepad>/dpad/down", "<Gamepad>/leftStick/down");
        }

        /// <summary>Routes equivalent controls to the same named action.</summary>
        private InputAction Bind(string name, params string[] paths)
        {
            var action = map.AddAction(name, InputActionType.Button);
            foreach (string path in paths)
                action.AddBinding(path);
            return action;
        }

        /// <summary>Keeps menu actions available throughout the trial.</summary>
        public void Enable() => map.Enable();

        /// <summary>Stops listening when the scene controller becomes inactive.</summary>
        public void Disable() => map.Disable();

        /// <summary>Releases the generated action map.</summary>
        public void Dispose() => map.Dispose();
    }
}
