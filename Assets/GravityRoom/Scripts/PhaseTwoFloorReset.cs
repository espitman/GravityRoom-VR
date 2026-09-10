using UnityEngine;

namespace GravityRoom
{
    /// <summary>Reports a real floor collision to the practice lifecycle in the same physics step.</summary>
    public sealed class PhaseTwoFloorReset : MonoBehaviour
    {
        [SerializeField] private PhaseTwoPracticeController controller;

        public PhaseTwoPracticeController Controller => controller;

        public void Configure(PhaseTwoPracticeController practiceController)
        {
            controller = practiceController;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (controller != null && collision.collider.name == "Floor")
                controller.ResetAfterFloorContact();
        }
    }
}
