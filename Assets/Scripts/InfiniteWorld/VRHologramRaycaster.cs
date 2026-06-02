using UnityEngine;
using UnityEngine.InputSystem;

namespace InfiniteWorld
{
    /// <summary>
    /// Implements a visual laser pointer that supports both VR controllers and mouse clicks on desktop.
    /// Interacts seamlessly with HolographicButtons in world space.
    /// </summary>
    public class VRHologramRaycaster : MonoBehaviour
    {
        [Header("Laser Visuals")]
        public Color laserColor = new Color(0.08f, 0.9f, 0.9f, 0.6f); // Cyan laser
        public float laserWidth = 0.015f;
        public float maxDistance = 25f;

        private LineRenderer _lineRenderer;
        private GameObject _cursorVisual;
        private HolographicButton _currentHoveredButton;

        private UnityEngine.XR.InputDevice _rightController;
        private Transform _cameraTransform;
        private bool _prevTriggerClicked = false;

        private void Start()
        {
            _cameraTransform = Camera.main != null ? Camera.main.transform : transform;

            // Create a LineRenderer for the visual laser beam
            _lineRenderer = gameObject.AddComponent<LineRenderer>();
            _lineRenderer.startWidth = laserWidth;
            _lineRenderer.endWidth = laserWidth * 0.4f;
            _lineRenderer.positionCount = 2;
            
            // Set laser material
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard"));
            mat.color = laserColor;
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", laserColor * 2f);
            }
            _lineRenderer.sharedMaterial = mat;

            // Create a small glowing cursor dot at the raycast hit point
            _cursorVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _cursorVisual.name = "LaserCursorDot";
            _cursorVisual.transform.localScale = Vector3.one * 0.08f;
            Destroy(_cursorVisual.GetComponent<Collider>());
            
            var cursorMat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard"));
            cursorMat.color = Color.white;
            _cursorVisual.GetComponent<Renderer>().sharedMaterial = cursorMat;
        }

