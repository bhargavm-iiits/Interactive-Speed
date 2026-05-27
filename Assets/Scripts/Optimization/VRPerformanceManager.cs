using UnityEngine;
using UnityEngine.XR;

namespace Optimization
{
    /// <summary>
    /// Meta Quest 3 VR Performance Manager.
    ///
    /// Applies at startup:
    ///  • 72 FPS target framerate (Quest 3 native)
    ///  • Fixed timestep at 1/72
    ///  • Disable VSync (XR manages its own sync)
    ///  • Physics solver iteration reduction (acceptable for car physics)
    ///  • Quest-specific texture quality
    ///
    /// Disable this component to restore defaults (e.g., during PC testing).
    /// </summary>
    public class VRPerformanceManager : MonoBehaviour
    {
        [Header("Target FPS")]
        [Tooltip("Set 72 for Quest 3 (72 Hz default). Use 90 if you enable 90 Hz mode.")]
        public int targetFrameRate = 72;

        [Header("Physics")]
        [Tooltip("Physics update rate. 1/72 keeps it in sync with XR frame rate.")]
        public float fixedTimestep = 0.01388f;   // 1/72
        public int   physicsSolverIterations         = 8;
        public int   physicsSolverVelocityIterations = 4;

        [Header("Texture Quality")]
        [Tooltip("Mip level bias (0 = full res, 1 = half, 2 = quarter).")]
        public int textureMipBias = 1;

        [Header("Foveated Rendering")]
        [Tooltip("Attempt to enable fixed foveated rendering via XRSettings.")]
        public bool enableFoveatedRendering = true;

        private void Awake()
        {
            // Framerate
            Application.targetFrameRate = targetFrameRate;
            QualitySettings.vSyncCount  = 0;    // XR runtime handles sync

            // Physics
            Time.fixedDeltaTime                              = fixedTimestep;
            Physics.defaultSolverIterations                  = physicsSolverIterations;
            Physics.defaultSolverVelocityIterations          = physicsSolverVelocityIterations;

            // Texture streaming
            QualitySettings.globalTextureMipmapLimit = textureMipBias;

            // Foveated rendering (if supported by the XR runtime/device)
            if (enableFoveatedRendering)
            {
                // Try via XRDisplaySubsystem (URP + OpenXR path)
                TryEnableFoveation();
            }

            Debug.Log($"[VRPerformanceManager] Target FPS: {targetFrameRate} | " +
                      $"Fixed Timestep: {fixedTimestep:F5} | " +
                      $"Physics iters: {physicsSolverIterations}");
        }

        private static void TryEnableFoveation()
        {
            var displays = new System.Collections.Generic.List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays)
            {
                // foveatedRenderingLevel is available on OpenXR-backed displays
                // Attempt via reflection to avoid hard XRI dependency
                try
                {
                    var prop = d.GetType().GetProperty("foveatedRenderingLevel");
                    prop?.SetValue(d, 2);     // 2 = HighTop (device-dependent)
                    Debug.Log("[VRPerformanceManager] Foveated rendering requested.");
                }
                catch
                {
                    // Not supported on this runtime — silently skip
                }
            }
        }
    }
}
