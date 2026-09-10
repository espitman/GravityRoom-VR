using UnityEngine;

namespace GravityRoom
{
    /// <summary>Runs the deterministic Phase 3 gravity cycle and its VR-facing feedback.</summary>
    [DisallowMultipleComponent]
    public sealed class PhaseThreeGravityController : MonoBehaviour
    {
        private const int WarningSampleRate = 24000;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly Color NormalArrowColor = new(0.08f, 0.78f, 1f, 1f);
        private static readonly Color WarningArrowColor = new(1f, 0.32f, 0.06f, 1f);

        [Header("References")]
        [SerializeField] private DirectionalGravityBody gravityBody;
        [SerializeField] private TextMesh modeText;
        [SerializeField] private Transform directionArrow;
        [SerializeField] private Renderer[] arrowRenderers;
        [SerializeField] private AudioSource warningAudioSource;

        [Header("Initial Quest tuning")]
        [SerializeField, Min(0f)] private float gravityStrength = 2.5f;
        [SerializeField, Min(3.1f)] private float changeInterval = 12f;
        [SerializeField, Range(1f, 3f)] private float countdownDuration = 3f;

        private AudioClip warningClip;
        private Material runtimeArrowMaterial;
        private MaterialPropertyBlock arrowProperties;
        private float timeUntilChange;
        private bool warningPlayed;
        private GravityMode displayedMode = (GravityMode)(-1);
        private int displayedCountdown = int.MinValue;

        public DirectionalGravityBody GravityBody => gravityBody;
        public TextMesh ModeText => modeText;
        public Transform DirectionArrow => directionArrow;
        public AudioSource WarningAudioSource => warningAudioSource;
        public float GravityStrength => gravityStrength;
        public float ChangeInterval => changeInterval;
        public float CountdownDuration => countdownDuration;
        public float TimeUntilChange => timeUntilChange;

        public void Configure(DirectionalGravityBody target, TextMesh display, Transform arrow,
            Renderer[] renderers, AudioSource audioSource, float strength, float interval, float countdown)
        {
            gravityBody = target;
            modeText = display;
            directionArrow = arrow;
            arrowRenderers = renderers;
            warningAudioSource = audioSource;
            gravityStrength = Mathf.Max(0f, strength);
            changeInterval = Mathf.Max(3.1f, interval);
            countdownDuration = Mathf.Clamp(countdown, 1f, 3f);
            gravityBody?.SetAcceleration(gravityStrength);
            ResetInterval();
            RefreshFeedback(true);
        }

        private void Awake()
        {
            EnsureRuntimeFeedback();
            arrowProperties = new MaterialPropertyBlock();
            warningClip = CreateWarningClip();
            warningAudioSource.clip = warningClip;
            gravityBody?.SetAcceleration(gravityStrength);
            ResetInterval();
            RefreshFeedback(true);
        }

        private void OnDestroy()
        {
            if (warningClip != null)
                Destroy(warningClip);
            if (runtimeArrowMaterial != null)
                Destroy(runtimeArrowMaterial);
        }

        private void Update()
        {
            if (OVRInput.GetDown(OVRInput.RawButton.A))
            {
                SelectNextMode();
                return;
            }
            if (OVRInput.GetDown(OVRInput.RawButton.X))
            {
                SelectPreviousMode();
                return;
            }

            timeUntilChange -= Time.deltaTime;
            if (timeUntilChange <= 0f)
            {
                SetModeInternal(GravityCycleRules.Next(CurrentMode));
                return;
            }

            if (!warningPlayed && timeUntilChange <= countdownDuration)
            {
                warningPlayed = true;
                if (warningAudioSource != null && warningClip != null)
                    warningAudioSource.Play();
            }
            RefreshFeedback(false);
        }

        public void SelectNextMode() => SetMode(GravityCycleRules.Next(CurrentMode));

        public void SelectPreviousMode() => SetMode(GravityCycleRules.Previous(CurrentMode));

        public void SetMode(GravityMode mode) => SetModeInternal(mode);

        private GravityMode CurrentMode => gravityBody != null ? gravityBody.Mode : GravityMode.Down;

        private void SetModeInternal(GravityMode mode)
        {
            if (gravityBody == null) return;
            gravityBody.SetMode(mode);
            ResetInterval();
            RefreshFeedback(true);
            Debug.Log($"[GravityRoom] Phase 3 gravity mode: {mode}; next automatic change in {changeInterval:F1}s.");
        }

        private void ResetInterval()
        {
            if (warningAudioSource != null && warningAudioSource.isPlaying)
                warningAudioSource.Stop();
            timeUntilChange = changeInterval;
            warningPlayed = false;
            displayedCountdown = int.MinValue;
        }

