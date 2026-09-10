using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;
using InputHand = Oculus.Interaction.Input.Hand;

namespace GravityRoom
{
    /// <summary>Owns the single reusable Phase 2 practice ball and gate pass lifecycle.</summary>
    public sealed class PhaseTwoPracticeController : MonoBehaviour
    {
        [SerializeField] private Rigidbody ball;
        [SerializeField] private Grabbable grabbable;
        [SerializeField] private Transform gateCenter;
        [SerializeField] private TextMesh statusText;
        [SerializeField] private Material readyMaterial;
        [SerializeField] private Material successMaterial;
        [SerializeField] private float ballRadius = 0.1f;
        [SerializeField] private Vector2 apertureHalfSize = new(0.5f, 0.6f);
        [SerializeField] private float resetDelay = 0.9f;
        [SerializeField] private float floorResetDelay = 2f;
        [SerializeField] private float unheldTimeout = 12f;
        [Header("Natural-hand reset")]
        [SerializeField] private float handResetHoldDuration = 1f;
        [SerializeField] private float minimumHandResetDistance = 0.9f;
        [SerializeField, Range(-1f, 1f)] private float palmUpDotThreshold = 0.65f;

        private Vector3 spawnPosition;
        private Quaternion spawnRotation;
        private Vector3 previousPosition;
        private float unheldTime;
        private float settledUnreachableTime;
        private float pendingResetTime = -1f;
        private bool wasHeld;
        private bool hasBeenHeld;
        private bool passArmed;
        private bool successPending;
        private bool releaseObserved;
        private Vector3 releasePosition;
        private OVRHand leftOvrHand;
        private OVRHand rightOvrHand;
        private InputHand leftInputHand;
        private InputHand rightInputHand;
        private float leftResetGestureTime;
        private float rightResetGestureTime;

        public Rigidbody Ball => ball;
        public Grabbable Grabbable => grabbable;
        public Transform GateCenter => gateCenter;
        public TextMesh StatusText => statusText;
        public float BallRadius => ballRadius;
        public Vector2 ApertureHalfSize => apertureHalfSize;

        public void Configure(Rigidbody practiceBall, Grabbable ballGrabbable, Transform gate,
            TextMesh status, Material ready, Material success, float radius, Vector2 halfSize)
        {
            ball = practiceBall;
            grabbable = ballGrabbable;
            gateCenter = gate;
            statusText = status;
            readyMaterial = ready;
            successMaterial = success;
            ballRadius = radius;
            apertureHalfSize = halfSize;
        }

        private void Awake()
        {
            spawnPosition = ball.position;
            spawnRotation = ball.rotation;
            previousPosition = ball.position;
            DiscoverHands();
            SetStatus("GRAB THE ORB, THEN THROW IT THROUGH THE GATE\nRESET: B / Y OR PALM-UP PINCH", Color.black);
        }

        private void OnValidate()
        {
            handResetHoldDuration = Mathf.Max(0.1f, handResetHoldDuration);
            minimumHandResetDistance = Mathf.Max(ballRadius * 2f, minimumHandResetDistance);
            palmUpDotThreshold = Mathf.Clamp(palmUpDotThreshold, -1f, 1f);
        }

        private void OnEnable()
        {
            if (grabbable != null) grabbable.WhenPointerEventRaised += HandlePointerEvent;
        }

        private void OnDisable()
        {
            if (grabbable != null) grabbable.WhenPointerEventRaised -= HandlePointerEvent;
            ResetHandGestureProgress();
        }

        private void HandlePointerEvent(PointerEvent pointerEvent)
        {
            if (pointerEvent.Type == PointerEventType.Select)
            {
                // Selection can begin and end between two physics steps.
                hasBeenHeld = true;
                wasHeld = true;
                passArmed = false;
                releaseObserved = false;
                pendingResetTime = -1f;
                successPending = false;
                SetBallMaterial(readyMaterial);
                SetStatus("HELD — AIM THROUGH THE GATE", Color.black);
                Debug.Log("[GravityRoom] Phase 2 orb grabbed.");
            }
            else if ((pointerEvent.Type == PointerEventType.Unselect || pointerEvent.Type == PointerEventType.Cancel)
                     && grabbable.SelectingPointsCount == 0)
            {
                releasePosition = ball.transform.position;
                releaseObserved = pointerEvent.Type == PointerEventType.Unselect;
            }
        }

