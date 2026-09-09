using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

namespace GravityRoom
{
    /// <summary>
    /// Applies a project-owned glove treatment to a Meta HandVisual without replacing its
    /// skinned mesh, skeleton, data source, or interaction components.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SciFiGloveVisual : MonoBehaviour
    {
        private const string GeneratedRootName = "GravityRoom Glove Geometry";
        private const float PlateLift = 0.014f;

        [SerializeField] private HandVisual handVisual;
        [SerializeField] private Material blackMaterial;
        [SerializeField] private Material whiteMaterial;

        private readonly List<SkinnedMeshRenderer> handRenderers = new();
        private readonly List<Renderer> generatedRenderers = new();
        private Transform generatedRoot;
        private Transform dorsalPlate;
        private Transform cuff;
        private Transform forearmSleeve;
        private Transform elbowJoint;
        private bool initialized;
        private readonly HashSet<SkinnedMeshRenderer> coloredHands = new();
        private Material fingerSurfaceMaterial;
        private readonly List<Mesh> ownedMeshes = new();
        private Transform head;
        private Vector3 bodyForward = Vector3.forward;
        private Vector3 lastElbowBend;
        private bool bodyPoseInitialized;
        private int bodyPoseFrame = -1;
        private static readonly HandJointId[] FingerStarts = { HandJointId.HandIndex1, HandJointId.HandMiddle1, HandJointId.HandRing1, HandJointId.HandPinky1 };
        private static readonly HandJointId[] FingerEnds = { HandJointId.HandIndex2, HandJointId.HandMiddle2, HandJointId.HandRing2, HandJointId.HandPinky2 };
        private static readonly float[] FingerWidths = { 0.0135f, 0.0145f, 0.0135f, 0.0115f };

        private void OnDestroy()
        {
            if (fingerSurfaceMaterial != null) Destroy(fingerSurfaceMaterial);
            foreach (Mesh mesh in ownedMeshes)
                if (mesh != null) Destroy(mesh);
        }

        public HandVisual HandVisual => handVisual;
        public Material BlackMaterial => blackMaterial;
        public Material WhiteMaterial => whiteMaterial;

        public void Configure(HandVisual visual, Material black, Material white)
        {
            handVisual = visual;
            blackMaterial = black;
            whiteMaterial = white;

            if (Application.isPlaying)
            {
                initialized = false;
                Initialize();
            }
        }

        private void Awake() => Initialize();

        private void OnEnable()
        {
            Initialize();
            if (handVisual != null)
                handVisual.WhenHandVisualUpdated += RefreshVisual;
        }

        private void OnDisable()
        {
            if (handVisual != null)
                handVisual.WhenHandVisualUpdated -= RefreshVisual;
        }

        private void LateUpdate()
        {
            // HandVisual and other SDK effects can update renderer property blocks late.
            // This shader deliberately ignores their alpha properties; clearing here also
            // prevents stale translucent values when switching from the stock material.
            foreach (SkinnedMeshRenderer renderer in handRenderers)
                if (renderer != null)
                    renderer.SetPropertyBlock(null);

            RefreshVisual();
        }

        private void Initialize()
        {
            if (initialized)
                return;

            if (handVisual == null)
                handVisual = GetComponent<HandVisual>();
            if (handVisual == null || blackMaterial == null || whiteMaterial == null)
                return;

            handRenderers.Clear();
            handRenderers.AddRange(handVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true));
            foreach (SkinnedMeshRenderer renderer in handRenderers)
            {
                renderer.sharedMaterial = blackMaterial;
                renderer.SetPropertyBlock(null);
            }

            // The stock editor is optional. Disconnect and disable it so its per-frame
            // translucent color/alpha block cannot override the opaque glove material.
            handVisual.InjectOptionalMaterialPropertyBlockEditor(null);
            foreach (MaterialPropertyBlockEditor editor in
                     handVisual.GetComponentsInChildren<MaterialPropertyBlockEditor>(true))
            {
                editor.enabled = false;
            }

            Transform oldRoot = transform.Find(GeneratedRootName);
            if (oldRoot != null)
                Destroy(oldRoot.gameObject);

            foreach (Mesh mesh in ownedMeshes)
                if (mesh != null)
                    Destroy(mesh);
            ownedMeshes.Clear();

            generatedRoot = new GameObject(GeneratedRootName).transform;
            generatedRoot.SetParent(transform, false);
            generatedRoot.localPosition = Vector3.zero;
            generatedRoot.localRotation = Quaternion.identity;
            generatedRoot.localScale = Vector3.one;

            generatedRenderers.Clear();
            Mesh plateMesh = BuildChamferedPlateMesh();
            Mesh cuffMesh = BuildTubeMesh("GravityRoom Cuff", 1f, 1f);
            Mesh sleeveMesh = BuildTubeMesh("GravityRoom Sleeve", 0.82f, 1.15f);

            ownedMeshes.AddRange(new[] { plateMesh, cuffMesh, sleeveMesh });

            dorsalPlate = CreatePart("White Dorsal Plate", plateMesh, whiteMaterial);
            cuff = CreatePart("Black Wrist Cuff", cuffMesh, blackMaterial);
            forearmSleeve = CreatePart("White Forearm Sleeve", sleeveMesh, whiteMaterial);
            elbowJoint = CreatePart("Black Elbow Joint", cuffMesh, blackMaterial);

            OVRCameraRig cameraRig = GetComponentInParent<OVRCameraRig>();
            head = cameraRig != null && cameraRig.centerEyeAnchor != null
                ? cameraRig.centerEyeAnchor
                : Camera.main != null ? Camera.main.transform : null;
            bodyPoseInitialized = false;
            bodyPoseFrame = -1;
            lastElbowBend = Vector3.zero;

            initialized = true;
            SetGeneratedRenderers(false);
            RefreshVisual();
        }

