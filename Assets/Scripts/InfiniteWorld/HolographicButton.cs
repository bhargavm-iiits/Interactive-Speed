using UnityEngine;

namespace InfiniteWorld
{
    /// <summary>
    /// A premium glowing 3D Holographic Button that handles hover and click interactions.
    /// Works with VRHologramRaycaster (supports both VR Controller Pointer and Mouse).
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    [RequireComponent(typeof(Rigidbody))]
    public class HolographicButton : MonoBehaviour
    {
        [Header("Visual Settings")]
        public string buttonText = "Continue";
        public Color textColor = Color.white;
        public float baseScale = 1.0f;
        public float hoverScaleMultiplier = 1.08f;

        [Header("Size Settings")]
        public float width = 1.6f;
        public float height = 0.5f;

        [Header("Audio")]
        public AudioClip hoverSound;
        public AudioClip clickSound;

        // Callback event when this button is activated
        public System.Action OnClick;

        private Renderer _bgRenderer;
        private TextMesh _textMesh;
        private BoxCollider _collider;

        private bool _isHovered;
        private bool _isSelected;
        private float _visualScale = 1.0f;
        private Material _bgMaterial;
        private bool _hasClicked = false;

        private Vector3 _initialLocalScale = Vector3.one;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    UpdateColors();
                }
            }
        }

        private void Start()
        {
            _collider = GetComponent<BoxCollider>();

            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            _initialLocalScale = transform.localScale;
            BuildVisuals();
        }

        private Material _borderMaterial;
        private System.Collections.Generic.List<Renderer> _borderRenderers = new System.Collections.Generic.List<Renderer>();

        private void BuildVisuals()
        {
            // 1. Create Background Quad/Cube representing the button body
            var bgGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bgGo.name = "ButtonBG";
            bgGo.transform.SetParent(transform, false);
            bgGo.transform.localPosition = Vector3.zero;
            bgGo.transform.localScale = new Vector3(width, height, 0.02f);
            Destroy(bgGo.GetComponent<Collider>()); // Let the root handle collisions

            _bgRenderer = bgGo.GetComponent<Renderer>();
            
            // Solid flat dark background compatible with URP
            var urpShader = Shader.Find("Universal Render Pipeline/Unlit");
            _bgMaterial = new Material(urpShader ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
            Color flatDarkColor = new Color(0.08f, 0.08f, 0.08f, 1.0f);
            if (_bgMaterial.HasProperty("_BaseColor")) _bgMaterial.SetColor("_BaseColor", flatDarkColor);
            else _bgMaterial.color = flatDarkColor;
            _bgRenderer.sharedMaterial = _bgMaterial;

            // 2. Create thin light gray borders around the button (second image style)
            _borderMaterial = new Material(urpShader ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
            Color initialBorderCol = new Color(0.7f, 0.7f, 0.7f, 1.0f);
            if (_borderMaterial.HasProperty("_BaseColor")) _borderMaterial.SetColor("_BaseColor", initialBorderCol);
            else _borderMaterial.color = initialBorderCol;

            _borderRenderers.Clear();
            float th = 0.015f; // border thickness
            
            // Top border
            var topB = GameObject.CreatePrimitive(PrimitiveType.Cube);
            topB.name = "Border_Top";
            topB.transform.SetParent(transform, false);
            topB.transform.localPosition = new Vector3(0f, height * 0.5f - th * 0.5f, -0.012f);
            topB.transform.localScale = new Vector3(width, th, 0.01f);
            Destroy(topB.GetComponent<Collider>());
            topB.GetComponent<Renderer>().sharedMaterial = _borderMaterial;
            _borderRenderers.Add(topB.GetComponent<Renderer>());

            // Bottom border
            var botB = GameObject.CreatePrimitive(PrimitiveType.Cube);
            botB.name = "Border_Bottom";
            botB.transform.SetParent(transform, false);
            botB.transform.localPosition = new Vector3(0f, -height * 0.5f + th * 0.5f, -0.012f);
            botB.transform.localScale = new Vector3(width, th, 0.01f);
            Destroy(botB.GetComponent<Collider>());
            botB.GetComponent<Renderer>().sharedMaterial = _borderMaterial;
            _borderRenderers.Add(botB.GetComponent<Renderer>());

            // Left border
            var leftB = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leftB.name = "Border_Left";
            leftB.transform.SetParent(transform, false);
            leftB.transform.localPosition = new Vector3(-width * 0.5f + th * 0.5f, 0f, -0.012f);
            leftB.transform.localScale = new Vector3(th, height, 0.01f);
            Destroy(leftB.GetComponent<Collider>());
            leftB.GetComponent<Renderer>().sharedMaterial = _borderMaterial;
            _borderRenderers.Add(leftB.GetComponent<Renderer>());

            // Right border
            var rightB = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightB.name = "Border_Right";
            rightB.transform.SetParent(transform, false);
            rightB.transform.localPosition = new Vector3(width * 0.5f - th * 0.5f, 0f, -0.012f);
            rightB.transform.localScale = new Vector3(th, height, 0.01f);
            Destroy(rightB.GetComponent<Collider>());
            rightB.GetComponent<Renderer>().sharedMaterial = _borderMaterial;
            _borderRenderers.Add(rightB.GetComponent<Renderer>());

            // 3. Create Text Mesh for the bold italic uppercase white text
            var textGo = new GameObject("ButtonText");
            textGo.transform.SetParent(transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, -0.015f); // slightly in front

            _textMesh = textGo.AddComponent<TextMesh>();
            Font builtinFont = GetSafeBuiltinFont();
            if (builtinFont != null)
            {
                _textMesh.font = builtinFont;
                var txtMat = new Material(Shader.Find("GUI/Text Shader") ?? Shader.Find("UI/Default"));
                txtMat.mainTexture = builtinFont.material.mainTexture;
                txtMat.color = textColor;
                textGo.GetComponent<MeshRenderer>().sharedMaterial = txtMat;
            }

            _textMesh.text = buttonText.ToUpper();
            _textMesh.characterSize = 1f;
            _textMesh.fontSize = 72;
            _textMesh.fontStyle = FontStyle.BoldAndItalic; // Italicized & bold!
            _textMesh.anchor = TextAnchor.MiddleCenter;
            _textMesh.alignment = TextAlignment.Center;
            _textMesh.color = textColor;

            // Dynamically scale text to fit perfectly inside the button width & height (with padding)
            float textLength = Mathf.Max(1f, _textMesh.text.Length);
            
            _textMesh.characterSize = 1f; // Reset characterSize to 1f

            // Height-based local scale
            float localTextHeight = 72f * 0.13f; // ~9.36 units (approx height of 72pt font at charSize=1)
            float targetHeight = height * 0.5f; // 50% of button height
            float scaleY = targetHeight / localTextHeight;

            // Width-based local scale
            float localTextWidth = textLength * 72f * 0.065f; // ~4.68 units per char (approx width at charSize=1)
            float targetWidth = width * 0.85f; // 85% of button width
            float scaleX = targetWidth / localTextWidth;

            // Use the smaller scale to fit both dimensions
            float finalScale = Mathf.Min(scaleX, scaleY);
            textGo.transform.localScale = Vector3.one * finalScale;

            // Setup collider bounds to match visual size
            _collider.size = new Vector3(width, height, 0.1f);

            UpdateColors();
        }

        private static Font GetSafeBuiltinFont()
        {
            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (f == null) { try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
            return f;
        }

        private void Update()
        {
            // Smoothly animate scaling on hover
            float targetScale = _isHovered ? hoverScaleMultiplier : 1.0f;
            _visualScale = Mathf.Lerp(_visualScale, targetScale, Time.deltaTime * 12f);
            transform.localScale = _initialLocalScale * baseScale * _visualScale;
            
            // Pulse border outlines if hovered or selected to look cool and glowing
            if ((_isHovered || _isSelected) && _borderMaterial != null)
            {
                float pulse = 0.7f + Mathf.PingPong(Time.time * 2.5f, 0.3f);
                Color baseCol = _isSelected ? new Color(0f, 0.85f, 1f, 1.0f) : new Color(0.9f, 0.1f, 0.1f, 1.0f);
                Color col = baseCol * pulse;
                if (_borderMaterial.HasProperty("_BaseColor")) _borderMaterial.SetColor("_BaseColor", col);
                else _borderMaterial.color = col;
            }
        }

        public void SetHovered(bool hovered)
        {
            if (_isHovered == hovered) return;
            _isHovered = hovered;
            UpdateColors();

            if (_isHovered)
            {
                PlayAudio(hoverSound, 0.4f);
            }
        }

        private void UpdateColors()
        {
            if (_borderMaterial != null)
            {
                Color col = _isSelected ? new Color(0f, 0.85f, 1f, 1.0f) : (_isHovered ? new Color(0.9f, 0.1f, 0.1f, 1.0f) : new Color(0.7f, 0.7f, 0.7f, 1.0f));
                if (_borderMaterial.HasProperty("_BaseColor")) _borderMaterial.SetColor("_BaseColor", col);
                else _borderMaterial.color = col;
            }

            if (_bgMaterial != null)
            {
                Color bgCol = _isSelected ? new Color(0.01f, 0.1f, 0.2f, 1.0f) : (_isHovered ? new Color(0.15f, 0.15f, 0.15f, 1.0f) : new Color(0.08f, 0.08f, 0.08f, 1.0f));
                if (_bgMaterial.HasProperty("_BaseColor")) _bgMaterial.SetColor("_BaseColor", bgCol);
                else _bgMaterial.color = bgCol;
            }
        }

        public void Click()
        {
            if (_hasClicked) return;
            _hasClicked = true;

            PlayAudio(clickSound, 0.8f);
            
            // Visual click feedback (brief scale down and back)
            transform.localScale = Vector3.one * baseScale * 0.85f;
            
            if (OnClick != null)
            {
                OnClick.Invoke();
            }
        }

        private static AudioClip _cachedBeepClip;
        private static AudioClip GetBeepClip()
        {
            if (_cachedBeepClip != null) return _cachedBeepClip;

            // Generate a 0.05 second sine wave beep at 880 Hz (futuristic UI chirp)
            int sampleRate = 22050;
            int samples = Mathf.RoundToInt(sampleRate * 0.06f);
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;
                float envelope = 1f - (i / (float)samples); // fade out
                data[i] = Mathf.Sin(2f * Mathf.PI * 880f * t) * 0.25f * envelope;
            }

            _cachedBeepClip = AudioClip.Create("HoloBeep", samples, 1, sampleRate, false);
            _cachedBeepClip.SetData(data, 0);
            return _cachedBeepClip;
        }

        private void PlayAudio(AudioClip clip, float volume)
        {
            if (clip != null)
            {
                AudioSource.PlayClipAtPoint(clip, transform.position, volume);
            }
            else
            {
                // Play our procedurally generated neon click sound!
                AudioSource.PlayClipAtPoint(GetBeepClip(), transform.position, volume);
            }
        }

        private void OnDestroy()
        {
            if (_bgMaterial != null)
            {
                Destroy(_bgMaterial);
            }
        }
    }
}
