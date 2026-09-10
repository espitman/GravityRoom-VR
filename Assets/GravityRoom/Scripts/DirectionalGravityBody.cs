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
        [SerializeField, Min(0f)] private float acceleration = 9.81f;

        public Rigidbody Body => body;
        public Grabbable Grabbable => grabbable;
        public GravityMode Mode => mode;
        public float Acceleration => acceleration;
        public Vector3 AccelerationVector => GravityRules.GetAcceleration(mode, acceleration);
        public bool IsSelected => grabbable != null && grabbable.SelectingPointsCount > 0;

        public void Configure(Rigidbody targetBody, Grabbable targetGrabbable,
            GravityMode initialMode, float gravityAcceleration)
        {
            body = targetBody;
            grabbable = targetGrabbable;
            mode = initialMode;
            acceleration = Mathf.Max(0f, gravityAcceleration);
            DisableBuiltInGravity();
        }

        public void SetMode(GravityMode newMode)
        {
            mode = newMode;
            if (body != null && !body.isKinematic)
                body.WakeUp();
        }

        private void Awake()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            DisableBuiltInGravity();
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
            if (GravityRules.ShouldApply(body, selected))
                body.AddForce(AccelerationVector, ForceMode.Acceleration);
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

        public static bool ShouldApply(Rigidbody body, bool selected) =>
            body != null && !selected && !body.isKinematic;
    }
}