        private void RefreshFeedback(bool force)
        {
            GravityMode current = CurrentMode;
            int countdown = GravityCycleRules.CountdownNumber(timeUntilChange, countdownDuration);
            if (!force && displayedMode == current && displayedCountdown == countdown) return;

            displayedMode = current;
            displayedCountdown = countdown;
            if (modeText != null)
            {
                string mode = current.ToString().ToUpperInvariant();
                modeText.text = countdown > 0
                    ? $"GRAVITY: {mode}   CHANGE IN {countdown}"
                    : $"GRAVITY: {mode}   A / X: MANUAL";
                modeText.color = countdown > 0 ? WarningArrowColor : Color.black;
            }

            if (directionArrow != null)
                directionArrow.localRotation = Quaternion.Euler(0f, 0f, GravityCycleRules.ArrowAngle(current));
            SetArrowColor(countdown > 0 ? WarningArrowColor : NormalArrowColor);
        }

        private void SetArrowColor(Color color)
        {
            if (arrowRenderers == null || arrowRenderers.Length == 0) return;
            arrowProperties ??= new MaterialPropertyBlock();
            arrowProperties.SetColor(BaseColorId, color);
            arrowProperties.SetColor(ColorId, color);
            foreach (Renderer arrowRenderer in arrowRenderers)
                if (arrowRenderer != null) arrowRenderer.SetPropertyBlock(arrowProperties);
        }

        private void EnsureRuntimeFeedback()
        {
            if (directionArrow == null)
                directionArrow = CreateRuntimeDirectionArrow();
            if (arrowRenderers == null || arrowRenderers.Length == 0)
                arrowRenderers = directionArrow.GetComponentsInChildren<Renderer>(true);

            if (warningAudioSource == null)
                warningAudioSource = gameObject.AddComponent<AudioSource>();
            warningAudioSource.playOnAwake = false;
            warningAudioSource.loop = false;
            warningAudioSource.spatialBlend = 1f;
            warningAudioSource.rolloffMode = AudioRolloffMode.Linear;
            warningAudioSource.minDistance = 0.75f;
            warningAudioSource.maxDistance = 8f;
            warningAudioSource.volume = 0.8f;
        }

        private Transform CreateRuntimeDirectionArrow()
        {
            var root = new GameObject("Gravity Direction Arrow (Runtime Fallback)");
            root.transform.SetParent(transform, false);
            root.transform.SetPositionAndRotation(new Vector3(-1.75f, 1.42f, 2.84f), Quaternion.identity);

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
                runtimeArrowMaterial = new Material(shader) { name = "Runtime Gravity Arrow" };
            Renderer shaft = CreateRuntimeArrowPart("Arrow Shaft", root.transform,
                new Vector3(0f, 0.13f, 0f), new Vector3(0.14f, 0.5f, 0.055f), 0f);
            Renderer headLeft = CreateRuntimeArrowPart("Arrow Head Left", root.transform,
                new Vector3(-0.12f, -0.22f, 0f), new Vector3(0.12f, 0.36f, 0.055f), -45f);
            Renderer headRight = CreateRuntimeArrowPart("Arrow Head Right", root.transform,
                new Vector3(0.12f, -0.22f, 0f), new Vector3(0.12f, 0.36f, 0.055f), 45f);
            arrowRenderers = new[] { shaft, headLeft, headRight };
            return root.transform;
        }

        private Renderer CreateRuntimeArrowPart(string objectName, Transform parent, Vector3 localPosition,
            Vector3 localScale, float zRotation)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = objectName;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = Quaternion.Euler(0f, 0f, zRotation);
            part.transform.localScale = localScale;
            Collider partCollider = part.GetComponent<Collider>();
            partCollider.enabled = false;
            Destroy(partCollider);
            Renderer renderer = part.GetComponent<Renderer>();
            if (runtimeArrowMaterial != null)
                renderer.sharedMaterial = runtimeArrowMaterial;
            return renderer;
        }

        private static AudioClip CreateWarningClip()
        {
            const float duration = 0.18f;
            int sampleCount = Mathf.CeilToInt(WarningSampleRate * duration);
            var samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)WarningSampleRate;
                float envelope = Mathf.Sin(Mathf.PI * i / (sampleCount - 1));
                float frequency = Mathf.Lerp(660f, 990f, t / duration);
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * 0.35f;
            }
            AudioClip clip = AudioClip.Create("Phase 3 procedural warning", sampleCount, 1,
                WarningSampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }

    public static class GravityCycleRules
    {
        public static GravityMode Next(GravityMode mode) => mode switch
        {
            GravityMode.Down => GravityMode.Left,
            GravityMode.Left => GravityMode.Right,
            _ => GravityMode.Down
        };

        public static GravityMode Previous(GravityMode mode) => mode switch
        {
            GravityMode.Down => GravityMode.Right,
            GravityMode.Left => GravityMode.Down,
            _ => GravityMode.Left
        };

        public static int CountdownNumber(float timeUntilChange, float countdownDuration) =>
            timeUntilChange > 0f && timeUntilChange <= countdownDuration
                ? Mathf.Clamp(Mathf.CeilToInt(timeUntilChange), 1, Mathf.CeilToInt(countdownDuration))
                : 0;

        public static float ArrowAngle(GravityMode mode) => mode switch
        {
            GravityMode.Left => -90f,
            GravityMode.Right => 90f,
            _ => 0f
        };
    }
}