        private void Update()
        {
            if (OVRInput.GetDown(OVRInput.RawButton.B) || OVRInput.GetDown(OVRInput.RawButton.Y))
                RequestReset(0f, false);

            UpdateHandResetGesture(Time.unscaledDeltaTime);
        }

        private void DiscoverHands()
        {
            OVRHand[] ovrHands = FindObjectsByType<OVRHand>(FindObjectsSortMode.None);
            for (int i = 0; i < ovrHands.Length; i++)
            {
                OVRHand hand = ovrHands[i];
                OVRSkeleton.SkeletonType type =
                    ((OVRSkeleton.IOVRSkeletonDataProvider)hand).GetSkeletonType();
                if (type == OVRSkeleton.SkeletonType.HandLeft ||
                    type == OVRSkeleton.SkeletonType.XRHandLeft)
                    leftOvrHand = hand;
                else if (type == OVRSkeleton.SkeletonType.HandRight ||
                         type == OVRSkeleton.SkeletonType.XRHandRight)
                    rightOvrHand = hand;
            }

            InputHand[] inputHands = FindObjectsByType<InputHand>(FindObjectsSortMode.None);
            for (int i = 0; i < inputHands.Length; i++)
            {
                InputHand hand = inputHands[i];
                if (hand.Handedness == Handedness.Left) leftInputHand = hand;
                else if (hand.Handedness == Handedness.Right) rightInputHand = hand;
            }
        }

        private void UpdateHandResetGesture(float deltaTime)
        {
            if (ball == null || grabbable == null || grabbable.SelectingPointsCount > 0)
            {
                ResetHandGestureProgress();
                return;
            }

            HandResetSample left = SampleHand(leftOvrHand, leftInputHand, true);
            HandResetSample right = SampleHand(rightOvrHand, rightInputHand, false);
            bool orbIsDistant = PhaseTwoResetGestureLogic.IsOrbMeaningfullyDistant(
                ball.position, left.Position, left.IsPoseValid, right.Position, right.IsPoseValid,
                minimumHandResetDistance);

            bool leftConditions = orbIsDistant && left.IsGestureValid(palmUpDotThreshold);
            bool rightConditions = orbIsDistant && right.IsGestureValid(palmUpDotThreshold);
            float previousLeftTime = leftResetGestureTime;
            float previousRightTime = rightResetGestureTime;
            leftResetGestureTime = PhaseTwoResetGestureLogic.UpdateHoldDuration(
                leftResetGestureTime, leftConditions, deltaTime, handResetHoldDuration);
            rightResetGestureTime = PhaseTwoResetGestureLogic.UpdateHoldDuration(
                rightResetGestureTime, rightConditions, deltaTime, handResetHoldDuration);

            if (PhaseTwoResetGestureLogic.CompletedThisFrame(
                    previousLeftTime, leftResetGestureTime, handResetHoldDuration) ||
                PhaseTwoResetGestureLogic.CompletedThisFrame(
                    previousRightTime, rightResetGestureTime, handResetHoldDuration))
                RequestReset(0f, false);
        }

        private static HandResetSample SampleHand(OVRHand ovrHand, InputHand inputHand, bool isLeft)
        {
            if (ovrHand == null || inputHand == null || !ovrHand.isActiveAndEnabled ||
                !ovrHand.IsTracked || !ovrHand.IsDataValid ||
                ovrHand.HandConfidence != OVRHand.TrackingConfidence.High ||
                !inputHand.GetJointPose(HandJointId.HandWristRoot, out Pose wristPose))
                return default;

            Vector3 localPalmar = isLeft ? Constants.LeftPalmar : Constants.RightPalmar;
            return new HandResetSample
            {
                IsPoseValid = true,
                Position = wristPose.position,
                PalmUpDot = Vector3.Dot(wristPose.rotation * localPalmar, Vector3.up),
                IsIndexPinching = ovrHand.GetFingerIsPinching(OVRHand.HandFinger.Index) &&
                    ovrHand.GetFingerConfidence(OVRHand.HandFinger.Index) ==
                    OVRHand.TrackingConfidence.High,
                IsSystemGesture = ovrHand.IsSystemGestureInProgress
            };
        }

        private void ResetHandGestureProgress()
        {
            leftResetGestureTime = 0f;
            rightResetGestureTime = 0f;
        }

