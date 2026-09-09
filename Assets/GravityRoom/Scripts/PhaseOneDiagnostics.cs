using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace GravityRoom
{
    /// <summary>Local, on-device smoke test. No network or account data is collected.</summary>
    public sealed class PhaseOneDiagnostics : MonoBehaviour
    {
        [SerializeField] private TextMesh statusText;
        private readonly List<XRDisplaySubsystem> displays = new();
        private readonly Dictionary<XRNode, int> pressedStates = new();
        private float nextRefresh;
        private float nextLog;

        public void Configure(TextMesh text) => statusText = text;

        private void Start()
        {
            Application.targetFrameRate = 90;
            Debug.Log("[GravityRoom] Phase 1 scene started.");
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.2f;
            SubsystemManager.GetSubsystems(displays);
            bool displayRunning = displays.Exists(display => display.running);
            string loader = XRGeneralSettings.Instance?.Manager?.activeLoader?.name ?? "not initialized";
            string status = $"XR: {(displayRunning ? "RUNNING" : "WAITING")}\n"
                + $"Head: {Tracking(XRNode.Head)}\n"
                + $"Left: {Controller(XRNode.LeftHand)}\n"
                + $"Right: {Controller(XRNode.RightHand)}";
            if (statusText != null) statusText.text = status;
            if (Time.unscaledTime >= nextLog)
            {
                nextLog = Time.unscaledTime + 5f;
                Debug.Log($"[GravityRoom] loader={loader}; {status.Replace('\n', ';')}");
                foreach (var hand in FindObjectsByType<OVRHand>(FindObjectsSortMode.None))
                    Debug.Log($"[GravityRoom] Hand {hand.name}: tracked={hand.IsTracked}; " +
                        $"dataValid={hand.IsDataValid}; confidence={hand.HandConfidence}; " +
                        $"controllerPoses={OVRManager.instance?.controllerDrivenHandPosesType}");
                foreach (var glove in FindObjectsByType<SciFiGloveVisual>(FindObjectsSortMode.None))
                {
                    int visibleParts = 0;
                    foreach (var part in glove.GetComponentsInChildren<MeshRenderer>())
                        if (part.enabled && part.gameObject.activeInHierarchy) visibleParts++;
                    Debug.Log($"[GravityRoom] Glove {glove.name}: visibleParts={visibleParts}; " +
                        $"shader={glove.BlackMaterial?.shader?.name}");
                }
            }
        }

        private static string Tracking(XRNode node)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            return device.isValid && device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked
                ? "TRACKED" : "NOT TRACKED";
        }

        private string Controller(XRNode node)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            device.TryGetFeatureValue(CommonUsages.trigger, out float trigger);
            device.TryGetFeatureValue(CommonUsages.grip, out float grip);
            int pressed = (trigger > 0.1f ? 1 : 0) | (grip > 0.1f ? 2 : 0);
            pressedStates.TryGetValue(node, out int previous);
            if (pressed != previous)
            {
                Debug.Log($"[GravityRoom] Input {node}: trigger={trigger:0.00}; grip={grip:0.00}");
                pressedStates[node] = pressed;
            }
            return $"{Tracking(node)}  trigger {trigger:0.0}  grip {grip:0.0}";
        }
    }
}