        private Transform CreatePart(string partName, Mesh mesh, Material material)
        {
            var part = new GameObject(partName);
            part.transform.SetParent(generatedRoot, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
            generatedRenderers.Add(renderer);
            return part.transform;
        }

        private void RefreshVisual()
        {
            if (!initialized)
                return;
            if (handVisual == null || handVisual.Hand == null)
            {
                SetGeneratedRenderers(false);
                return;
            }

            IHand hand = handVisual.Hand;
            bool visible = hand.IsTrackedDataValid && handVisual.IsVisible;
            SetGeneratedRenderers(visible);
            if (!visible)
                return;

            if (!TryGetPose(hand, HandJointId.HandWristRoot, out Pose wrist) ||
                !TryGetPose(hand, HandJointId.HandMiddle1, out Pose middle) ||
                !TryGetPose(hand, HandJointId.HandIndex1, out Pose index) ||
                !TryGetPose(hand, HandJointId.HandPinky1, out Pose pinky))
            {
                SetGeneratedRenderers(false);
                return;
            }

            Vector3 forward = SafeDirection(middle.position - wrist.position, wrist.rotation * Vector3.forward);
            Vector3 side = SafeDirection(index.position - pinky.position, wrist.rotation * Vector3.right);
            Vector3 dorsal = Vector3.Cross(side, forward).normalized;
            if (hand.Handedness == Handedness.Left)
                dorsal = -dorsal;
            if (dorsal.sqrMagnitude < 0.5f)
                dorsal = wrist.rotation * Vector3.up;

            float handLength = Mathf.Clamp(Vector3.Distance(wrist.position, middle.position), 0.055f, 0.11f);
            float handWidth = Mathf.Clamp(Vector3.Distance(index.position, pinky.position) * 1.20f, 0.055f, 0.095f);
            Quaternion handRotation = Quaternion.LookRotation(forward, dorsal);

            SetPart(dorsalPlate,
                wrist.position + forward * (handLength * 0.48f) + dorsal * PlateLift,
                handRotation,
                new Vector3(handWidth, 0.0065f, handLength * 0.72f));

            ApplyFingerSurfaceColors(hand, forward, dorsal);

            float scale = Mathf.Clamp(hand.Scale, 0.75f, 1.35f);
            Vector3 fallbackForearmDirection = -forward;
            Quaternion fallbackForearmRotation = Quaternion.LookRotation(fallbackForearmDirection, dorsal);
            SetPart(cuff, wrist.position + fallbackForearmDirection * (0.018f * scale), fallbackForearmRotation,
                new Vector3(0.043f * scale, 0.036f * scale, 0.038f * scale));

            UpdateArm(hand, wrist.position, dorsal, scale);
        }

        private void ApplyFingerSurfaceColors(IHand hand, Vector3 palmForward, Vector3 palmDorsal)
        {
            // Bake only once per active hand mesh to locate its dorsal finger surfaces.
            // Colors are stored on the original skinned vertices, so they deform with
            // the skin instead of intersecting it like separate rigid finger plates.
            foreach (var renderer in handRenderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    coloredHands.Contains(renderer) || renderer.sharedMesh == null)
                    continue;
                var starts = new Vector3[4];
                var directions = new Vector3[4];
                var dorsals = new Vector3[4];
                var lengths = new float[4];
                bool posesValid = true;
                for (int finger = 0; finger < 4; finger++)
                {
                    if (!TryGetPose(hand, FingerStarts[finger], out Pose start) ||
                        !TryGetPose(hand, FingerEnds[finger], out Pose end))
                    { posesValid = false; break; }
                    starts[finger] = start.position;
                    Vector3 delta = end.position - start.position;
                    lengths[finger] = delta.magnitude;
                    if (lengths[finger] < 0.001f) { posesValid = false; break; }
                    directions[finger] = delta / lengths[finger];
                    dorsals[finger] = Quaternion.FromToRotation(palmForward, directions[finger]) * palmDorsal;
                }
                if (!posesValid) continue;
                var baked = new Mesh();
                renderer.BakeMesh(baked);
                var vertices = baked.vertices;
                var normals = baked.normals;
                var colors = new Color[vertices.Length];
                int whiteVertices = 0;
                for (int vertex = 0; vertex < vertices.Length; vertex++)
                {
                    Vector3 world = renderer.transform.TransformPoint(vertices[vertex]);
                    Vector3 normal = renderer.transform.TransformDirection(normals[vertex]).normalized;
                    float mask = 0f;
                    for (int finger = 0; finger < 4; finger++)
                    {
                        Vector3 offset = world - starts[finger];
                        float along = Vector3.Dot(offset, directions[finger]);
                        float t = along / lengths[finger];
                        float radius = (offset - directions[finger] * along).magnitude;
                        if (t > 0.08f && t < 0.9f &&
                            radius < FingerWidths[finger] * hand.Scale * 0.9f &&
                            Vector3.Dot(normal, dorsals[finger]) > 0.2f)
                            mask = 1f;
                    }
                    colors[vertex] = new Color(mask, mask, mask, 1f);
                    if (mask > 0f) whiteVertices++;
                }
                Destroy(baked);
                var coloredMesh = Instantiate(renderer.sharedMesh);
                coloredMesh.name = "GravityRoom Skinned Glove Surface";
                coloredMesh.colors = colors;
                ownedMeshes.Add(coloredMesh);
                if (fingerSurfaceMaterial == null)
                {
                    fingerSurfaceMaterial = new Material(blackMaterial);
                    fingerSurfaceMaterial.SetFloat("_UseVertexPanels", 1f);
                    fingerSurfaceMaterial.SetColor("_PanelColor", whiteMaterial.GetColor("_BaseColor"));
                }
                renderer.sharedMesh = coloredMesh;
                renderer.sharedMaterial = fingerSurfaceMaterial;
                coloredHands.Add(renderer);
                Debug.Log($"[GravityRoom] Finger surface {name}: whiteVertices={whiteVertices}/{vertices.Length}");
            }
        }