        private void FixedUpdate()
        {
            bool held = grabbable.SelectingPointsCount > 0;
            Vector3 currentPosition = ball.position;

            if (held)
            {
                if (!wasHeld)
                {
                    hasBeenHeld = true;
                    passArmed = false;
                    SetStatus("HELD — AIM THROUGH THE GATE", Color.black);
                }
                wasHeld = true;
                previousPosition = currentPosition;
                return;
            }

            if (wasHeld)
            {
                wasHeld = false;
                unheldTime = 0f;
                settledUnreachableTime = 0f;
                // Start exactly where the SDK emitted Unselect. This retains a one-step fast
                // throw but cannot turn a carried-through-then-released ball into a success.
                previousPosition = releasePosition;
                passArmed = releaseObserved && IsFullyInFront(previousPosition);
                releaseObserved = false;
                Debug.Log($"[GravityRoom] Phase 2 orb released; speed={ball.linearVelocity.magnitude:F2} m/s.");
                SetStatus("RELEASED — FOLLOW THE ORB", Color.black);
            }

            if (pendingResetTime >= 0f)
            {
                pendingResetTime -= Time.fixedDeltaTime;
                if (pendingResetTime <= 0f)
                {
                    ResetBall();
                    return;
                }
            }

            if (!successPending && PhaseTwoPassLogic.HasReachedFloor(currentPosition, ballRadius))
            {
                RequestReset(floorResetDelay, false);
                SetStatus("ORB DOWN — RESETTING...", Color.black);
            }

            if (!successPending && passArmed && PhaseTwoPassLogic.CrossedFrontToBack(
                    previousPosition, currentPosition, gateCenter.position, gateCenter.rotation,
                    apertureHalfSize, ballRadius))
            {
                successPending = true;
                passArmed = false;
                SetBallMaterial(successMaterial);
                SetStatus("SUCCESS! CLEAN PASS", new Color(0.05f, 0.55f, 0.16f));
                Debug.Log("[GravityRoom] Phase 2 successful gate pass.");
                RequestReset(resetDelay, true);
            }

            if (!successPending && hasBeenHeld)
            {
                unheldTime += Time.fixedDeltaTime;
                settledUnreachableTime = PhaseTwoPassLogic.IsSettledAndUnreachable(
                        currentPosition, ball.linearVelocity, ball.angularVelocity, ball.IsSleeping())
                    ? settledUnreachableTime + Time.fixedDeltaTime
                    : 0f;

                if (PhaseTwoPassLogic.ShouldAutoReset(
                        currentPosition, unheldTime, settledUnreachableTime, unheldTimeout))
                    RequestReset(0f, false);
            }

            previousPosition = currentPosition;
        }

        private bool IsFullyInFront(Vector3 position) =>
            Vector3.Dot(position - gateCenter.position, gateCenter.forward) <= -ballRadius;

        private void RequestReset(float delay, bool afterSuccess)
        {
            successPending |= afterSuccess;
            if (pendingResetTime < 0f || delay < pendingResetTime)
                pendingResetTime = Mathf.Max(0f, delay);
        }

        private void ResetBall()
        {
            // Recheck selection at the mutation boundary.
            if (grabbable.SelectingPointsCount > 0) return;
            Debug.Log("[GravityRoom] Phase 2 orb reset to pedestal.");
            if (ball.TryGetComponent(out DirectionalGravityBody directionalGravity))
                directionalGravity.DisarmCustomGravity();
            ball.position = spawnPosition;
            ball.rotation = spawnRotation;
            ball.linearVelocity = Vector3.zero;
            ball.angularVelocity = Vector3.zero;
            ball.isKinematic = false;
            ball.WakeUp();
            previousPosition = spawnPosition;
            unheldTime = 0f;
            settledUnreachableTime = 0f;
            pendingResetTime = -1f;
            passArmed = false;
            successPending = false;
            hasBeenHeld = false;
            releaseObserved = false;
            ResetHandGestureProgress();
            SetBallMaterial(readyMaterial);
            SetStatus("READY — GRAB AND THROW THROUGH THE GATE\nRESET: B / Y OR PALM-UP PINCH", Color.black);
        }

        private struct HandResetSample
        {
            public bool IsPoseValid;
            public Vector3 Position;
            public float PalmUpDot;
            public bool IsIndexPinching;
            public bool IsSystemGesture;

            public bool IsGestureValid(float requiredPalmUpDot) =>
                PhaseTwoResetGestureLogic.AreHandConditionsMet(IsPoseValid, IsSystemGesture,
                    IsIndexPinching, PalmUpDot, requiredPalmUpDot);
        }

