using System;
using System.Collections.Generic;
using System.Text;
using GorillaLocomotion;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace ThatUtilsPad;

internal sealed class VrDiagnosticsMonitor : MonoBehaviour
{
    private static VrDiagnosticsMonitor? _instance;

    private static readonly Vector3 CanvasFollowOffset = new Vector3(0f, 0f, 1.6f);
    private static readonly Vector3 InfoBaseLocal = new Vector3(-1f, -1f, 0.5f);
    private static readonly Vector3 TextLocalScale = new Vector3(0.00333333333f, 0.00333333333f, 0.33333333f);
    private static readonly Vector2 TextSize = new Vector2(450f, 420f);
    private const int OverlayFontSize = 30;
    private const float MoveSpeed = 0.55f;
    private const float StickDeadzone = 0.22f;
    private const float LineFlickCooldown = 0.22f;

    private readonly StringBuilder _sb = new StringBuilder(256);
    private readonly List<string> _lines = new List<string>(8);
    private readonly Dictionary<string, string> _info = new Dictionary<string, string>(StringComparer.Ordinal);

    private GameObject? _canvas;
    private TextMeshProUGUI? _informationText;
    private Material? _textMaterial;
    private Camera? _camera;
    private float _fpsAccum;
    private int _fpsFrameCount;
    private float _fps;
    private float _fpsAverage = 90f;
    private float _lastSampleTime;
    private float _lastNetSampleTime;
    private long _lastIncomingBytes;
    private long _lastOutgoingBytes;
    private float _incomingKbps;
    private float _outgoingKbps;
    private bool _wasMoveHeld;
    private bool _dirtyPosition;
    private bool _moveHeld;
    private int _selectedLine;
    private float _nextLineFlickTime;
    private float _lastStickY;

    public static VrDiagnosticsMonitor Ensure()
    {
        if (_instance != null)
            return _instance;

        var root = new GameObject("TUP_VRDiagnosticsMonitor");
        DontDestroyOnLoad(root);
        _instance = root.AddComponent<VrDiagnosticsMonitor>();
        return _instance;
    }

    public static void SyncFromSettings()
    {
        if (!Mods.IsVrDiagnosticsHudEnabled())
        {
            if (_instance != null)
                _instance.ApplyVisibility();
            return;
        }

        if (_instance == null)
            Ensure();
        _instance?.ApplyVisibility();
    }

    public static void ResetHudPosition()
    {
        Mods.SetVrHudLocalPosition(Vector3.zero);
        if (_instance != null && _instance._informationText != null)
            _instance._informationText.rectTransform.localPosition = InfoBaseLocal;
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        _lastSampleTime = Time.realtimeSinceStartup;
        _lastNetSampleTime = _lastSampleTime;
        DestroyLegacyWorldHud();
        ForceDisableNativeDebugHud();
        ApplyVisibility();
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;

        if (_textMaterial != null)
            Destroy(_textMaterial);
        if (_canvas != null)
            Destroy(_canvas);

        ForceDisableNativeDebugHud();
    }

    private void LateUpdate()
    {
        if (!Mods.IsVrDiagnosticsHudEnabled())
            return;

        if (_canvas == null || _informationText == null)
            EnsureOverlay();

        if (_canvas == null || _informationText == null)
            return;

        FollowCamera();
        HandleMoveInput();
        HandleLineSelect();
        ApplyInfoCornerPose();
    }

    private void Update()
    {
        float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
        float instant = 1f / dt;
        if (Mods.IsVrFpsAverageEnabled())
            _fpsAverage = (_fpsAverage * 99f + instant) / 100f;
        else
        {
            _fpsAccum += dt;
            _fpsFrameCount++;
            if (_fpsAccum >= 0.5f)
            {
                _fps = _fpsFrameCount / _fpsAccum;
                _fpsAccum = 0f;
                _fpsFrameCount = 0;
            }
        }

        UpdateNetRates();

        if (!Mods.IsVrDiagnosticsHudEnabled())
        {
            SetOverlayActive(false);
            return;
        }

        if (_canvas == null || _informationText == null)
            EnsureOverlay();
        SetOverlayActive(true);

        float refresh = Mods.IsVrFpsSlowEnabled() ? 1f : 0.25f;
        if (Time.realtimeSinceStartup - _lastSampleTime >= refresh || _moveHeld != _wasMoveHeld)
        {
            _lastSampleTime = Time.realtimeSinceStartup;
            if (Mods.IsVrFpsAverageEnabled())
                _fps = Mathf.Ceil(_fpsAverage);
            RenderOverlay();
        }
    }

