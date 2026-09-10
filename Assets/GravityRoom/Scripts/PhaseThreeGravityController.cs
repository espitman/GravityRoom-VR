using UnityEngine;

namespace GravityRoom
{
    /// <summary>Manual Phase 3 mode selection; timing and random switching belong to a later milestone.</summary>
    [DisallowMultipleComponent]
    public sealed class PhaseThreeGravityController : MonoBehaviour
    {
        [SerializeField] private DirectionalGravityBody gravityBody;
        [SerializeField] private TextMesh modeText;

        public DirectionalGravityBody GravityBody => gravityBody;
        public TextMesh ModeText => modeText;

        public void Configure(DirectionalGravityBody target, TextMesh display)
        {
            gravityBody = target;
            modeText = display;
            RefreshDisplay();
        }

        private void Awake()
        {
            RefreshDisplay();
        }

        private void Update()
        {
            if (OVRInput.GetDown(OVRInput.RawButton.A))
                SelectNextMode();
            if (OVRInput.GetDown(OVRInput.RawButton.X))
                SelectPreviousMode();
        }

        public void SelectNextMode() => SetMode(OffsetMode(1));

        public void SelectPreviousMode() => SetMode(OffsetMode(-1));

        public void SetMode(GravityMode mode)
        {
            if (gravityBody == null) return;
            gravityBody.SetMode(mode);
            RefreshDisplay();
            Debug.Log($"[GravityRoom] Phase 3 gravity mode: {mode}.");
        }

        private GravityMode OffsetMode(int offset)
        {
            int count = System.Enum.GetValues(typeof(GravityMode)).Length;
            int current = gravityBody != null ? (int)gravityBody.Mode : 0;
            return (GravityMode)((current + offset + count) % count);
        }

        private void RefreshDisplay()
        {
            if (modeText == null) return;
            GravityMode current = gravityBody != null ? gravityBody.Mode : GravityMode.Down;
            modeText.text = $"GRAVITY: {current.ToString().ToUpperInvariant()}   A / X: CHANGE MODE";
            modeText.color = Color.black;
        }
    }
}
