using System.Collections;
using UnityEngine;
using InfiniteWorld;

namespace Vehicle
{
    /// <summary>
    /// Detects tree trunks ahead of the car and triggers:
    ///   Stop → pause → reverse → resume driving.
    ///
    /// Detection uses Physics.OverlapSphere every frame (no tag dependency —
    /// identifies trees by the TreePhysics component anywhere in their hierarchy).
    ///
    /// Speed override is done directly so the car stops INSTANTLY regardless of
    /// VRCarController's internal deceleration logic.
    /// </summary>
    [RequireComponent(typeof(VRCarController))]
    public class TreeCollisionResponse : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Detection")]
        [Tooltip("Radius of the forward detection sphere (metres). Increase if trees are missed.")]
        public float detectionRadius = 2.0f;

        [Tooltip("How far forward from the car pivot to place the probe sphere.")]
        public float detectionForwardOffset = 1.8f;

        [Tooltip("Height above car pivot for the probe (should reach trunk height).")]
        public float detectionHeight = 0.9f;

        [Header("Response")]
        [Tooltip("Seconds to hold still after impact before reversing.")]
        public float pauseDuration = 0.4f;

        [Tooltip("How fast to reverse (km/h).")]
        public float reverseSpeedKmh = 20f;

        [Tooltip("How long to reverse (seconds).")]
        public float reverseDuration = 1.8f;

        [Tooltip("Seconds after reversing before re-enabling the hit sequence (prevents loop).")]
        public float cooldownAfterReverse = 0.8f;

        [Header("Camera Shake")]
        public bool  enableShake   = true;
        public float shakeStrength = 0.15f;
        public float shakeDuration = 0.25f;

        // ── Private ───────────────────────────────────────────────────────────

        private VRCarController _car;
        private Camera          _cam;
        private Vector3         _camLocalOrigin;

        private bool  _active;       // true while the hit coroutine is running
        private float _cooldown;     // countdown before we can trigger again

        // Non-alloc overlap buffer
        private readonly Collider[] _overlapBuffer = new Collider[16];

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            _car = GetComponent<VRCarController>();
            _cam = Camera.main;
            if (_cam != null) _camLocalOrigin = _cam.transform.localPosition;
        }

        private void Update()
        {
            // Count down cooldown
            if (_cooldown > 0f) { _cooldown -= Time.deltaTime; return; }

            if (_active) return;
            if (_car.SpeedKmh < 1f) return;   // ignore when stationary

            // Build probe position
            Vector3 probe = transform.position
                          + transform.forward * detectionForwardOffset
                          + Vector3.up        * detectionHeight;

            // Non-alloc overlap sphere — checks all layers
            int count = Physics.OverlapSphereNonAlloc(probe, detectionRadius,
                                                      _overlapBuffer, ~0,
                                                      QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider col = _overlapBuffer[i];
                if (col == null) continue;

                // Skip colliders that belong to this car
                if (col.transform.IsChildOf(transform)) continue;

                // Identify as a tree: check for TreePhysics anywhere up the hierarchy
                if (col.GetComponentInParent<TreePhysics>() == null) continue;

                // It's a tree — react!
                StartCoroutine(HitSequence());
                return;
            }
        }

        // ── Hit Sequence ──────────────────────────────────────────────────────

        private IEnumerator HitSequence()
        {
            _active = true;

            // ── 1. Lock input and instant stop ───────────────────────────────
            _car.inputLocked = true;
            _car.ForceSetSpeed(0f);

            if (enableShake && _cam != null)
                StartCoroutine(ShakeCamera());

            // ── 2. Hold for pauseDuration ──────────────────────────────────
            float t = 0f;
            while (t < pauseDuration)
            {
                _car.ForceSetSpeed(0f);   // keep enforcing zero during pause
                t += Time.deltaTime;
                yield return null;
            }

            // ── 3. Reverse ────────────────────────────────────────────────────
            float elapsed = 0f;
            float speedMs = reverseSpeedKmh / 3.6f;

            while (elapsed < reverseDuration)
            {
                float dt = Time.deltaTime;
                elapsed += dt;

                _car.ForceSetSpeed(0f);   // prevent VRCarController re-accelerating
                transform.Translate(Vector3.back * speedMs * dt, Space.Self);

                yield return null;
            }

            // ── 4. Resume ─────────────────────────────────────────────────────
            _car.inputLocked = false;     // re-enable player input
            _car.ForceSetSpeed(0f);       // start from 0 — player will throttle up naturally

            _cooldown = cooldownAfterReverse;
            _active   = false;
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private IEnumerator ShakeCamera()
        {
            float t = 0f;
            while (t < shakeDuration)
            {
                t += Time.deltaTime;
                float fade   = 1f - (t / shakeDuration);
                Vector3 off  = Random.insideUnitSphere * shakeStrength * fade;
                _cam.transform.localPosition = _camLocalOrigin + off;
                yield return null;
            }
            _cam.transform.localPosition = _camLocalOrigin;
        }

        // ── Gizmo ──────────────────────────────────────────────────────────────
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Vector3 probe = transform.position
                          + transform.forward * detectionForwardOffset
                          + Vector3.up        * detectionHeight;
            Gizmos.color = _active ? Color.red : new Color(1f, 0.8f, 0f);
            Gizmos.DrawWireSphere(probe, detectionRadius);
            Gizmos.DrawLine(transform.position, probe);
        }
#endif
    }
}
