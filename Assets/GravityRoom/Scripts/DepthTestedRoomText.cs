using UnityEngine;

namespace GravityRoom
{
    [RequireComponent(typeof(TextMesh))]
    public sealed class DepthTestedRoomText : MonoBehaviour
    {
        [SerializeField] private Shader textShader;
        private TextMesh text;
        private Material material;

        public void Configure(Shader shader) => textShader = shader;

        private void Awake()
        {
            text = GetComponent<TextMesh>();
            material = new Material(text.font.material) { shader = textShader };
            GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private void OnEnable() => Font.textureRebuilt += RefreshAtlas;
        private void OnDisable() => Font.textureRebuilt -= RefreshAtlas;
        private void RefreshAtlas(Font font)
        {
            if (text != null && material != null && font == text.font)
                material.mainTexture = font.material.mainTexture;
        }
        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}
