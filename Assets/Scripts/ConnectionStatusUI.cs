using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// ConnectionStatusUI - Displays backend connection status as popup/overlay
/// 
/// Supports both 2D ScreenSpace Overlay (for editor monitor) and 3D VR Floating Popups (for VR headset).
/// </summary>
public class ConnectionStatusUI : MonoBehaviour
{
    [SerializeField] private Text statusText;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private float autoHideTime = 5f;
    [SerializeField] private bool showOnConnect = true;
    [SerializeField] private bool showOnError = true;
    
    public CanvasGroup canvasGroup;
    private Coroutine hideCoroutine;
    
    // VR 3D Popup references
    private GameObject vrPopupGo;
    private TextMesh vrText;
    private MeshRenderer vrBgRenderer;
    
    private Color connectedColor = new Color(0.2f, 0.8f, 0.2f, 0.8f);      // Green
    private Color disconnectedColor = new Color(0.8f, 0.2f, 0.2f, 0.8f);    // Red
    private Color connectingColor = new Color(0.8f, 0.8f, 0.2f, 0.8f);      // Yellow
    private Color errorColor = new Color(0.9f, 0.4f, 0.1f, 0.8f);           // Orange
    
    void Start()
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }
        canvasGroup.blocksRaycasts = false;
        
        // Start hidden if text is empty or default
        if (statusText == null || string.IsNullOrEmpty(statusText.text) || statusText.text == "Initializing...")
        {
            Hide();
        }
    }

    public void Initialize(Text textComponent, Image imageComponent)
    {
        statusText = textComponent;
        backgroundImage = imageComponent;
    }

    public static ConnectionStatusUI CreateDynamic()
    {
        // 1. Create Canvas (Always create a dedicated ScreenSpaceOverlay for popups)
        GameObject canvasGo = new GameObject("DynamicConnectionStatusCanvas");
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999; // Draw on top of all other canvases (e.g. WorldSpace VR canvases)
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();
        
        DontDestroyOnLoad(canvasGo);

        // 2. Create Panel
        GameObject panelGo = new GameObject("ConnectionStatusPanel");
        panelGo.transform.SetParent(canvas.transform, false);

        var image = panelGo.AddComponent<Image>();
        image.color = new Color(0.15f, 0.15f, 0.15f, 0.85f);

        var cg = panelGo.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.alpha = 0f; // start hidden

        var rect = panelGo.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -50f); // 50 pixels down from top
        rect.sizeDelta = new Vector2(450f, 65f); // Width=450, Height=65

        // 3. Create Text
        GameObject textGo = new GameObject("StatusText");
        textGo.transform.SetParent(panelGo.transform, false);

        var text = textGo.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (text.font == null) text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 20;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;

        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10, 5);
        textRect.offsetMax = new Vector2(-10, -5);

        // 4. Add component and initialize
        var statusUI = panelGo.AddComponent<ConnectionStatusUI>();
        statusUI.Initialize(text, image);
        statusUI.canvasGroup = cg;

        return statusUI;
    }
    
    public void ShowConnecting()
    {
        Show("🔄 Connecting to Backend...", connectingColor);
    }
    
    public void ShowConnected(string message = "✅ Connected to Backend")
    {
        Show(message, connectedColor);
        if (showOnConnect)
            StartAutoHide();
    }
    
    public void ShowDisconnected(string message = "❌ Disconnected from Backend")
    {
        Show(message, disconnectedColor);
        if (showOnError)
            StartAutoHide();
    }
    
    public void ShowError(string message)
    {
        Show("⚠️ " + message, errorColor);
        if (showOnError)
            StartAutoHide();
    }
    
    public void ShowSuccess(string message)
    {
        Show("✓ " + message, connectedColor);
        StartAutoHide();
    }
    
    public void ShowWarning(string message)
    {
        Show("⚠️ " + message, connectingColor);
        StartAutoHide();
    }
    
    private void Show(string message, Color color)
    {
        // 1. Update 2D Canvas UI (if components exist)
        if (statusText != null)
        {
            statusText.text = message;
        }
        
        if (backgroundImage != null)
        {
            backgroundImage.color = color;
        }
        
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
        }
        
        // 2. Update 3D VR Floating UI
        ShowVR3D(message, color);
        
        gameObject.SetActive(true);
    }
    
    public void Hide()
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
        }
        
        HideVR3D();
    }
    
    private void ShowVR3D(string message, Color color)
    {
        if (vrPopupGo == null)
        {
            vrPopupGo = new GameObject("VR_ConnectionStatusPopup");
            
            // Create background Quad
            GameObject bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(bg.GetComponent<Collider>()); // No collision physics
            bg.transform.SetParent(vrPopupGo.transform, false);
            bg.transform.localPosition = new Vector3(0f, 0f, 0.01f); // slightly behind text
            bg.transform.localScale = new Vector3(0.9f, 0.16f, 1f);
            
            vrBgRenderer = bg.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Unlit/Color"));
            vrBgRenderer.sharedMaterial = mat;
            
            // Create Text
            GameObject textGo = new GameObject("Text");
            textGo.transform.SetParent(vrPopupGo.transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, 0f);
            textGo.transform.localScale = Vector3.one * 0.008f;
            
            vrText = textGo.AddComponent<TextMesh>();
            Font builtinFont = GetSafeBuiltinFont();
            if (builtinFont != null)
            {
                vrText.font = builtinFont;
                var txtMat = new Material(Shader.Find("GUI/Text Shader") ?? Shader.Find("UI/Default"));
                txtMat.mainTexture = builtinFont.material.mainTexture;
                textGo.GetComponent<MeshRenderer>().sharedMaterial = txtMat;
            }
            vrText.fontSize = 24;
            vrText.fontStyle = FontStyle.Bold;
            vrText.anchor = TextAnchor.MiddleCenter;
            vrText.alignment = TextAlignment.Center;
        }
        
        var cam = Camera.main;
        if (cam != null && vrPopupGo.transform.parent != cam.transform)
        {
            vrPopupGo.transform.SetParent(cam.transform, false);
            vrPopupGo.transform.localPosition = new Vector3(0f, 0.22f, 0.85f); // Bring closer: 0.85m instead of 1.2m
            vrPopupGo.transform.localRotation = Quaternion.identity;
            vrPopupGo.transform.localScale = Vector3.one * 0.7f;
        }
        else if (vrPopupGo.transform.parent == null)
        {
            vrPopupGo.transform.position = new Vector3(0f, 0.22f, 0.85f);
            vrPopupGo.transform.rotation = Quaternion.identity;
            vrPopupGo.transform.localScale = Vector3.one * 0.7f;
        }
        
        if (vrText != null) vrText.text = message;
        if (vrBgRenderer != null) vrBgRenderer.sharedMaterial.color = color;
        
        vrPopupGo.SetActive(true);
    }

    private void HideVR3D()
    {
        if (vrPopupGo != null)
        {
            vrPopupGo.SetActive(false);
        }
    }

    private Font GetSafeBuiltinFont()
    {
        Font f = null;
        try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (f == null) { try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
        return f;
    }

    private void StartAutoHide()
    {
        if (hideCoroutine != null)
            StopCoroutine(hideCoroutine);
        
        hideCoroutine = StartCoroutine(AutoHideCoroutine());
    }
    
    private IEnumerator AutoHideCoroutine()
    {
        yield return new WaitForSeconds(autoHideTime);
        Hide();
    }

    private void OnDestroy()
    {
        if (vrPopupGo != null)
        {
            Destroy(vrPopupGo);
        }
    }
}
