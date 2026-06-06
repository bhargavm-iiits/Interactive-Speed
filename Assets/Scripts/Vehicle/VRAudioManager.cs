using UnityEngine;

namespace Vehicle
{
    /// <summary>
    /// Immersion effects layer — handles all non-physics VR feel:
    ///  • Engine audio pitch/volume
    ///  • Wind audio volume
    ///  • Tire squeal audio
    ///  • Dashboard ambient light flicker (speed-based)
    ///
    /// All audio is procedurally generated (sine-wave synthesis) —
    /// no external audio clips required.
    /// </summary>
    public class VRAudioManager : MonoBehaviour
    {
        [Header("References")]
        public VRCarController car;

        [Header("Audio Sources (auto-created if null)")]
        public AudioSource engineSource;
        public AudioSource windSource;
        public AudioSource tireSqueelSource;

        [Header("Engine Sound")]
        public float engineIdlePitch = 0.55f;
        public float engineMaxPitch  = 2.4f;
        [Tooltip("Volume at idle.")]
        public float engineIdleVolume = 0.35f;
        [Tooltip("Volume at full throttle.")]
        public float engineMaxVolume  = 0.75f;

        [Header("Wind Sound")]
        public float windStartSpeed = 30f;   // km/h
        public float windMaxSpeed   = 160f;
        public float windMaxVolume  = 0.5f;

        [Header("Tire Squeal")]
        [Tooltip("Lateral slip threshold before squeal starts.")]
        public float squeelSlipThreshold = 0.35f;
        public float squeelMaxVolume     = 0.45f;

        // ── Procedural clips ───────────────────────────────────────────────────
        private AudioClip _engineClip;
        private AudioClip _windClip;
        private AudioClip _squeelClip;

        private void Awake()
        {
            if (car == null) car = GetComponentInParent<VRCarController>();

            // Auto-create audio sources on this GameObject if not assigned
            engineSource    ??= CreateSource("Engine");
            windSource      ??= CreateSource("Wind");
            tireSqueelSource ??= CreateSource("TireSqueel");

            // Generate procedural clips
            _engineClip = GenerateEngineClip();
            _windClip   = GenerateWindClip();
            _squeelClip = GenerateSqueelClip();

            // Assign and start looping
            AssignAndPlay(engineSource,    _engineClip,  0.35f, 0.55f, true);
            AssignAndPlay(windSource,      _windClip,    0f,    1.0f,  true);
            AssignAndPlay(tireSqueelSource, _squeelClip, 0f,    1.0f,  true);
        }

        private void Update()
        {
            // Mute all car sounds in classroom or intro splash states
            bool muteCarSounds = false;
            if (InfiniteWorld.SpeedLessonManager.Instance != null)
            {
                var state = InfiniteWorld.SpeedLessonManager.Instance.currentState;
                if (state == InfiniteWorld.SpeedLessonManager.LessonState.Classroom || 
                    state == InfiniteWorld.SpeedLessonManager.LessonState.IntroSplash)
                {
                    muteCarSounds = true;
                }
            }

            if (muteCarSounds)
            {
                if (engineSource != null) engineSource.volume = 0f;
                if (windSource != null) windSource.volume = 0f;
                if (tireSqueelSource != null) tireSqueelSource.volume = 0f;
                return;
            }

            if (car == null) return;

            float speed    = car.SpeedKmh;
            float throttle = car.ThrottleInput;
            float rpm      = car.CurrentRPM;
            float rpmT     = Mathf.Clamp01(rpm / 7000f);

            // ── Engine ────────────────────────────────────────────────────────
            if (engineSource != null)
            {
                engineSource.pitch  = Mathf.Lerp(engineIdlePitch, engineMaxPitch, rpmT);
                engineSource.volume = Mathf.Lerp(engineIdleVolume,
                    engineMaxVolume, Mathf.Max(throttle, rpmT * 0.3f));
            }

            // ── Wind ──────────────────────────────────────────────────────────
            if (windSource != null)
            {
                float windT   = Mathf.Clamp01((speed - windStartSpeed) / (windMaxSpeed - windStartSpeed));
                windSource.volume = windT * windMaxVolume;
                windSource.pitch  = 0.8f + windT * 0.5f;
            }

            // ── Tire squeal (simplified lateral slip proxy) ────────────────
            if (tireSqueelSource != null)
            {
                float steer     = Mathf.Abs(car.SteerInput);
                float latSlip   = steer * (speed / 100f);            // proxy
                float squeelVol = Mathf.Clamp01((latSlip - squeelSlipThreshold)
                    / (1f - squeelSlipThreshold)) * squeelMaxVolume;

                tireSqueelSource.volume = Mathf.Lerp(tireSqueelSource.volume,
                    squeelVol, Time.deltaTime * 10f);
            }
        }

