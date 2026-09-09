using System.Collections.Generic;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GravityRoom.Editor
{
    /// <summary>Exercises the generated room's collider geometry in an isolated PhysX scene.</summary>
    internal static class PhaseTwoPhysicsChecks
    {
        public static void Validate(Scene source, ICollection<string> failures)
        {
            var practice = source.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<PhaseTwoPracticeController>()).Single();
            string sourcePath = source.path;
            float radius = practice.BallRadius;
            float mass = practice.Ball.mass;
            var collisionMode = practice.Ball.collisionDetectionMode;
            Vector3 gatePosition = practice.GateCenter.position;
            Quaternion gateRotation = practice.GateCenter.rotation;
            Vector2 halfSize = practice.ApertureHalfSize;
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
                var orb = new GameObject("Physics test orb");
                SceneManager.MoveGameObjectToScene(orb, testScene);
                orb.AddComponent<SphereCollider>().radius = radius;
                var body = orb.AddComponent<Rigidbody>();
                body.mass = mass;
                body.collisionDetectionMode = collisionMode;
                PhysicsScene physics = testScene.GetPhysicsScene();

                body.position = new Vector3(1f, 1.5f, 0f);
                Physics.SyncTransforms();
                for (int i = 0; i < 200; i++) physics.Simulate(0.01f);
                if (body.position.y < 0.085f || body.position.y > 0.13f)
                    failures.Add("PhysX drop did not settle on the room floor.");

                // A fast straight shot must pass the opening, then stop at the back wall.
                body.useGravity = false;
                body.position = new Vector3(0f, 1.4f, 1.5f);
                body.linearVelocity = Vector3.forward * 15f;
                body.angularVelocity = Vector3.zero;
                body.WakeUp();
                Physics.SyncTransforms();
                bool passed = false;
                for (int i = 0; i < 60; i++)
                {
                    Vector3 previous = body.position;
                    physics.Simulate(0.01f);
                    passed |= PhaseTwoPassLogic.CrossedFrontToBack(previous, body.position,
                        gatePosition, gateRotation, halfSize, radius);
                }
                if (!passed || body.position.z > 2.87f)
                    failures.Add("PhysX fast shot did not pass the opening and remain inside the back wall.");

                // Aim into the side rail. Continuous collision must stop it before the scoring plane.
                body.position = new Vector3(0.575f, 1.4f, 1.5f);
                body.linearVelocity = Vector3.forward * 15f;
                body.angularVelocity = Vector3.zero;
                body.WakeUp();
                Physics.SyncTransforms();
                float furthest = body.position.z;
                for (int i = 0; i < 60; i++)
                {
                    physics.Simulate(0.01f);
                    furthest = Mathf.Max(furthest, body.position.z);
                }
                if (furthest > 2.45f)
                    failures.Add("PhysX fast shot tunneled through the gate rail.");
                Debug.Log("[GravityRoom] Phase 2 isolated physics checks completed: floor, fast aperture shot/back wall, rail impact.");
            }
            finally
            {
                Physics.simulationMode = originalMode;
                EditorSceneManager.OpenScene(sourcePath, OpenSceneMode.Single);
            }
        }
    }
}
