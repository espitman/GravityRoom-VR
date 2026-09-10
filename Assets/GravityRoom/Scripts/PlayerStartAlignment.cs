using System.Collections;
using UnityEngine;

namespace GravityRoom
{
    /// <summary>Places the stationary rig at the room origin and faces it toward the target wall.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerStartAlignment : MonoBehaviour
    {
        [SerializeField] private OVRCameraRig cameraRig;
        private Coroutine pendingAlignment;

        public OVRCameraRig CameraRig => cameraRig;

        public void Configure(OVRCameraRig rig) => cameraRig = rig;

        private void OnEnable()
        {
            if (OVRManager.display != null)
                OVRManager.display.RecenteredPose += HandleRecentered;
            ScheduleAlignment();
        }

        private void OnDisable()
        {
            if (OVRManager.display != null)
                OVRManager.display.RecenteredPose -= HandleRecentered;
            if (pendingAlignment != null)
                StopCoroutine(pendingAlignment);
        }

        private void HandleRecentered() => ScheduleAlignment();

        private void ScheduleAlignment()
        {
            if (!isActiveAndEnabled) return;
            if (pendingAlignment != null) StopCoroutine(pendingAlignment);
            pendingAlignment = StartCoroutine(AlignWhenTrackingIsReady());
        }

        private IEnumerator AlignWhenTrackingIsReady()
        {
            yield return null;
            yield return new WaitForSecondsRealtime(0.35f);
            if (cameraRig == null || cameraRig.centerEyeAnchor == null) yield break;

            Transform rigTransform = cameraRig.transform;
            Transform head = cameraRig.centerEyeAnchor;
            Vector3 localHead = rigTransform.InverseTransformPoint(head.position);
            Vector3 localForward = rigTransform.InverseTransformDirection(head.forward);
            localForward.y = 0f;
            if (localForward.sqrMagnitude < 0.001f) yield break;

            float yaw = Mathf.Atan2(localForward.x, localForward.z) * Mathf.Rad2Deg;
            Quaternion alignedRotation = Quaternion.Euler(0f, -yaw, 0f);
            rigTransform.SetPositionAndRotation(
                -(alignedRotation * new Vector3(localHead.x, 0f, localHead.z)),
                alignedRotation);

            foreach (SciFiGloveVisual glove in FindObjectsByType<SciFiGloveVisual>(FindObjectsSortMode.None))
                glove.ResetBodyPose();
            Debug.Log($"[GravityRoom] Player aligned to target wall; corrected yaw={yaw:F1} degrees.");
            pendingAlignment = null;
        }
    }
}