        // ── Source helpers ─────────────────────────────────────────────────────

        private AudioSource CreateSource(string label)
        {
            var go  = new GameObject($"Audio_{label}");
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 1f;        // Full 3D
            src.rolloffMode  = AudioRolloffMode.Logarithmic;
            src.minDistance  = 0.3f;
            src.maxDistance  = 8f;
            src.playOnAwake  = false;
            src.loop         = true;
            return src;
        }

        private static void AssignAndPlay(AudioSource src, AudioClip clip,
                                          float volume, float pitch, bool loop)
        {
            if (src == null || clip == null) return;
            src.clip   = clip;
            src.volume = volume;
            src.pitch  = pitch;
            src.loop   = loop;
            src.Play();
        }

        // ── Procedural Audio Generation ────────────────────────────────────────

        /// <summary>
        /// Generates a 2-second engine loop using multi-harmonic synthesis.
        /// Sounds like a rough idle growl — pitch is shifted at runtime.
        /// </summary>
        private static AudioClip GenerateEngineClip()
        {
            const int sampleRate = 22050;
            const int samples    = sampleRate * 2;
            float[]   data       = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;
                // Fundamental at 80 Hz + odd harmonics (diesel-like texture)
                float s = Mathf.Sin(2f * Mathf.PI * 80f  * t) * 0.50f
                        + Mathf.Sin(2f * Mathf.PI * 160f * t) * 0.28f
                        + Mathf.Sin(2f * Mathf.PI * 240f * t) * 0.14f
                        + Mathf.Sin(2f * Mathf.PI * 320f * t) * 0.08f
                        + Mathf.Sin(2f * Mathf.PI * 400f * t) * 0.04f;

                // Rough pulse (cylinder fire simulation) at 13.3 Hz (800 RPM / 60)
                float pulse = Mathf.Abs(Mathf.Sin(2f * Mathf.PI * 13.3f * t));
                data[i] = s * (0.7f + 0.3f * pulse);
            }

            var clip = AudioClip.Create("ProceduralEngine", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// Generates a 3-second wind rush loop using filtered noise.
        /// </summary>
        private static AudioClip GenerateWindClip()
        {
            const int sampleRate = 22050;
            const int samples    = sampleRate * 3;
            float[]   data       = new float[samples];

            float prev = 0f;
            System.Random rng = new System.Random(42);
            for (int i = 0; i < samples; i++)
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                // Low-pass filter for whoosh texture (coefficient ≈ 600 Hz cutoff)
                float alpha = 0.015f;
                prev        = prev + alpha * (noise - prev);
                data[i]     = prev * 3f; // boost filtered signal
            }

            var clip = AudioClip.Create("ProceduralWind", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// Generates a tire squeal tone (1.1 kHz) with amplitude modulation.
        /// </summary>
        private static AudioClip GenerateSqueelClip()
        {
            const int sampleRate = 22050;
            const int samples    = sampleRate;    // 1 second loop
            float[]   data       = new float[samples];

            System.Random rng = new System.Random(99);
            for (int i = 0; i < samples; i++)
            {
                float t     = (float)i / sampleRate;
                float tone  = Mathf.Sin(2f * Mathf.PI * 1100f * t) * 0.5f
                            + Mathf.Sin(2f * Mathf.PI * 1350f * t) * 0.2f;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.08f;
                data[i]     = tone + noise;
            }

            var clip = AudioClip.Create("ProceduralSqueel", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
