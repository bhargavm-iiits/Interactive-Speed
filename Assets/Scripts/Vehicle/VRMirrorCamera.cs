using UnityEngine;

namespace Vehicle
{
    /// <summary>
    /// Mirror cameras for VR — each renders to a RenderTexture assigned to a mirror mesh.
    /// Only renders every N frames to save GPU budget on Meta Quest 3.
    /// </summary>
    public class VRMirrorCamera : MonoBehaviour
    {
        [Header("Mirror Setup")]
        [Tooltip("Camera for this mirror. Assign or auto-create.")]
        public Camera mirrorCamera;
        [Tooltip("Mesh renderer of the mirror surface.")]
        public Renderer mirrorRenderer;
        [Tooltip("Material property name to assign the render texture.")]
        public string texturePropertyName = "_BaseMap";
        [Tooltip("Render texture resolution. Keep low for Quest (128 or 256).")]
        public int renderSize = 128;
        [Tooltip("Only render every N frames. 0 = every frame.")]
        public int renderEveryNFrames = 4;

        private RenderTexture _rt;
        private int _frameCounter;

        private void Awake()
        {
            // Create render texture
            _rt = new RenderTexture(renderSize, renderSize / 2, 16, RenderTextureFormat.Default);
            _rt.Create();

            // Auto-create camera if not assigned
            if (mirrorCamera == null)
            {
                var camGO    = new GameObject("MirrorCam");
                camGO.transform.SetParent(transform, false);
                mirrorCamera = camGO.AddComponent<Camera>();
            }

            mirrorCamera.targetTexture = _rt;
            mirrorCamera.fieldOfView   = 80f;
            mirrorCamera.nearClipPlane = 0.05f;
            mirrorCamera.farClipPlane  = 300f;
            mirrorCamera.enabled       = false;    // we manually render

            // Assign RT to mirror material
            if (mirrorRenderer != null)
                mirrorRenderer.material.SetTexture(texturePropertyName, _rt);
        }

        private void LateUpdate()
        {
            if (mirrorCamera == null) return;

            _frameCounter++;
            if (renderEveryNFrames <= 0 || _frameCounter % renderEveryNFrames == 0)
                mirrorCamera.Render();
        }

        private void OnDestroy()
        {
            if (_rt != null) _rt.Release();
        }
    }
}
