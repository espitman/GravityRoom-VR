using Oculus.Interaction;
using UnityEngine;

namespace GravityRoom
{
    public enum GravityMode
    {
        Down,
        Left,
        Right
    }

    /// <summary>Applies room-relative gravity to one opted-in body without changing Physics.gravity.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class DirectionalGravityBody : MonoBehaviour
    {
        [SerializeField] private Rigidbody body;
        [SerializeField] private Grabbable grabbable;
        [SerializeField] private GravityMode mode = GravityMode.Down;
        [SerializeField, Min(0f)] private float acceleration = 2.5f;

        private Grabbable subscribedGrabbable;
        private bool selectObserved;
        private bool customGravityArmed;

        public Rigidbody Body => body;
        public Grabbable Grabbable => grabbable;
        public GravityMode Mode => mode;
        public float Acceleration => acceleration;
        public Vector3 AccelerationVector => GravityRules.GetAcceleration(mode, acceleration);
        public bool IsSelected => grabbable != null && grabbable.SelectingPointsCount > 0;
        public bool IsGravityArmed => customGravityArmed;

        public void Configure(Rigidbody targetBody, Grabbable targetGrabbable,
            GravityMode initialMode, float gravityAcceleration)
        {
            body = targetBody;
            grabbable = targetGrabbable;
            mode = initialMode;
            acceleration = Mathf.Max(0f, gravityAcceleration);
            DisableBuiltInGravity();
            DisarmCustomGravity();
            BindGrabbable();
        }

        public void SetMode(GravityMode newMode)
        {
            mode = newMode;
            if (body != null && !body.isKinematic)
                body.WakeUp();
        }

        public void SetAcceleration(float gravityAcceleration)
        {
            acceleration = Mathf.Max(0f, gravityAcceleration);
        }

        private void Awake()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            DisableBuiltInGravity();
            DisarmCustomGravity();
        }

        private void OnEnable() => BindGrabbable();

        private void OnDisable()
        {
            UnbindGrabbable();
            DisarmCustomGravity();
        }

        private void FixedUpdate()
        {
            ApplyGravityStep(IsSelected);
        }

        /// <summary>
        /// Applies one force step. Selection always suppresses gravity; the first free physics step
        /// after release preserves the SDK throw velocity and adds the current mode's acceleration.
        /// </summary>
        public void ApplyGravityStep(bool selected)
        {
            if (GravityRules.ShouldApply(body, selected, customGravityArmed))
                body.AddForce(AccelerationVector, ForceMode.Acceleration);
        }

        /// <summary>Returns the orb to its safe pedestal state until another real grab/release.</summary>
        public void DisarmCustomGravity()
        {
            customGravityArmed = false;
            selectObserved = false;
        }

        /// <summary>
        /// Tracks the Grabbable lifecycle. Only a terminal Unselect paired with a prior Select arms
        /// gravity; Cancel and incomplete/multi-point releases leave it disarmed.
        /// </summary>
        public void ProcessSelectionEvent(PointerEventType eventType, int selectingPointsCount)
        {
            if (eventType == PointerEventType.Select)
            {
                selectObserved = true;
                customGravityArmed = false;
                return;
            }

            if (eventType == PointerEventType.Cancel)
            {
                customGravityArmed = false;
                if (selectingPointsCount == 0)
                    selectObserved = false;
                return;
            }

            if (eventType == PointerEventType.Unselect && selectingPointsCount == 0)
            {
                customGravityArmed = selectObserved;
                selectObserved = false;
            }
        }

        private void HandlePointerEvent(PointerEvent pointerEvent) =>
            ProcessSelectionEvent(pointerEvent.Type, grabbable != null ? grabbable.SelectingPointsCount : 0);

        private void BindGrabbable()
        {
            if (!isActiveAndEnabled || subscribedGrabbable == grabbable) return;
            UnbindGrabbable();
            subscribedGrabbable = grabbable;
            if (subscribedGrabbable != null)
                subscribedGrabbable.WhenPointerEventRaised += HandlePointerEvent;
        }

        private void UnbindGrabbable()
        {
            if (subscribedGrabbable != null)
                subscribedGrabbable.WhenPointerEventRaised -= HandlePointerEvent;
            subscribedGrabbable = null;
        }

        private void DisableBuiltInGravity()
        {
            if (body != null) body.useGravity = false;
        }
    }

    public static class GravityRules
    {
        public static Vector3 GetDirection(GravityMode mode)
        {
            return mode switch
            {
                GravityMode.Down => Vector3.down,
                GravityMode.Left => Vector3.left,
                GravityMode.Right => Vector3.right,
                _ => Vector3.down
            };
        }

        public static Vector3 GetAcceleration(GravityMode mode, float acceleration) =>
            GetDirection(mode) * Mathf.Max(0f, acceleration);

        public static bool ShouldApply(Rigidbody body, bool selected, bool armed) =>
            body != null && armed && !selected && !body.isKinematic;
    }
}