    private void ApplyVisibility()
    {
        ForceDisableNativeDebugHud();
        DestroyLegacyWorldHud();

        bool want = Mods.IsVrDiagnosticsHudEnabled();
        if (want)
        {
            EnsureOverlay();
            SetOverlayActive(true);
            FollowCamera();
            ApplyInfoCornerPose();
            RenderOverlay();
        }
        else
        {
            SetOverlayActive(false);
        }
    }

    private void SetOverlayActive(bool active)
    {
        if (_canvas != null && _canvas.activeSelf != active)
            _canvas.SetActive(active);
        if (_informationText != null && _informationText.gameObject.activeSelf != active)
            _informationText.gameObject.SetActive(active);
    }

    private void DestroyLegacyWorldHud()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child == null)
                continue;
            string n = child.name;
            if (n.StartsWith("Diagnostics", StringComparison.Ordinal) ||
                n.IndexOf("Panel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Border", StringComparison.OrdinalIgnoreCase) >= 0)
                Destroy(child.gameObject);
        }
    }

    private void EnsureOverlay()
    {
        Camera? cam = ResolveVrCamera();
        if (cam == null)
            return;

        _camera = cam;

        if (_canvas == null)
        {
            _canvas = new GameObject("TUP_DiagnosticsCanvas");
            DontDestroyOnLoad(_canvas);

            Canvas canvas = _canvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = cam;
            canvas.enabled = true;
            _canvas.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            _canvas.AddComponent<GraphicRaycaster>();

            RectTransform canvasRect = _canvas.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(5f, 5f);
            canvasRect.localScale = Vector3.one;
        }

        if (_textMaterial == null)
        {
            Shader? shader = ShaderCache.TextShader != null
                ? ShaderCache.TextShader
                : Shader.Find("GUI/Text Shader");
            if (shader != null)
                _textMaterial = new Material(shader);
        }

        if (_informationText == null)
        {
            var textObj = new GameObject("TUP_DiagnosticsInfo");
            textObj.transform.SetParent(_canvas.transform, false);
            _informationText = textObj.AddComponent<TextMeshProUGUI>();
            _informationText.text = string.Empty;
            _informationText.fontSize = OverlayFontSize;
            _informationText.alignment = TextAlignmentOptions.TopRight;
            _informationText.overflowMode = TextOverflowModes.Overflow;
            _informationText.richText = true;
            _informationText.color = Color.white;
            _informationText.characterSpacing = -9f;
            _informationText.rectTransform.sizeDelta = TextSize;
            _informationText.rectTransform.localScale = TextLocalScale;
            _informationText.rectTransform.localPosition = InfoBaseLocal;

            if (FontCache.figtreeBundle != null)
            {
                TMP_FontAsset? font = FontCache.figtreeBundle.LoadAsset<TMP_FontAsset>("assets/fonts/figtree.asset");
                if (font != null)
                    _informationText.font = font;
            }

            if (_textMaterial != null)
                _informationText.material = _textMaterial;
        }
    }

    private void FollowCamera()
    {
        Camera? cam = ResolveVrCamera();
        if (cam == null || _canvas == null)
            return;

        _camera = cam;
        Canvas canvas = _canvas.GetComponent<Canvas>();
        if (canvas != null)
            canvas.worldCamera = cam;

        float scale = 1f;
        try
        {
            if (GTPlayer.Instance != null)
                scale = Mathf.Max(0.1f, GTPlayer.Instance.scale);
        }
        catch
        {
        }

        _canvas.transform.position = cam.transform.TransformPoint(CanvasFollowOffset);
        _canvas.transform.rotation = cam.transform.rotation * Quaternion.Euler(0f, 90f, 0f);
        _canvas.transform.localScale = Vector3.one * scale;
    }

    private void ApplyInfoCornerPose()
    {
        if (_informationText == null)
            return;

        Vector3 offset = Mods.GetVrHudLocalPosition(Vector3.zero);
        offset.x = Mathf.Clamp(offset.x, -0.8f, 0.8f);
        offset.y = Mathf.Clamp(offset.y, -0.8f, 0.8f);
        offset.z = Mathf.Clamp(offset.z, -0.8f, 0.8f);

        _informationText.rectTransform.localPosition = InfoBaseLocal + offset;
        _informationText.rectTransform.localScale = TextLocalScale;
        _informationText.alignment = TextAlignmentOptions.TopRight;
        _informationText.color = _moveHeld ? new Color(1f, 0.92f, 0.45f, 1f) : Color.white;
    }

    private void HandleMoveInput()
    {
        string bind = Mods.GetHudMoveBindCode();
        bool held = Main.IsMenuOpenBindPressed(bind);
        _moveHeld = held;

        if (held)
        {
            bool leftHand = IsLeftHandBind(bind);
            if (Main.TryGetControllerJoystick(leftHand, out Vector2 stick) && stick.magnitude > StickDeadzone)
            {
                Vector3 pos = Mods.GetVrHudLocalPosition(Vector3.zero);
                float speed = MoveSpeed * Time.unscaledDeltaTime;
                pos.z += stick.x * speed;
                pos.y += stick.y * speed;
                pos.x = Mathf.Clamp(pos.x, -0.8f, 0.8f);
                pos.y = Mathf.Clamp(pos.y, -0.8f, 0.8f);
                pos.z = Mathf.Clamp(pos.z, -0.8f, 0.8f);
                Mods.SetVrHudLocalPosition(pos, save: false);
                _dirtyPosition = true;
                ApplyInfoCornerPose();
            }
        }
        else if (_wasMoveHeld && _dirtyPosition)
        {
            Mods.PersistVrHudLocalPosition();
            _dirtyPosition = false;
        }

        if (_wasMoveHeld != held)
            RenderOverlay();

        _wasMoveHeld = held;
    }

    private void HandleLineSelect()
    {
        if (_moveHeld || _lines.Count == 0)
            return;

        string bind = Mods.GetHudMoveBindCode();
        bool leftHand = IsLeftHandBind(bind);
        if (!Main.TryGetControllerJoystick(leftHand, out Vector2 stick))
            return;

        if (Time.unscaledTime < _nextLineFlickTime)
        {
            _lastStickY = stick.y;
            return;
        }

        if (stick.y > 0.55f && _lastStickY <= 0.55f)
        {
            _selectedLine = (_selectedLine - 1 + _lines.Count) % _lines.Count;
            _nextLineFlickTime = Time.unscaledTime + LineFlickCooldown;
            RenderOverlay();
            PulseHaptic(leftHand);
        }
        else if (stick.y < -0.55f && _lastStickY >= -0.55f)
        {
            _selectedLine = (_selectedLine + 1) % _lines.Count;
            _nextLineFlickTime = Time.unscaledTime + LineFlickCooldown;
            RenderOverlay();
            PulseHaptic(leftHand);
        }

        _lastStickY = stick.y;
    }

    private static void PulseHaptic(bool leftHand)
    {
        if (!Mods.IsControllerHapticsEnabled())
            return;

        try
        {
            var devices = new List<UnityEngine.XR.InputDevice>();
            InputDevices.GetDevicesAtXRNode(leftHand ? XRNode.LeftHand : XRNode.RightHand, devices);
            for (int i = 0; i < devices.Count; i++)
                devices[i].SendHapticImpulse(0u, 0.25f, 0.04f);
        }
        catch
        {
        }
    }

    private static bool IsLeftHandBind(string bindCode)
    {
        if (string.IsNullOrWhiteSpace(bindCode))
            return false;
        if (bindCode.StartsWith("Left:", StringComparison.OrdinalIgnoreCase))
            return true;
        if (bindCode.StartsWith("Right:", StringComparison.OrdinalIgnoreCase))
            return false;
        string lower = bindCode.ToLowerInvariant();
        return lower.Contains("lefthand") || lower.Contains("/left");
    }

    private static Camera? ResolveVrCamera()
    {
        if (Main.FirstPersonCamera != null)
            return Main.FirstPersonCamera;

        try
        {
            if (GTPlayer.Instance != null && GTPlayer.Instance.mainCamera != null)
                return GTPlayer.Instance.mainCamera;
        }
        catch
        {
        }

        return Camera.main;
    }

    private static void ForceDisableNativeDebugHud()
    {
        try
        {
            Camera? cam = ResolveVrCamera();
            if (cam == null)
                return;

            Transform? canvas = cam.transform.Find("DebugCanvas");
            if (canvas == null)
            {
                for (int i = 0; i < cam.transform.childCount; i++)
                {
                    Transform child = cam.transform.GetChild(i);
                    if (child.name == "DebugCanvas")
                    {
                        canvas = child;
                        break;
                    }
                }
            }

            if (canvas != null)
            {
                if (canvas.gameObject.activeSelf)
                    canvas.gameObject.SetActive(false);

                foreach (var c in canvas.GetComponentsInChildren<Behaviour>(true))
                {
                    if (c != null && c.GetType().Name == "DebugHudStats" && c.enabled)
                        c.enabled = false;
                }
            }
        }
        catch
        {
        }
    }

    private void UpdateNetRates()
    {
        float now = Time.realtimeSinceStartup;
        if (now - _lastNetSampleTime < 0.5f)
            return;

        float elapsed = Mathf.Max(0.001f, now - _lastNetSampleTime);
        _lastNetSampleTime = now;

        if (!PhotonNetwork.IsConnected)
        {
            _incomingKbps = 0f;
            _outgoingKbps = 0f;
            _lastIncomingBytes = 0;
            _lastOutgoingBytes = 0;
            return;
        }

        long incoming = PhotonNetwork.NetworkingClient?.LoadBalancingPeer?.BytesIn ?? 0L;
        long outgoing = PhotonNetwork.NetworkingClient?.LoadBalancingPeer?.BytesOut ?? 0L;

        if (_lastIncomingBytes > 0)
            _incomingKbps = (incoming - _lastIncomingBytes) / elapsed / 1024f;
        if (_lastOutgoingBytes > 0)
            _outgoingKbps = (outgoing - _lastOutgoingBytes) / elapsed / 1024f;

        _lastIncomingBytes = incoming;
        _lastOutgoingBytes = outgoing;
    }

    private void RenderOverlay()
    {
        if (_informationText == null)
            return;

        _info.Clear();
        _lines.Clear();

        if (Mods.IsVrFpsCounterEnabled())
        {
            if (Mods.IsVrFrametimeEnabled())
            {
                float ft = _fps > 0.01f ? 1000f / _fps : 0f;
                _info["FT"] = ft.ToString("0.0") + "ms";
            }
            else
            {
                _info["FPS"] = _fps.ToString("0");
            }
        }

        if (Mods.IsVrPingDisplayEnabled())
        {
            int ping = PhotonNetwork.IsConnected ? PhotonNetwork.GetPing() : -1;
            _info["Ping"] = ping >= 0 ? ping + "ms" : "-";
        }

        if (Mods.IsVrNetworkStatsEnabled())
        {
            _info["Net"] = "\u2193" + _incomingKbps.ToString("0.0") + " \u2191" + _outgoingKbps.ToString("0.0");
            try
            {
                _info["Res"] = XRSettings.eyeTextureResolutionScale.ToString("0.00") + "x";
            }
            catch
            {
                _info["Res"] = "n/a";
            }
        }

        if (_moveHeld)
            _info["HUD"] = "MOVING";

        string labelColor = "7DFFB0";
        string valueColor = "FFFFFF";
        string selectedColor = "FFE66A";

        foreach (KeyValuePair<string, string> pair in _info)
            _lines.Add(pair.Key + " " + pair.Value);

        if (_selectedLine >= _lines.Count)
            _selectedLine = 0;

        _sb.Clear();
        int i = 0;
        foreach (KeyValuePair<string, string> pair in _info)
        {
            bool selected = i == _selectedLine;
            string keyCol = selected ? selectedColor : labelColor;
            string valCol = selected ? selectedColor : valueColor;
            string prefix = selected ? "> " : "";
            _sb.Append(prefix)
              .Append("<color=#").Append(keyCol).Append(">").Append(pair.Key).Append("</color> ")
              .Append("<color=#").Append(valCol).Append(">").Append(pair.Value).Append("</color>");
            if (i < _info.Count - 1)
                _sb.Append('\n');
            i++;
        }

        _informationText.text = _sb.ToString();
    }
}