        private void Update()
        {
            // Sync moving colliders instantly to the physics world for pixel-perfect raycasts with zero delay
            Physics.SyncTransforms();

            // Update Right Hand VR Device reference if needed
            if (!_rightController.isValid)
            {
                _rightController = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            }

            Vector3 rayOrigin = Vector3.zero;
            Vector3 rayDirection = Vector3.forward;
            bool isVR = false;

            // 1. Determine Ray Source
            // Try to find the physical VR right hand controller model/transform in the scene
            Transform rightHandAnchor = FindRightHandAnchor();
            if (rightHandAnchor != null && _rightController.isValid)
            {
                rayOrigin = rightHandAnchor.position;
                rayDirection = rightHandAnchor.forward;
                isVR = true;
            }
            else
            {
                // Desktop Mode: Raycast from camera through mouse pointer (safe from legacy input exceptions)
                if (Camera.main != null)
                {
                    Vector2 mousePos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                    if (Mouse.current != null)
                    {
                        try { mousePos = Mouse.current.position.ReadValue(); } catch { }
                    }
                    Ray mouseRay = Camera.main.ScreenPointToRay(mousePos);
                    rayOrigin = mouseRay.origin;
                    rayDirection = mouseRay.direction;
                }
                else
                {
                    rayOrigin = transform.position;
                    rayDirection = transform.forward;
                }
            }

            // If desktop mode, let's offset the visual line slightly to feel like a laser pointer from the dashboard/hand
            Vector3 visualLineStart = rayOrigin;
            if (!isVR && Camera.main != null)
            {
                visualLineStart = Camera.main.transform.position + Camera.main.transform.right * 0.25f + Camera.main.transform.up * -0.2f + Camera.main.transform.forward * 0.4f;
            }

            // 2. Perform Raycast
            RaycastHit hit;
            Vector3 targetPoint = rayOrigin + rayDirection * maxDistance;
            bool hitAnything = Physics.Raycast(rayOrigin, rayDirection, out hit, maxDistance);

            HolographicButton hitButton = null;

            if (hitAnything)
            {
                targetPoint = hit.point;
                hitButton = hit.collider.GetComponentInParent<HolographicButton>();
            }

            // 3. Update Visuals
            _lineRenderer.SetPosition(0, visualLineStart);
            _lineRenderer.SetPosition(1, targetPoint);

            _cursorVisual.transform.position = targetPoint;
            // Align cursor face to camera
            _cursorVisual.transform.rotation = Quaternion.LookRotation(rayDirection);

            // 4. Handle Button Hover States
            if (hitButton != _currentHoveredButton)
            {
                if (_currentHoveredButton != null)
                {
                    _currentHoveredButton.SetHovered(false);
                }

                _currentHoveredButton = hitButton;

                if (_currentHoveredButton != null)
                {
                    _currentHoveredButton.SetHovered(true);
                }
            }

            // 5. Handle Click / Trigger Click
            if (_currentHoveredButton != null)
            {
                bool clicked = false;

                if (isVR && _rightController.isValid)
                {
                    bool triggerDown = false;
                    // Check Quest 3 Trigger Click (digital down)
                    _rightController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out triggerDown);
                    // Also support analog trigger pull threshold as click
                    if (!triggerDown)
                    {
                        float triggerValue;
                        if (_rightController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out triggerValue))
                        {
                            triggerDown = triggerValue > 0.75f;
                        }
                    }

                    // Click transitions: only trigger click on the frame it transitions to true
                    if (triggerDown && !_prevTriggerClicked)
                    {
                        clicked = true;
                    }
                    _prevTriggerClicked = triggerDown;
                }
                else
                {
                    _prevTriggerClicked = false;

                    // Desktop Left Mouse Click using new InputSystem (safe from legacy input exceptions)
                    if (Mouse.current != null)
                    {
                        try { clicked = Mouse.current.leftButton.wasPressedThisFrame; } catch { }
                    }
                }

                if (clicked)
                {
                    _currentHoveredButton.Click();
                }
            }
            else
            {
                // Track trigger state even when not hovering to avoid click-on-enter triggers
                if (isVR && _rightController.isValid)
                {
                    bool triggerDown = false;
                    _rightController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out triggerDown);
                    if (!triggerDown)
                    {
                        float triggerValue;
                        if (_rightController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out triggerValue))
                        {
                            triggerDown = triggerValue > 0.75f;
                        }
                    }
                    _prevTriggerClicked = triggerDown;
                }
                else
                {
                    _prevTriggerClicked = false;
                }
            }
        }

        private Transform FindRightHandAnchor()
        {
            // Look for typical XRI structure or hands under VRCar
            var driver = FindFirstObjectByType<StraightLineDriver>();
            if (driver != null)
            {
                // Search deep for controllers or hand anchors
                Transform rHand = FindDeep(driver.transform, "RightHand");
                if (rHand != null) return rHand;
                
                rHand = FindDeep(driver.transform, "RightHand Controller");
                if (rHand != null) return rHand;
                
                rHand = FindDeep(driver.transform, "Right Controller");
                if (rHand != null) return rHand;

                rHand = FindDeep(driver.transform, "RightHandAnchor");
                if (rHand != null) return rHand;
            }

            // Fallback: look for "RightHand Controller" anywhere in scene
            GameObject go = GameObject.Find("RightHand Controller") ?? GameObject.Find("RightHand") ?? GameObject.Find("Right Controller");
            if (go != null) return go.transform;

            return null;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name.Contains(name)) return root;
            foreach (Transform child in root)
            {
                var found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private void OnDestroy()
        {
            if (_lineRenderer != null && _lineRenderer.sharedMaterial != null)
            {
                Destroy(_lineRenderer.sharedMaterial);
            }
            if (_cursorVisual != null)
            {
                Destroy(_cursorVisual);
            }
        }
    }
}