        public void ResetAfterFloorContact()
        {
            if (grabbable != null && grabbable.SelectingPointsCount == 0)
            {
                RequestReset(floorResetDelay, false);
                SetStatus("ORB DOWN — RESETTING...", Color.black);
            }
        }

        private void SetBallMaterial(Material material)
        {
            if (material != null && ball.TryGetComponent(out MeshRenderer renderer))
                renderer.sharedMaterial = material;
        }

        private void SetStatus(string message, Color color)
        {
            if (statusText == null) return;
            statusText.text = message;
            statusText.color = color;
        }
    }

    /// <summary>Pure safety and timing rules for the intentional natural-hand reset gesture.</summary>
    public static class PhaseTwoResetGestureLogic
    {
        public static bool AreHandConditionsMet(bool trackingAndConfidenceValid,
            bool systemGestureInProgress, bool indexIsPinching, float palmUpDot,
            float requiredPalmUpDot) =>
            trackingAndConfidenceValid && !systemGestureInProgress && indexIsPinching &&
            palmUpDot >= requiredPalmUpDot;

        public static bool IsOrbMeaningfullyDistant(Vector3 orbPosition,
            Vector3 leftHandPosition, bool leftHandValid,
            Vector3 rightHandPosition, bool rightHandValid, float minimumDistance)
        {
            if (!leftHandValid && !rightHandValid) return false;
            float minimumDistanceSquared = minimumDistance * minimumDistance;
            return (!leftHandValid ||
                    (orbPosition - leftHandPosition).sqrMagnitude >= minimumDistanceSquared) &&
                   (!rightHandValid ||
                    (orbPosition - rightHandPosition).sqrMagnitude >= minimumDistanceSquared);
        }

        public static float UpdateHoldDuration(float currentDuration, bool conditionsMet,
            float deltaTime, float requiredDuration)
        {
            if (!conditionsMet) return 0f;
            return Mathf.Min(requiredDuration,
                currentDuration + Mathf.Max(0f, deltaTime));
        }

        public static bool CompletedThisFrame(float previousDuration, float currentDuration,
            float requiredDuration) =>
            previousDuration < requiredDuration && currentDuration >= requiredDuration;
    }

    /// <summary>Geometry-only gate rule, kept independent of frame rate and physics callbacks.</summary>
    public static class PhaseTwoPassLogic
    {
        public static bool HasReachedFloor(Vector3 sphereCenter, float sphereRadius) =>
            sphereCenter.y <= sphereRadius + 0.015f;

        public static bool CrossedFrontToBack(Vector3 previous, Vector3 current, Vector3 gatePosition,
            Quaternion gateRotation, Vector2 apertureHalfSize, float sphereRadius)
        {
            Vector3 normal = gateRotation * Vector3.forward;
            float previousDistance = Vector3.Dot(previous - gatePosition, normal);
            float currentDistance = Vector3.Dot(current - gatePosition, normal);
            if (previousDistance >= 0f || currentDistance < 0f || currentDistance <= previousDistance)
                return false;

            float denominator = previousDistance - currentDistance;
            if (Mathf.Abs(denominator) < 0.00001f)
                return false;
            float t = previousDistance / denominator;
            Vector3 crossing = Vector3.LerpUnclamped(previous, current, t);
            Vector3 local = Quaternion.Inverse(gateRotation) * (crossing - gatePosition);
            float allowedX = apertureHalfSize.x - sphereRadius;
            float allowedY = apertureHalfSize.y - sphereRadius;
            return allowedX >= 0f && allowedY >= 0f &&
                   Mathf.Abs(local.x) <= allowedX && Mathf.Abs(local.y) <= allowedY;
        }

        public static bool IsSettledAndUnreachable(Vector3 position, Vector3 linearVelocity,
            Vector3 angularVelocity, bool sleeping)
        {
            bool unreachable = position.y < 0.24f || Mathf.Abs(position.x) > 2.35f ||
                               position.z > 2.9f || position.z < -2.6f;
            bool settled = sleeping ||
                           (linearVelocity.sqrMagnitude < 0.01f && angularVelocity.sqrMagnitude < 0.04f);
            return unreachable && settled;
        }

        public static bool ShouldAutoReset(Vector3 position, float unheldTime,
            float settledUnreachableTime, float unheldTimeout)
        {
            bool escaped = Mathf.Abs(position.x) > 3.4f || position.y < -0.45f ||
                           position.y > 4f || position.z < -3.4f || position.z > 3.4f;
            return escaped || settledUnreachableTime >= 1.5f || unheldTime >= unheldTimeout;
        }
    }
}