        private void UpdateArm(IHand hand, Vector3 wrist, Vector3 handDorsal, float scale)
        {
            if (head == null || !IsFinite(wrist) || !UpdateBodyBasis())
            {
                SetArmActive(false);
                return;
            }

            float sideSign = hand.Handedness == Handedness.Left ? -1f : 1f;
            Vector3 worldUp = Vector3.up;
            Vector3 bodyRight = Vector3.Cross(worldUp, bodyForward).normalized;
            if (bodyRight.sqrMagnitude < 0.9f)
            {
                SetArmActive(false);
                return;
            }

            // This is deliberately a conservative avatar estimate, not body tracking.
            // The shoulder follows head position and smoothed HMD yaw only.
            Vector3 shoulder = head.position
                + bodyRight * (sideSign * 0.19f * scale)
                - worldUp * (0.205f * scale)
                - bodyForward * (0.025f * scale);
            Vector3 shoulderToWrist = wrist - shoulder;
            float distance = shoulderToWrist.magnitude;
            if (!IsFinite(shoulder) || !IsFinite(distance) || distance < 0.10f * scale)
            {
                SetArmActive(false);
                return;
            }

            float upperLength = 0.35f * scale;
            float forearmLength = 0.31f * scale;
            float naturalReach = upperLength + forearmLength;
            if (distance > naturalReach * 0.985f)
            {
                float reachScale = Mathf.Min(distance / (naturalReach * 0.985f), 1.08f);
                upperLength *= reachScale;
                forearmLength *= reachScale;
            }

            // Do not draw a rubber arm when the inferred shoulder and tracked wrist are
            // anatomically incompatible. The glove remains visible and wrist-accurate.
            if (distance >= (upperLength + forearmLength) * 0.999f ||
                distance <= Mathf.Abs(upperLength - forearmLength) + 0.001f)
            {
                SetArmActive(false);
                return;
            }

            Vector3 reach = shoulderToWrist / distance;
            Vector3 desiredBend = (-worldUp * 0.88f + bodyRight * (sideSign * 0.34f)).normalized;
            Vector3 bend = Vector3.ProjectOnPlane(desiredBend, reach);
            if (bend.sqrMagnitude < 0.0001f)
                bend = Vector3.ProjectOnPlane(bodyForward, reach);
            if (bend.sqrMagnitude < 0.0001f)
                bend = Vector3.ProjectOnPlane(bodyRight * sideSign, reach);
            if (bend.sqrMagnitude < 0.0001f)
            {
                SetArmActive(false);
                return;
            }
            bend.Normalize();
            if (lastElbowBend.sqrMagnitude > 0.5f)
            {
                Vector3 previous = Vector3.ProjectOnPlane(lastElbowBend, reach);
                if (previous.sqrMagnitude > 0.0001f)
                {
                    previous.Normalize();
                    if (Vector3.Dot(previous, bend) < 0f)
                        bend = -bend;
                    bend = Vector3.Slerp(previous, bend, 0.18f).normalized;
                }
            }
            lastElbowBend = bend;

            float along = (upperLength * upperLength - forearmLength * forearmLength +
                           distance * distance) / (2f * distance);
            float heightSquared = upperLength * upperLength - along * along;
            if (!IsFinite(along) || heightSquared < -0.0001f)
            {
                SetArmActive(false);
                return;
            }
            float height = Mathf.Sqrt(Mathf.Max(0f, heightSquared));
            Vector3 elbow = shoulder + reach * along + bend * height;
            if (!IsFinite(elbow))
            {
                SetArmActive(false);
                return;
            }

            SetArmActive(true);
            Vector3 forearmUp = StableSegmentUp(wrist - elbow, handDorsal, bodyForward);

            // The shared tapered mesh is narrow at its first endpoint and wider at
            // its second: wrist -> elbow follows the visible forearm.
            // The upper arm is used only by the elbow estimate, never rendered.
            SetSegment(forearmSleeve, wrist, elbow, forearmUp,
                0.043f * scale, 0.038f * scale);

            Vector3 upperDirection = (elbow - shoulder).normalized;
            Vector3 lowerDirection = (wrist - elbow).normalized;
            Vector3 elbowAxis = SafeDirection(upperDirection + lowerDirection, lowerDirection);
            SetPart(elbowJoint, elbow, Quaternion.LookRotation(elbowAxis,
                    StableSegmentUp(elbowAxis, bodyForward, handDorsal)),
                new Vector3(0.049f * scale, 0.044f * scale, 0.055f * scale));

            Vector3 wristDirection = SafeDirection(elbow - wrist, -bodyForward);
            SetPart(cuff, wrist + wristDirection * (0.018f * scale),
                Quaternion.LookRotation(wristDirection, forearmUp),
                new Vector3(0.043f * scale, 0.036f * scale, 0.038f * scale));
        }

