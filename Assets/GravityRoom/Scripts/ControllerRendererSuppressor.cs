using System;
using UnityEngine;

namespace GravityRoom
{
    /// <summary>Hides controller model renderers while leaving controller data and interactors active.</summary>
    [DisallowMultipleComponent]
    public sealed class ControllerRendererSuppressor : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float rescanInterval = 0.5f;
        private float nextScan;

        public float RescanInterval => rescanInterval;

        public void Configure(float interval = 0.5f) => rescanInterval = Mathf.Max(0.1f, interval);

        private void Awake() => HideControllerRenderers();
        private void OnEnable() => HideControllerRenderers();

        private void LateUpdate()
        {
            if (Time.unscaledTime < nextScan)
                return;
            nextScan = Time.unscaledTime + rescanInterval;
            HideControllerRenderers();
        }

        public void HideControllerRenderers()
        {
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer != null && IsControllerModelRenderer(renderer.transform))
                    // ControllerVisual may toggle Renderer.enabled as tracking changes.
                    // forceRenderingOff suppresses drawing without touching the visual,
                    // controller data source, anchors, or any interactor GameObject.
                    renderer.forceRenderingOff = true;
            }
        }

        private bool IsControllerModelRenderer(Transform candidate)
        {
            for (Transform current = candidate; current != null; current = current.parent)
            {
                string name = current.name;
                if (name.IndexOf("OVRControllerPrefab", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("ControllerVisual", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("ControllerModel", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                foreach (Component component in current.GetComponents<Component>())
                {
                    if (component == null)
                        continue;
                    string typeName = component.GetType().FullName;
                    if (typeName == "OVRControllerHelper" ||
                        typeName == "Oculus.Interaction.Input.Visuals.ControllerVisual")
                        return true;
                }

                if (current == transform)
                    break;
            }
            return false;
        }
    }
}
