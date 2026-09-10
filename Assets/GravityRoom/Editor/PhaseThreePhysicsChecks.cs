using System.Collections.Generic;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GravityRoom.Editor
{
    /// <summary>Exercises held/released gravity and room collisions in an isolated PhysX scene.</summary>
    internal static class PhaseThreePhysicsChecks
    {
        private const float Step = 0.01f;
        private const float GravityAcceleration = 2.5f;

        public static void Validate(Scene source, ICollection<string> failures)
        {
            PhaseTwoPracticeController practice = source.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<PhaseTwoPracticeController>()).Single();
            string sourcePath = source.path;
            float radius = practice.BallRadius;
            float mass = practice.Ball.mass;
            var boxes = source.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<BoxCollider>())
                .Where(box => box.enabled && !box.isTrigger && box.attachedRigidbody == null)
                .Select(box => (box.name, position: box.transform.TransformPoint(box.center),
                    rotation: box.transform.rotation, size: Vector3.Scale(box.size, box.transform.lossyScale)))
                .ToArray();

            SimulationMode originalMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            Scene testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                foreach (var box in boxes)
                {
                    var copy = new GameObject(box.name);
                    SceneManager.MoveGameObjectToScene(copy, testScene);
                    copy.transform.SetPositionAndRotation(box.position, box.rotation);
                    copy.transform.localScale = box.size;
                    copy.AddComponent<BoxCollider>();
                }

                var orb = new GameObject("Phase 3 physics test orb");
                SceneManager.MoveGameObjectToScene(orb, testScene);
                orb.AddComponent<SphereCollider>().radius = radius;
                Rigidbody body = orb.AddComponent<Rigidbody>();
                body.mass = mass;
                body.useGravity = false;
                body.linearDamping = 0f;
                body.angularDamping = 0f;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                DirectionalGravityBody gravity = orb.AddComponent<DirectionalGravityBody>();
                gravity.Configure(body, null, GravityMode.Down, GravityAcceleration);
                PhysicsScene physics = testScene.GetPhysicsScene();

                ValidateReleaseLifecycle(gravity, body, physics, failures);
                ValidateHeldAndReleasedModes(gravity, body, physics, failures);
                ValidateDirectionalRoomCollisions(gravity, body, physics, failures);
                Debug.Log("[GravityRoom] Phase 3 isolated physics checks completed: pedestal disarm, " +
                          "Select/Unselect/Cancel lifecycle, held suppression, first release step, " +
                          "and Down/Left/Right room collisions.");
            }
            finally
            {
                Physics.simulationMode = originalMode;
                EditorSceneManager.OpenScene(sourcePath, OpenSceneMode.Single);
            }
        }

        private static void ValidateReleaseLifecycle(DirectionalGravityBody gravity, Rigidbody body,
            PhysicsScene physics, ICollection<string> failures)
        {
            body.position = new Vector3(0f, 5f, 0f);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            gravity.DisarmCustomGravity();
            gravity.ApplyGravityStep(false);
            physics.Simulate(Step);
            if (gravity.IsGravityArmed || body.linearVelocity.sqrMagnitude > 0.000001f)
                failures.Add("A reset/disarmed orb received custom gravity while waiting on its pedestal.");

            gravity.ProcessSelectionEvent(Oculus.Interaction.PointerEventType.Unselect, 0);
            if (gravity.IsGravityArmed)
                failures.Add("Unselect without a preceding Select armed custom gravity.");

            gravity.ProcessSelectionEvent(Oculus.Interaction.PointerEventType.Select, 2);
            gravity.ProcessSelectionEvent(Oculus.Interaction.PointerEventType.Unselect, 1);
            if (gravity.IsGravityArmed)
                failures.Add("A non-terminal Unselect armed custom gravity while the orb was still held.");
            gravity.DisarmCustomGravity();

            gravity.ProcessSelectionEvent(Oculus.Interaction.PointerEventType.Select, 1);
            gravity.ProcessSelectionEvent(Oculus.Interaction.PointerEventType.Cancel, 0);
            if (gravity.IsGravityArmed)
                failures.Add("Cancel armed custom gravity.");

            gravity.ProcessSelectionEvent(Oculus.Interaction.PointerEventType.Unselect, 0);
            if (gravity.IsGravityArmed)
                failures.Add("Unselect after Cancel armed custom gravity without a new Select.");

            gravity.ProcessSelectionEvent(Oculus.Interaction.PointerEventType.Select, 1);
            gravity.ProcessSelectionEvent(Oculus.Interaction.PointerEventType.Unselect, 0);
            if (!gravity.IsGravityArmed)
                failures.Add("A genuine Select/Unselect release did not arm custom gravity.");
            if (!GravityRules.ShouldApply(body, false, true) ||
                GravityRules.ShouldApply(body, true, true))
                failures.Add("Armed gravity must apply only while the orb is not selected.");

            gravity.DisarmCustomGravity();
            if (gravity.IsGravityArmed)
                failures.Add("Reset did not disarm custom gravity.");
        }

        private static void ValidateHeldAndReleasedModes(DirectionalGravityBody gravity, Rigidbody body,
            PhysicsScene physics, ICollection<string> failures)
        {
            Vector3 throwVelocity = new(0.8f, 1.1f, 3f);
            foreach (GravityMode mode in new[] { GravityMode.Down, GravityMode.Left, GravityMode.Right })
            {
                body.position = new Vector3(0f, 5f, 0f);
                body.linearVelocity = throwVelocity;
                body.angularVelocity = Vector3.zero;
                gravity.SetMode(mode);
                gravity.ProcessSelectionEvent(Oculus.Interaction.PointerEventType.Select, 1);
                body.WakeUp();
                Physics.SyncTransforms();

                gravity.ApplyGravityStep(true);
                physics.Simulate(Step);
                if (Vector3.Distance(body.linearVelocity, throwVelocity) > 0.001f)
                    failures.Add($"{mode} gravity changed orb velocity while selected.");

                Vector3 beforeReleaseStep = body.linearVelocity;
                gravity.ProcessSelectionEvent(Oculus.Interaction.PointerEventType.Unselect, 0);
                gravity.ApplyGravityStep(false);
                physics.Simulate(Step);
                Vector3 expected = beforeReleaseStep +
                                   GravityRules.GetAcceleration(mode, GravityAcceleration) * Step;
                if (Vector3.Distance(body.linearVelocity, expected) > 0.002f)
                    failures.Add($"{mode} gravity did not preserve throw velocity and accelerate on the first free step.");
            }
        }

        private static void ValidateDirectionalRoomCollisions(DirectionalGravityBody gravity, Rigidbody body,
            PhysicsScene physics, ICollection<string> failures)
        {
            foreach (GravityMode mode in new[] { GravityMode.Down, GravityMode.Left, GravityMode.Right })
            {
                body.position = new Vector3(0f, 1.4f, 0f);
                body.rotation = Quaternion.identity;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                gravity.SetMode(mode);
                gravity.ProcessSelectionEvent(Oculus.Interaction.PointerEventType.Select, 1);
                gravity.ProcessSelectionEvent(Oculus.Interaction.PointerEventType.Unselect, 0);
                body.WakeUp();
                Physics.SyncTransforms();
                for (int i = 0; i < 400; i++)
                {
                    gravity.ApplyGravityStep(false);
                    physics.Simulate(Step);
                }

                bool settledAtSurface = mode switch
                {
                    GravityMode.Down => body.position.y >= 0.085f && body.position.y <= 0.13f,
                    GravityMode.Left => body.position.x >= -2.92f && body.position.x <= -2.82f,
                    GravityMode.Right => body.position.x >= 2.82f && body.position.x <= 2.92f,
                    _ => false
                };
                if (!settledAtSurface)
                    failures.Add($"{mode} gravity did not carry the orb to the matching fixed room surface.");
            }
        }
    }
}