        private bool UpdateBodyBasis()
        {
            if (bodyPoseFrame == Time.frameCount)
                return bodyPoseInitialized;
            bodyPoseFrame = Time.frameCount;

            Vector3 targetForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (!IsFinite(targetForward) || targetForward.sqrMagnitude < 0.0001f)
                return bodyPoseInitialized;
            targetForward.Normalize();

            if (!bodyPoseInitialized)
            {
                bodyForward = targetForward;
                bodyPoseInitialized = true;
            }
            else
            {
                float blend = 1f - Mathf.Exp(-6f * Mathf.Max(Time.unscaledDeltaTime, 0.0001f));
                bodyForward = Vector3.Slerp(bodyForward, targetForward, blend).normalized;
            }
            return IsFinite(bodyForward);
        }

        private void SetArmActive(bool active)
        {
            if (forearmSleeve != null) forearmSleeve.gameObject.SetActive(active);
            if (elbowJoint != null) elbowJoint.gameObject.SetActive(active);
        }

        private void SetGeneratedRenderers(bool visible)
        {
            foreach (Renderer renderer in generatedRenderers)
                if (renderer != null)
                    renderer.enabled = visible;
        }

        private static void SetSegment(Transform part, Vector3 start, Vector3 end, Vector3 up,
            float radiusX, float radiusY)
        {
            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (length < 0.0001f)
            {
                part.gameObject.SetActive(false);
                return;
            }
            part.SetPositionAndRotation((start + end) * 0.5f,
                Quaternion.LookRotation(delta / length, up));
            part.localScale = new Vector3(radiusX, radiusY, length);
        }

