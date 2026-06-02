using UnityEngine;

namespace InfiniteWorld
{
    /// <summary>
    /// Attaches to each planted tree to give it realistic collision physics.
    ///
    /// At rest the tree is kinematic (zero CPU cost).
    /// When a vehicle collides hard enough, the tree switches to dynamic and
    /// falls over under gravity and the impact impulse.
    /// After <see cref="destroyDelay"/> seconds it fades out and is destroyed.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class TreePhysics : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Trigger")]
        [Tooltip("Minimum collision impulse magnitude required to topple the tree.")]
        public float toppleThreshold = 4f;

        [Tooltip("Extra upward and outward force applied when the tree is hit.")]
        public float impactForceMultiplier = 2.5f;

        [Header("Fall")]
        [Tooltip("Angular drag applied once the tree starts falling (slows the fall a little).")]
        public float fallingAngularDrag = 1.2f;

        [Tooltip("Linear drag while falling — keeps the tree from sliding too far.")]
        public float fallingLinearDrag = 0.8f;

        [Header("Cleanup")]
        [Tooltip("Seconds after toppling before the tree fades out and is destroyed.")]
        public float destroyDelay = 8f;

        [Tooltip("Duration of the fade-out in seconds.")]
        public float fadeDuration = 1.5f;

        // ── Private state ─────────────────────────────────────────────────────

        private Rigidbody  _rb;
        private bool       _toppled;
        private float      _destroyTimer;
        private Renderer[] _renderers;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();

            // Start kinematic — no physics cost while tree is standing still
            _rb.isKinematic = true;
            _rb.useGravity  = false;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;

            // Cache all renderers for the fade-out
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        private void OnCollisionEnter(Collision col)
        {
            if (_toppled) return;

            // Only react to significant impulses (avoids tiny debris triggering it)
            float impulseMag = col.impulse.magnitude;
            if (impulseMag < toppleThreshold) return;

            Topple(col);
        }

        private void Update()
        {
            if (!_toppled) return;

            _destroyTimer += Time.deltaTime;

            // Begin fade once the destroy window is close
            float fadeStart = destroyDelay - fadeDuration;
            if (_destroyTimer >= fadeStart)
            {
                float t = Mathf.Clamp01((_destroyTimer - fadeStart) / fadeDuration);
                SetAlpha(1f - t);
            }

            if (_destroyTimer >= destroyDelay)
                Destroy(gameObject);
        }

        // ── Physics topple ────────────────────────────────────────────────────

        private void Topple(Collision col)
        {
            _toppled = true;

            // Switch from kinematic to fully simulated
            _rb.isKinematic = false;
            _rb.useGravity  = true;
            _rb.angularDamping = fallingAngularDrag;
            _rb.linearDamping  = fallingLinearDrag;

            // Work out the fall direction (away from the impact contact point)
            Vector3 contactNormal = col.contacts[0].normal;

            // Apply the collision impulse scaled up, directed away from the vehicle
            Vector3 impulseDir = (-contactNormal + Vector3.up * 0.3f).normalized;
            _rb.AddForce(impulseDir * col.impulse.magnitude * impactForceMultiplier,
                         ForceMode.Impulse);

            // Add a torque so the tree rotates as it falls (not just sliding)
            Vector3 torqueAxis = Vector3.Cross(Vector3.up, -contactNormal).normalized;
            _rb.AddTorque(torqueAxis * col.impulse.magnitude * impactForceMultiplier * 0.5f,
                          ForceMode.Impulse);

            _destroyTimer = 0f;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void SetAlpha(float alpha)
        {
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                foreach (var mat in r.materials)
                {
                    if (mat == null) continue;
                    // URP Lit uses Surface Type > Transparent when alpha < 1
                    // We change the color alpha on whatever _BaseColor / _Color exists
                    if (mat.HasProperty("_BaseColor"))
                    {
                        Color c = mat.GetColor("_BaseColor");
                        c.a = alpha;
                        mat.SetColor("_BaseColor", c);
                    }
                    else if (mat.HasProperty("_Color"))
                    {
                        Color c = mat.GetColor("_Color");
                        c.a = alpha;
                        mat.SetColor("_Color", c);
                    }
                }
            }
        }
    }
}