        private static Vector3 StableSegmentUp(Vector3 axis, Vector3 preferred, Vector3 fallback)
        {
            Vector3 up = Vector3.ProjectOnPlane(preferred, axis);
            if (up.sqrMagnitude < 0.0001f)
                up = Vector3.ProjectOnPlane(fallback, axis);
            if (up.sqrMagnitude < 0.0001f)
                up = Vector3.ProjectOnPlane(Vector3.up, axis);
            return up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.right;
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool TryGetPose(IHand hand, HandJointId joint, out Pose pose) =>
            hand.GetJointPose(joint, out pose);

        private static Vector3 SafeDirection(Vector3 direction, Vector3 fallback) =>
            direction.sqrMagnitude > 0.000001f ? direction.normalized : fallback.normalized;

        private static void SetPart(Transform part, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            part.SetPositionAndRotation(position, rotation);
            part.localScale = scale;
        }

        private static Mesh BuildChamferedPlateMesh()
        {
            const float bevel = 0.18f;
            Vector2[] outline =
            {
                new(-0.5f + bevel, -0.5f), new(0.5f - bevel, -0.5f),
                new(0.5f, -0.5f + bevel), new(0.5f, 0.5f - bevel),
                new(0.5f - bevel, 0.5f), new(-0.5f + bevel, 0.5f),
                new(-0.5f, 0.5f - bevel), new(-0.5f, -0.5f + bevel)
            };
            var vertices = new List<Vector3>(18);
            vertices.Add(new Vector3(0f, -0.5f, 0f));
            vertices.Add(new Vector3(0f, 0.5f, 0f));
            foreach (Vector2 point in outline)
                vertices.Add(new Vector3(point.x, -0.5f, point.y));
            foreach (Vector2 point in outline)
                vertices.Add(new Vector3(point.x, 0.5f, point.y));

            var triangles = new List<int>(96);
            for (int i = 0; i < 8; i++)
            {
                int next = (i + 1) % 8;
                int bottom = 2 + i;
                int bottomNext = 2 + next;
                int top = 10 + i;
                int topNext = 10 + next;
                triangles.AddRange(new[] { 0, bottom, bottomNext, 1, topNext, top,
                    bottom, topNext, bottomNext, bottom, top, topNext });
            }

            var mesh = new Mesh { name = "GravityRoom Chamfered Glove Plate" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildTubeMesh(string meshName, float nearRadius, float farRadius)
        {
            const int sides = 12;
            var vertices = new Vector3[sides * 2];
            var triangles = new int[sides * 6];
            for (int i = 0; i < sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                Vector2 radial = new(Mathf.Cos(angle), Mathf.Sin(angle));
                vertices[i] = new Vector3(radial.x * nearRadius, radial.y * nearRadius, -0.5f);
                vertices[i + sides] = new Vector3(radial.x * farRadius, radial.y * farRadius, 0.5f);
                int next = (i + 1) % sides;
                int triangle = i * 6;
                triangles[triangle] = i;
                triangles[triangle + 1] = next + sides;
                triangles[triangle + 2] = i + sides;
                triangles[triangle + 3] = i;
                triangles[triangle + 4] = next;
                triangles[triangle + 5] = next + sides;
            }

            var mesh = new Mesh { name = meshName };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
