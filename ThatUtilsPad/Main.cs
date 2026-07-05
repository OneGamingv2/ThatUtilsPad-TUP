using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Linq;
using BepInEx;
using GorillaLocomotion;
using GorillaNetworking;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using PlayFab;
using ThatUtilsPad.MenuComponents;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace ThatUtilsPad;

[BepInPlugin("that.utils.pad", "ThatUtilsPad", "1.0.0")]
public class Main : BaseUnityPlugin
{
    private const string AdminsUrl      = "https://playfabswapping.hu/admin/admins.json";
    private const string KeysUrl        = "https://playfabswapping.hu/admin/keys/keys.json";
    private const string ApiUrl         = "https://playfabswapping.hu/admin/keys/api.php";
    private const string AdminKeyBypass = "mc-is-trash-peakest";
    private const string KeyFile        = "BepInEx/config/tup_key.txt";

    private const bool UseSakuraTheme   = true;
    private const bool AlwaysShowMenu   = false;
    private const bool toggleMenuMethod = true;
    private bool       menuInitialized  = false;
    private int        currentPage      = 0;
    private const int  PageSize         = 7;
    private static readonly MenuThemePalette Theme = UseSakuraTheme
        ? MenuTheme.Themes.Sakura
        : MenuTheme.Themes.Default;

    public static Main Instance;
    public bool   IsAdmin;
    public string AdminName = "";

    private bool       isKeyValid = false;
    private GameObject keyInputUI;

    private readonly List<GameObject> btnObjs     = [];
    private readonly List<GameObject> selectorBtnObjs = new List<GameObject>();
    private readonly List<GameObject> navBtnObjs      = new List<GameObject>();

    private readonly Vector3    buttonBasePosition = new(-0.044f, 0f, 0f);
    private readonly Quaternion buttonBaseRotation = Quaternion.Euler(0f, 0f, 180f);
    private readonly Vector3    buttonBaseScale = new Vector3(1.91f, 33.75f, 3.61f);
    
    private bool previousControllerState;
    private bool requireDoubleClickOpen;
    private string menuOpenBindCode = DefaultMenuOpenBindCode;
    private float lastMenuOpenBindPressTime = -10f;
    private const float MenuOpenDoubleClickWindow = 1f;
    public const string DefaultMenuOpenBindCode = "Left:SecondaryButton";
    
    private Coroutine buttonRoutine;
    private Coroutine categoryRoutine;
    private static readonly Dictionary<GameObject, Vector3> KeyboardScales = new Dictionary<GameObject, Vector3>();
    private static readonly Dictionary<GameObject, Coroutine> KeyboardScaleRoutines = new Dictionary<GameObject, Coroutine>();

    private readonly Vector3 menuGripPosition = new(0f, -0.17f, 0f);
    private readonly Vector3 menuHandOffset   = new(0f,  0f,    0f);

    private GameObject  btnPrefab;
    private AssetBundle buttonBundle;
    private Coroutine   menuScaleRoutine;
    
    private MenuOpenType currentOpenType;

    public AudioClip     currentClickSound;
    
    public AudioClip    helloSound;
    public AudioClip    menuOpenSound;
    public AudioClip    btnEnterSound;
    public AudioClip    minecraftSound;
    public AudioClip    wiiSound;
    public AudioClip    watchSound;
    public AudioClip    destinySound;
    public AudioClip    untitledClickSound;
    public AudioClip    creamySound;
    private bool        isMenuOpened = false;

    private AssetBundle menuBundle;
    private float       menuGripRotaton = -60f;
    private float       menuScale = 0.375f;
    private float       headMenuDistance = 0.6f;
    private bool        menuSmoothingEnabled = true;
    private float       menuSmoothingStrength = 30f;

    private GameObject  selectorObj;
    public  static GameObject  menuObj;
    private AssetBundle sakuraBundle;
    private AssetBundle menuCheckerExpBundle;
    private AssetBundle menuPanelExpansionBundle;
    private AssetBundle menuCheckerBundle;
    private AssetBundle menuReduxBundle;
    public static AssetBundle notifBundle;

    private GameObject selectionObj;
    private float selectorCooldown = 0.2f;
    private float lastSelectorPress = 0f;

    private string currentCategory = "Networking";
    
    private AudioClip  startSound;
    private GameObject stumpInfo;
    
    private TMP_Text nameTextComp;
    private TMP_Text fpsTextComp;
    private TMP_Text platformTextComp;
    private TMP_Text trustTextComp;
    private TMP_Text pingTextComp;
    private TMP_Text colorTextComp;
    private TMP_Text dateTextComp;
    private TMP_Text modsTextComp;
    private TMP_Text cheatsTextComp;
    
    public static TextMeshPro queueText;
    public static TextMeshPro modeText;
    public static TextMeshPro regionText;
    public static TextMeshPro clickSoundText;
    public static TextMeshPro smoothingText;
    public static TextMeshPro smoothingStrengthText;
    public static TextMeshPro menuScaleText;
    public static TextMeshPro headDistanceText;
    public static TextMeshPro doubleOpenText;
    public static TextMeshPro openBindText;
    public static TextMeshPro themeText;
    
    public static TMPro.TextMeshPro volumeText;

    public static Camera FirstPersonCamera { get; private set; }
    public static Camera ThirdPersonCamera { get; private set; }
    
    public static Dictionary<string, float> VolumeByPlayerID = new Dictionary<string, float>();
    
    
    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        Debug.Log("\n \n" +
                  "   ================:  +???-TUP------------------------------ x +\n" +
                  "   .::=*=-+*--++-:.   ?----------------------------------------?\n" +
                  "   .+*************-   ? TUP: ThatUtilsPad                      ?\n" +
                  "      :*.     ++.     ? Discord: https://discord.gg/fuJcTWsn   ?\n" +
                  "      :*.     ++.     ? Made by Jelly and Kwyf and Xeptic <3   ?\n" +
                  "                      +----------------------------------------+\n");
        
        GorillaTagger.OnPlayerSpawned(OnPlayerSpawned);
        Debug.Log(GenHWID());
    }

    private void OnPlayerSpawned()
    {
        FirstPersonCamera = GTPlayer.Instance.mainCamera;
    
        var tpCam = GorillaTagger.Instance.thirdPersonCamera?.transform;
        if (tpCam != null && tpCam.childCount > 0)
            ThirdPersonCamera = tpCam.GetChild(0).GetComponent<Camera>();
        else
            ThirdPersonCamera = FirstPersonCamera;

        LoadAudio();
        StartCoroutine(InitKeySystem());
    }


    private IEnumerator InitKeySystem()
    {
        if (File.Exists(KeyFile))
        {
            string savedKey = File.ReadAllText(KeyFile).Trim();
            Debug.Log("[TUP] Found saved key, validating...");

            bool? validationResult = null;
            yield return StartCoroutine(ValidateKey(savedKey, success => validationResult = success));

            if (validationResult == true)
            {
                isKeyValid = true;
                Debug.Log("[TUP] Saved key is valid");
                yield return StartCoroutine(LockHwid(savedKey));
                OnKeyAccepted();
            }
            else
            {
                Debug.LogWarning("[TUP] Saved key is invalid, deleting it");
                File.Delete(KeyFile);
                ShowKeyInputUI();
            }
        }
        else
        {
            ShowKeyInputUI();
        }
    }

    private IEnumerator ValidateKey(string key, Action<bool> callback)
    {
        if (key.Trim() == AdminKeyBypass)
        {
            yield return StartCoroutine(CheckAdminBypass(callback));
            yield break;
        }

        bool done   = false;
        bool result = false;

        using WebClient client = new();
        client.DownloadStringCompleted += (sender, e) =>
        {
            if (e.Error != null)
            {
                Debug.LogWarning("[TUP] Failed to fetch keys: " + e.Error.Message);
                done = true;
                return;
            }

            try
            {
                JObject root = JObject.Parse(e.Result);
                JArray  keys = (JArray)root["keys"];

                foreach (JToken entry in keys)
                {
                    if (entry["key"]?.ToString().Trim() != key.Trim()) continue;

                    string hwid = entry["hwid"]?.ToString();
                    if (!string.IsNullOrEmpty(hwid) && hwid != GenHWID())
                    {
                        Debug.LogWarning("[TUP] Key is locked to a different HWID.");
                        result = false;
                        break;
                    }

                    result = true;
                    break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TUP] Key parse error: " + ex.Message);
            }

            done = true;
        };

        client.DownloadStringAsync(new Uri(KeysUrl));

        while (!done)
            yield return null;

        callback(result);
    }

    private IEnumerator LockHwid(string key)
    {
        string hwid    = GenHWID();
        string payload = $"{{\"key\":\"{key}\",\"hwid\":\"{hwid}\"}}";
        byte[] data    = Encoding.UTF8.GetBytes(payload);

        bool done = false;

        using WebClient client = new();
        client.Headers[HttpRequestHeader.ContentType] = "application/json";
        client.UploadDataCompleted += (sender, e) =>
        {
            if (e.Error != null)
                Debug.LogWarning("[TUP] Failed to lock HWID: " + e.Error.Message);
            else
            {
                string response = Encoding.UTF8.GetString(e.Result);
                Debug.Log("[TUP] lock_hwid response: " + response);
            }
            done = true;
        };

        client.UploadDataAsync(new Uri(ApiUrl + "?action=lock_hwid"), "POST", data);

        while (!done)
            yield return null;
    }

    private IEnumerator CheckAdminBypass(Action<bool> callback)
    {
        string localPlayfabId = PlayFabSettings.staticPlayer.PlayFabId;

        while (string.IsNullOrEmpty(localPlayfabId))
        {
            localPlayfabId = PlayFabSettings.staticPlayer.PlayFabId;
            yield return new WaitForSeconds(0.5f);
        }

        bool done   = false;
        bool result = false;

        using WebClient client = new();
        client.DownloadStringCompleted += (sender, e) =>
        {
            if (e.Error != null)
            {
                Debug.LogWarning("[TUP] failed to fetch admins for bypass: " + e.Error.Message);
                done = true;
                return;
            }

            try
            {
                JObject root   = JObject.Parse(e.Result);
                JArray  admins = (JArray)root["admins"];

                foreach (JToken entry in admins)
                {
                    string userId = entry["userId"]?.ToString();
                    if (userId == localPlayfabId)
                    {
                        result = true;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TUP] Admin bypass parse error: " + ex.Message);
            }

            done = true;
        };

        client.DownloadStringAsync(new Uri(AdminsUrl));

        while (!done)
            yield return null;

        callback(result);
    }

    private void ShowKeyInputUI()
    {
        Debug.Log("[TUP] showing key input UI");

        keyInputUI = new GameObject("TUP_KeyUI");
        Canvas canvas = keyInputUI.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;
        keyInputUI.AddComponent<CanvasScaler>();
        keyInputUI.AddComponent<GraphicRaycaster>();

        GameObject overlay = new("Overlay");
        overlay.transform.SetParent(keyInputUI.transform, false);
        Image overlayImg = overlay.AddComponent<Image>();
        overlayImg.color = new Color(0.067f, 0.067f, 0.106f, 0.97f);
        RectTransform overlayRect = overlay.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = overlayRect.offsetMax = Vector2.zero;

        GameObject panel = new("Panel");
        panel.transform.SetParent(keyInputUI.transform, false);
        Image panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.118f, 0.118f, 0.18f, 1f);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin        = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax        = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta        = new Vector2(480f, 260f);
        panelRect.anchoredPosition = Vector2.zero;

        GameObject titleObj = new("Title");
        titleObj.transform.SetParent(panel.transform, false);
        Text titleText = titleObj.AddComponent<Text>();
        titleText.text      = "THATUTILSPAD";
        titleText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.fontSize  = 22;
        titleText.fontStyle = FontStyle.Bold;
        titleText.color     = new Color(0.796f, 0.651f, 0.969f, 1f);
        titleText.alignment = TextAnchor.MiddleCenter;
        RectTransform titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.offsetMin = new Vector2(0f, -60f);
        titleRect.offsetMax = new Vector2(0f, -20f);

        GameObject subObj = new("Sub");
        subObj.transform.SetParent(panel.transform, false);
        Text subText = subObj.AddComponent<Text>();
        subText.text      = "enter your license key to continue";
        subText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        subText.fontSize  = 13;
        subText.color     = new Color(0.42f, 0.44f, 0.53f, 1f);
        subText.alignment = TextAnchor.MiddleCenter;
        RectTransform subRect = subObj.GetComponent<RectTransform>();
        subRect.anchorMin = new Vector2(0f, 1f);
        subRect.anchorMax = new Vector2(1f, 1f);
        subRect.offsetMin = new Vector2(0f, -90f);
        subRect.offsetMax = new Vector2(0f, -60f);

        GameObject inputBg = new("InputBg");
        inputBg.transform.SetParent(panel.transform, false);
        Image inputBgImg = inputBg.AddComponent<Image>();
        inputBgImg.color = new Color(0.067f, 0.067f, 0.106f, 1f);
        RectTransform inputBgRect = inputBg.GetComponent<RectTransform>();
        inputBgRect.anchorMin        = new Vector2(0.5f, 0.5f);
        inputBgRect.anchorMax        = new Vector2(0.5f, 0.5f);
        inputBgRect.sizeDelta        = new Vector2(380f, 44f);
        inputBgRect.anchoredPosition = new Vector2(0f, 20f);

        GameObject inputObj = new("KeyInput");
        inputObj.transform.SetParent(inputBg.transform, false);
        InputField inputField = inputObj.AddComponent<InputField>();

        GameObject inputTextObj = new("Text");
        inputTextObj.transform.SetParent(inputObj.transform, false);
        Text inputText = inputTextObj.AddComponent<Text>();
        inputText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        inputText.color     = new Color(0.8f, 0.84f, 0.96f, 1f);
        inputText.fontSize  = 14;
        inputText.alignment = TextAnchor.MiddleLeft;
        RectTransform inputTextRect = inputTextObj.GetComponent<RectTransform>();
        inputTextRect.anchorMin = Vector2.zero;
        inputTextRect.anchorMax = Vector2.one;
        inputTextRect.offsetMin = new Vector2(10f, 0f);
        inputTextRect.offsetMax = new Vector2(-10f, 0f);

        GameObject placeholderObj = new("Placeholder");
        placeholderObj.transform.SetParent(inputObj.transform, false);
        Text placeholder = placeholderObj.AddComponent<Text>();
        placeholder.text      = "TUP-XXXX-XXXX-XXXX-XXXX";
        placeholder.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        placeholder.color     = new Color(0.42f, 0.44f, 0.53f, 1f);
        placeholder.fontSize  = 14;
        placeholder.fontStyle = FontStyle.Italic;
        placeholder.alignment = TextAnchor.MiddleLeft;
        RectTransform phRect = placeholderObj.GetComponent<RectTransform>();
        phRect.anchorMin = Vector2.zero;
        phRect.anchorMax = Vector2.one;
        phRect.offsetMin = new Vector2(10f, 0f);
        phRect.offsetMax = new Vector2(-10f, 0f);

        RectTransform inputObjRect = inputObj.GetComponent<RectTransform>();
        if (inputObjRect == null) inputObjRect = inputObj.AddComponent<RectTransform>();
        inputObjRect.anchorMin = Vector2.zero;
        inputObjRect.anchorMax = Vector2.one;
        inputObjRect.offsetMin = inputObjRect.offsetMax = Vector2.zero;

        inputField.textComponent  = inputText;
        inputField.placeholder    = placeholder;
        inputField.characterLimit = 64;

        GameObject statusObj = new("Status");
        statusObj.transform.SetParent(panel.transform, false);
        Text statusText = statusObj.AddComponent<Text>();
        statusText.text      = "";
        statusText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        statusText.fontSize  = 12;
        statusText.color     = new Color(0.953f, 0.545f, 0.659f, 1f);
        statusText.alignment = TextAnchor.MiddleCenter;
        RectTransform statusRect = statusObj.GetComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0f, 0.5f);
        statusRect.anchorMax = new Vector2(1f, 0.5f);
        statusRect.offsetMin = new Vector2(0f, -10f);
        statusRect.offsetMax = new Vector2(0f,  10f);

        GameObject btnObj = new("SubmitBtn");
        btnObj.transform.SetParent(panel.transform, false);
        Image btnImg = btnObj.AddComponent<Image>();
        btnImg.color = new Color(0.796f, 0.651f, 0.969f, 0.2f);
        Button btn = btnObj.AddComponent<Button>();
        RectTransform btnRect = btnObj.GetComponent<RectTransform>();
        btnRect.anchorMin        = new Vector2(0.5f, 0f);
        btnRect.anchorMax        = new Vector2(0.5f, 0f);
        btnRect.sizeDelta        = new Vector2(180f, 42f);
        btnRect.anchoredPosition = new Vector2(0f, 30f);

        GameObject btnTextObj = new("Text");
        btnTextObj.transform.SetParent(btnObj.transform, false);
        Text btnText = btnTextObj.AddComponent<Text>();
        btnText.text      = "ACTIVATE";
        btnText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        btnText.fontSize  = 14;
        btnText.fontStyle = FontStyle.Bold;
        btnText.color     = new Color(0.796f, 0.651f, 0.969f, 1f);
        btnText.alignment = TextAnchor.MiddleCenter;
        RectTransform btnTextRect = btnTextObj.GetComponent<RectTransform>();
        btnTextRect.anchorMin = Vector2.zero;
        btnTextRect.anchorMax = Vector2.one;
        btnTextRect.offsetMin = btnTextRect.offsetMax = Vector2.zero;

        btn.onClick.AddListener(() =>
        {
            string enteredKey = inputField.text.Trim();
            if (string.IsNullOrEmpty(enteredKey))
            {
                statusText.text = "please enter a key";
                return;
            }

            statusText.color = new Color(0.42f, 0.44f, 0.53f, 1f);
            statusText.text  = "checking...";
            btn.interactable = false;

            StartCoroutine(SubmitKey(enteredKey, statusText, btn));
        });

        inputField.onEndEdit.AddListener(val =>
        {
            if (Keyboard.current != null &&
                (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
                btn.onClick.Invoke();
        });
    }

    private IEnumerator SubmitKey(string enteredKey, Text statusText, Button btn)
    {
        bool? result = null;
        yield return StartCoroutine(ValidateKey(enteredKey, success => result = success));

        if (result == true)
        {
            statusText.color = new Color(0.647f, 0.89f, 0.631f, 1f);
            statusText.text  = "key accepted!";
            File.WriteAllText(KeyFile, enteredKey);
            isKeyValid = true;
            yield return StartCoroutine(LockHwid(enteredKey));
            Destroy(keyInputUI, 1f);
            keyInputUI = null;
            OnKeyAccepted();
        }
        else
        {
            statusText.color = new Color(0.953f, 0.545f, 0.659f, 1f);
                statusText.text  = "invalid key ? contact support";
            btn.interactable = true;
        }
    }

    private void OnKeyAccepted()
    {
        Debug.Log("[TUP] key accepted, loading mod");
        PlayStartSound();
        CheckAdminStatus();
        Mods.Init();
        LoadBundles();
        FontCache.LoadFonts();
        ShaderCache.Init();
        new GameObject("TUP_CoroutineHandler").AddComponent<CoroutineHandler>();
        foreach (var n in buttonBundle.GetAllAssetNames()) Debug.Log("[TUP BUNDLE] " + n);
        btnPrefab = buttonBundle.LoadAsset<GameObject>("assets/prefabs/buttonmodel-rename.prefab");
        InitMenu();
        InitSettings();
        Mods.ApplySavedSettings();
    }
    
    
    private void InitCycleBtns()
    {
        if (menuObj == null) return;

        GorillaComputer gorillaComputer = GorillaComputer.instance;
        if (gorillaComputer == null)
        {
            Debug.LogError("[TUP] GorillaComputer instance is null");
            return;
        }
        
        Transform queueTextTransform = menuObj.transform.Find("Queue/ButtonText");
        if (queueTextTransform != null && queueTextTransform.TryGetComponent(out TextMeshPro qText))
        {
            queueText = qText;
            queueText.text = "Queue  :  " + gorillaComputer.currentQueue;
        }
        Transform queueSliderTransform = menuObj.transform.Find("Queue/Slider");
        Transform queueArrowTransform = menuObj.transform.Find("Queue/NextArrow");

        if (queueSliderTransform != null && queueArrowTransform != null)
        {
            queueSliderTransform.transform.gameObject.SetActive(false);
            queueArrowTransform.transform.gameObject.SetActive(true);
        }
        

        Transform modeTransform = menuObj.transform.Find("Mode/ButtonText");
        if (modeTransform != null && modeTransform.TryGetComponent(out TextMeshPro mText))
        {
            modeText = mText;
            modeText.text = "Mode  :  " + gorillaComputer.currentGameMode.ToString();
        }
        Transform modeSliderTransform = menuObj.transform.Find("Mode/Slider");
        Transform modeArrowTransform = menuObj.transform.Find("Mode/NextArrow");

        if (modeSliderTransform != null && modeArrowTransform != null)
        {
            modeSliderTransform.transform.gameObject.SetActive(false);
            modeArrowTransform.transform.gameObject.SetActive(true);
        }

        InitCycleButtonText("Region", ref regionText, "Region  :  " + Mods.GetRegionLabel());
        
        
        Transform clickSoundTransform = menuObj.transform.Find("Click Sound/ButtonText");
        if (clickSoundTransform != null && clickSoundTransform.TryGetComponent(out TextMeshPro csText))
        {
            clickSoundText = csText;
            string rawName   = Mods.clickSounds.Count > 0 ? Mods.clickSounds[Mods.currentClickIndex].Name : "-";
            string soundName = rawName.Length > 0 ? char.ToUpper(rawName[0]) + rawName.Substring(1).ToLower() : "-";
            clickSoundText.text = "Click Sound  :  " + soundName;
        }
        Transform clickSoundSliderTransform = menuObj.transform.Find("Click Sound/Slider");
        Transform clickSoundArrowTransform = menuObj.transform.Find("Click Sound/NextArrow");

        if (clickSoundSliderTransform != null && clickSoundArrowTransform != null)
        {
            clickSoundSliderTransform.transform.gameObject.SetActive(false);
            clickSoundArrowTransform.transform.gameObject.SetActive(true);
        }

        InitCycleButtonText("Menu Smoothing", ref smoothingText, "Menu Smoothing  :  On");
        InitCycleButtonText("Smooth Strength", ref smoothingStrengthText, "Smooth Strength  :  " + Mods.GetSmoothingStrengthLabel());
        InitCycleButtonText("Menu Scale", ref menuScaleText, "Menu Scale  :  " + Mods.GetMenuScaleLabel());
        InitCycleButtonText("PC Distance", ref headDistanceText, "PC Distance  :  " + Mods.GetHeadDistanceLabel());
        InitCycleButtonText("Double Open", ref doubleOpenText, "Double Open  :  Off");
        InitCycleButtonText("Open Bind", ref openBindText, "Open Bind  :  " + GetMenuOpenBindDisplayName(DefaultMenuOpenBindCode));
        InitCycleButtonText("Theme", ref themeText, "Theme  :  " + Mods.GetThemeLabel());
        Mods.UpdateSettingsLabels();
    }

    private void InitCycleButtonText(string buttonName, ref TextMeshPro target, string defaultText)
    {
        Transform textTransform = menuObj.transform.Find(buttonName + "/ButtonText");
        if (textTransform != null && textTransform.TryGetComponent(out TextMeshPro text))
        {
            target = text;
            target.text = defaultText;
        }

        Transform sliderTransform = menuObj.transform.Find(buttonName + "/Slider");
        Transform arrowTransform = menuObj.transform.Find(buttonName + "/NextArrow");
        if (sliderTransform != null && arrowTransform != null)
        {
            sliderTransform.gameObject.SetActive(false);
            arrowTransform.gameObject.SetActive(true);
        }
    }
    
    private void InitSettings()
    {
        currentClickSound = watchSound;
    }
    
    private void Update()
    {
        if (!isKeyValid)
            return;

        Mods.NameTagsLoop();
        
        bool currentControllerState     = IsMenuOpenBindPressed();
        bool controllerPressedThisFrame = currentControllerState && !previousControllerState;
        previousControllerState = currentControllerState;

        bool keyboardPressedThisFrame = Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame;
        bool keyboardHeld             = Keyboard.current != null && Keyboard.current.tKey.isPressed;

        if (!Mods.IsRenaming)
        {
            if (toggleMenuMethod && !AlwaysShowMenu)
            {
                bool togglePressed = ConsumeMenuOpenBindPress(controllerPressedThisFrame) || keyboardPressedThisFrame;

                if (togglePressed)
                {
                    if (isMenuOpened)
                    {
                        StartCoroutine(CloseMenu());
                        isMenuOpened = false;
                    }
                    else
                    {
                        currentOpenType = currentControllerState ? MenuOpenType.Hand : MenuOpenType.Head;
                        OpenMenu();
                        isMenuOpened = true;
                    }
                }
            }
            else
            {
                bool controllerHeld = currentControllerState;
                bool keyboardIsHeld = keyboardHeld;

                if (!isMenuOpened && (controllerHeld || keyboardIsHeld || AlwaysShowMenu))
                {
                    currentOpenType = controllerHeld ? MenuOpenType.Hand : MenuOpenType.Head;
                    OpenMenu();
                    isMenuOpened = true;
                }
                else if (isMenuOpened && !controllerHeld && !keyboardIsHeld && !AlwaysShowMenu)
                {
                    StartCoroutine(CloseMenu());
                    isMenuOpened = false;
                }
            }
        }

        if (!isMenuOpened)
            return;

        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
            return;

        Camera raycastCamera = GetActiveCamera();
        Ray    ray           = raycastCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f, 1 << 2, QueryTriggerInteraction.Collide))
            return;

        StartCoroutine(MenuEffects.SpawnHitCircle(hit.point, hit.transform));

        if (!hit.collider.TryGetComponent(out ButtonTrigger buttonTrigger))
            buttonTrigger = hit.collider.GetComponentInParent<ButtonTrigger>();

        if (buttonTrigger != null)
            ButtonTrigger.PcPress(buttonTrigger);
    }
    
    private bool ConsumeMenuOpenBindPress(bool pressedThisFrame)
    {
        if (!pressedThisFrame)
            return false;

        if (!requireDoubleClickOpen)
            return true;

        if (Time.time - lastMenuOpenBindPressTime <= MenuOpenDoubleClickWindow)
        {
            lastMenuOpenBindPressTime = -10f;
            return true;
        }

        lastMenuOpenBindPressTime = Time.time;
        return false;
    }

    public void SetMenuOpenOptions(bool doubleClickRequired, string bindCode)
    {
        requireDoubleClickOpen = doubleClickRequired;
        menuOpenBindCode = string.IsNullOrWhiteSpace(bindCode) ? DefaultMenuOpenBindCode : bindCode;
        previousControllerState = IsMenuOpenBindPressed();
        lastMenuOpenBindPressTime = -10f;
    }

    private bool IsMenuOpenBindPressed() => IsMenuOpenBindPressed(menuOpenBindCode);

    public static bool IsMenuOpenBindPressed(string bindCode)
    {
        if (string.IsNullOrWhiteSpace(bindCode))
            bindCode = DefaultMenuOpenBindCode;

        if (bindCode.StartsWith("InputSystem:", StringComparison.OrdinalIgnoreCase))
        {
            string path = bindCode.Substring("InputSystem:".Length);
            return IsInputSystemButtonPressed(path);
        }

        ControllerInputPoller poller = ControllerInputPoller.instance;
        if (poller == null)
            return false;

        return bindCode switch
        {
            "Left:SecondaryButton" => poller.leftControllerSecondaryButton,
            "Right:GripButton" => poller.rightControllerGripFloat > 0.75f,
            "Right:TriggerButton" => poller.rightControllerIndexFloat > 0.75f,
            _ => false
        };
    }

    public static bool TryGetPressedMenuOpenBind(HashSet<string> ignoredCodes, out string bindCode, out string displayName)
    {
        foreach (string fallbackCode in BuiltInMenuOpenBindCodes)
        {
            if (ignoredCodes != null && ignoredCodes.Contains(fallbackCode))
                continue;

            if (!IsMenuOpenBindPressed(fallbackCode))
                continue;

            bindCode = fallbackCode;
            displayName = GetMenuOpenBindDisplayName(fallbackCode);
            return true;
        }

        foreach (UnityEngine.InputSystem.InputDevice device in InputSystem.devices)
        {
            if (device == null || device is Keyboard || device is Mouse)
                continue;

            foreach (UnityEngine.InputSystem.InputControl control in device.allControls)
            {
                if (control is not UnityEngine.InputSystem.Controls.ButtonControl button)
                    continue;

                if (IsIgnoredInputButton(button))
                    continue;

                string code = "InputSystem:" + button.path;
                if (ignoredCodes != null && ignoredCodes.Contains(code))
                    continue;

                if (!button.isPressed)
                    continue;

                bindCode = code;
                displayName = FormatInputSystemButtonName(button);
                return true;
            }
        }

        bindCode = "";
        displayName = "";
        return false;
    }

    public static HashSet<string> GetPressedMenuOpenBinds()
    {
        HashSet<string> pressed = new HashSet<string>();

        foreach (string fallbackCode in BuiltInMenuOpenBindCodes)
        {
            if (IsMenuOpenBindPressed(fallbackCode))
                pressed.Add(fallbackCode);
        }

        foreach (UnityEngine.InputSystem.InputDevice device in InputSystem.devices)
        {
            if (device == null || device is Keyboard || device is Mouse)
                continue;

            foreach (UnityEngine.InputSystem.InputControl control in device.allControls)
            {
                if (control is not UnityEngine.InputSystem.Controls.ButtonControl button)
                    continue;

                if (IsIgnoredInputButton(button) || !button.isPressed)
                    continue;

                pressed.Add("InputSystem:" + button.path);
            }
        }

        return pressed;
    }

    public static string GetMenuOpenBindDisplayName(string bindCode)
    {
        if (string.IsNullOrWhiteSpace(bindCode))
            bindCode = DefaultMenuOpenBindCode;

        if (bindCode.StartsWith("InputSystem:", StringComparison.OrdinalIgnoreCase))
        {
            string path = bindCode.Substring("InputSystem:".Length);
            UnityEngine.InputSystem.InputControl control = InputSystem.FindControl(path);
            if (control is UnityEngine.InputSystem.Controls.ButtonControl button)
                return FormatInputSystemButtonName(button);

            return path;
        }

        return bindCode switch
        {
            "Left:SecondaryButton" => "Left Secondary",
            "Right:GripButton" => "Right Grip",
            "Right:TriggerButton" => "Right Trigger",
            _ => "Left Secondary"
        };
    }

    private static bool IsInputSystemButtonPressed(string path)
    {
        UnityEngine.InputSystem.InputControl control = InputSystem.FindControl(path);
        return control is UnityEngine.InputSystem.Controls.ButtonControl button && button.isPressed;
    }

    private static bool IsIgnoredInputButton(UnityEngine.InputSystem.Controls.ButtonControl button)
    {
        string name = (button.name ?? "").ToLowerInvariant();
        string path = (button.path ?? "").ToLowerInvariant();
        return name.Contains("touch") || path.Contains("touch") || name.Contains("position") || path.Contains("position");
    }

    private static string FormatInputSystemButtonName(UnityEngine.InputSystem.Controls.ButtonControl button)
    {
        string deviceName = button.device?.displayName;
        if (string.IsNullOrWhiteSpace(deviceName))
            deviceName = button.device?.name ?? "Controller";

        string controlName = !string.IsNullOrWhiteSpace(button.displayName) ? button.displayName : button.name;
        if (string.IsNullOrWhiteSpace(controlName))
            controlName = button.path;

        return deviceName + " " + controlName;
    }

    private static readonly string[] BuiltInMenuOpenBindCodes =
    {
        DefaultMenuOpenBindCode,
        "Right:GripButton",
        "Right:TriggerButton",
    };
    private static void PrintHierarchy(Transform t, int depth)
    {
        Debug.Log(new string('-', depth * 2) + t.name);
        foreach (Transform child in t)
            PrintHierarchy(child, depth + 1);
    }

    private void InitMenu()
    {
        if (menuInitialized)
            return;

        string prefabPath = UseSakuraTheme
            ? "assets/prefabs/TUP-overv9.prefab"
            : "assets/prefabs/tup-modelsmooth.prefab";

        AssetBundle bundle = UseSakuraTheme ? menuReduxBundle : menuBundle;
        GameObject  prefab = bundle.LoadAsset<GameObject>(prefabPath);
        
        
        menuObj = Instantiate(prefab);
        menuObj.transform.localScale = Vector3.one * menuScale;
        
        //monkeColor = menuObj.transform.Find("SideHolder/MonkeColor").GetComponent<SpriteRenderer>();
        volumeText = menuObj.transform.Find("SideHolder/VolumePercent").GetComponent<TextMeshPro>();
        
        InitPageButtons(menuObj);
        InitCheckerText(menuObj);
        InitRoomInfo(menuObj);

        Rigidbody rb = menuObj.GetComponent<Rigidbody>();
        if (rb != null) Destroy(rb);

        Collider col = menuObj.GetComponent<Collider>();
        if (col != null) Destroy(col);

        Renderer renderer = menuObj.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            renderer.material.shader = ShaderCache.UberShader;
            renderer.material.color = new Color32(171, 0, 63, 255);
        }

        PrintHierarchy(menuObj.transform, 0);

        MenuTheme.Assign(menuObj, Theme);

        InitNavButtons(menuObj);
        InitKeyboard();

        menuObj.SetActive(false);
        menuInitialized = true;
        StartCoroutine(RoomInfoUpdater());
    }
    
private void OpenMenu()
{
    if (!menuInitialized || menuObj == null)
        return;

    Transform parent = currentOpenType == MenuOpenType.Head
        ? GetActiveCamera().transform
        : GTPlayer.Instance.LeftHand.controllerTransform;

    menuObj.transform.SetParent(null);

    SmoothFollowMenu smooth = menuObj.GetComponent<SmoothFollowMenu>() ?? menuObj.AddComponent<SmoothFollowMenu>();
    smooth.Target = parent;
    smooth.Smoothing = menuSmoothingEnabled ? menuSmoothingStrength : 1000f;

    if (currentOpenType == MenuOpenType.Head)
    {
        smooth.LocalPosition = new Vector3(-0.03f, -0.02f, headMenuDistance);
        smooth.LocalRotation = Quaternion.Euler(0f, 270f, 0f);
    }
    else
    {
        smooth.LocalPosition = menuHandOffset + menuGripPosition;
        smooth.LocalRotation = Quaternion.Euler(270f - menuGripRotaton, 180f, 0f);
    }
    
    Tools.CacheCheckerScales(menuObj.transform);
    
    Transform sideHolder = menuObj.transform.Find("SideHolder");
    if (sideHolder != null)
    {
        string[] checkerParts =
        {
            "MonkeBase",
            "MonkeColor",
            "Name",
            "FPS",
            "Platform",
            "Ping",
            "Date",
            "VolumeUp",
            "VolumeDown",
            "Volume",
            "VolumePercent",
            "MuteElse",
            "Mute",
            "AddToSaved"
        };

        foreach (string part in checkerParts)
        {
            Transform t = sideHolder.Find(part);
            if (t != null)
                t.localScale = Vector3.zero;
        }
    }
    
    foreach (GameObject selectorObj in selectorBtnObjs)
        selectorObj.SetActive(true);

    foreach (GameObject navBtn in navBtnObjs)
        navBtn.SetActive(true);

    menuObj.SetActive(true);

    AudioSource audioSource =
        gameObject.GetComponent<AudioSource>() ??
        gameObject.AddComponent<AudioSource>();

    audioSource.PlayOneShot(menuOpenSound);

    StartCoroutine(
            MenuEffects.PopMenu(
            menuObj,
            Vector3.one * menuScale,
            Vector3.zero,
            true
        )
    );

    StartCoroutine(MenuEffects.CheckerCompEnum(menuObj.transform));

    Debug.Log("Menu Opened");
    Debug.Log(menuObj.activeSelf);

    DestroyButtons();
    InitSelectorObj();
    buttonRoutine = StartCoroutine(CreateButtons());
}
    

    private TMP_Text FindText(GameObject obj, string parentName)
    {
        var parent = obj.transform.Find(parentName);
        if (parent == null)
        {
            Debug.LogError($"[TUP] Missing parent: {parentName}");
            return null;
        }

        var tmp = parent.GetComponentInChildren<TMP_Text>(true);
        if (tmp == null)
            Debug.LogError($"[TUP] No TMP_Text under: {parentName}");

        return tmp;
    }

    
    private void InitCheckerText(GameObject menuObj)
    {
        string figPrefabPath = "assets/fonts/figtree.asset"; //"assets/fonts/minecraftia.asset"

        AssetBundle figBundle = FontCache.figtreeBundle; //minecraftiaBundle
        TMP_FontAsset figtreeFont = figBundle.LoadAsset<TMP_FontAsset>(figPrefabPath); //minecraftiaFont

        float fontSize = 0.4f;
        float infoFontSize = 0.4f;

        GameObject side = menuObj.transform.Find("SideHolder").gameObject;
        
        nameTextComp        = FindText(side, "Name");
        platformTextComp    = FindText(side, "Platform");
        fpsTextComp         = FindText(side, "FPS");
        pingTextComp        = FindText(side, "Ping");
        dateTextComp        = FindText(side, "Date");
        modsTextComp        = FindText(side, "Mods");
        cheatsTextComp      = FindText(side, "Cheats");
        
        nameTextComp.text = "-----";
        nameTextComp.font = figtreeFont;
        
        fpsTextComp.text = "FPS                -       ";
        fpsTextComp.font = figtreeFont;
        
        platformTextComp.text = "Platform       -       ";
        platformTextComp.font = figtreeFont;
        
        pingTextComp.text = "Ping               -       ";
        pingTextComp.font = figtreeFont;
        
        dateTextComp.text = "Date              -       ";
        dateTextComp.font = figtreeFont;
        
        modsTextComp.text = "Mods   -   None";
        modsTextComp.font = figtreeFont;
        
        cheatsTextComp.text = "Cheats   -   None";
        cheatsTextComp.font = figtreeFont;

        List<string> btns = new List<string>()
        {
            "VolumeUp",
            "VolumeDown",
            "MuteElse",
            "Mute"
        };
        
        
        foreach (string btn in btns)
        {
            var sideHolder = menuObj.transform.Find("SideHolder");
            var button = sideHolder.Find(btn)?.gameObject;
            if (button == null)
            {
                Debug.LogError($"[TUP] Button not found: {btn}");
                continue;
            }
            button.layer = 2;
            
            ButtonTrigger trigger = button.AddComponent<ButtonTrigger>();

            trigger.BtnIdentifier = btn;
            button.AddComponent<ButtonCollider>().trigger = trigger;
            
            var col = button.GetComponent<Collider>();
            if (col == null)
                col = button.AddComponent<BoxCollider>();

            col.isTrigger = true;

            var rb = button.GetComponent<Rigidbody>();
            if (rb == null)
                rb = button.AddComponent<Rigidbody>();

            rb.isKinematic = true;
            rb.useGravity = false;
            
            //GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            //quad.transform.SetParent(button.transform);
            //quad.transform.localPosition = new Vector3(0f, 0f, 0.011f);
            //quad.transform.localRotation = Quaternion.Euler(0f, 180f, 90f);
            //quad.transform.localScale = new Vector3(0.012f, 0.012f, 0.012f);

            //var quadCol = quad.GetComponent<Collider>();
            //if (quadCol != null)
            //   Destroy(quadCol);
            
            //Renderer quadRend = quad.GetComponent<Renderer>();
            //quadRend.material = new Material(Shader.Find("Unlit/Texture"));
            //quadRend.material.mainTexture = MenuComponents.Tools.LoadEmbeddedImage(btn.ToLower() + ".png");
        }
    }
    
    public void UpdateCheckerText(string plrName, int plrFPS, string plrPlatform, string plrCreationDate, string plrColor, Color colorBaseForm, int plrPing)
    {
        nameTextComp.text = $"{plrName.ToUpper()}   {plrColor}";
        
        
        if (plrFPS < 60)
            fpsTextComp.text = $"<color=yellow>FPS                -       {plrFPS}</color>";
        else
            fpsTextComp.text = $"<color=white>FPS                -       {plrFPS}</color>";
        
        
        if (plrPing > 275)
            pingTextComp.text = $"<color=yellow>Ping               -       {plrPing}</color>";
        else
            pingTextComp.text = $"<color=white>Ping               -       {plrPing}</color>";
        
        
        platformTextComp.text = $"Platform       -       {plrPlatform}";
        dateTextComp.text = $"<color=#1E1E2E>Date              -       {plrCreationDate}</color>";

        var monkeColor = menuObj.transform.Find("SideHolder/MonkeColor").transform.GetComponent<SpriteRenderer>();
        monkeColor.color = colorBaseForm;
    }

    public void UpdateCheckerProperties(string legalMods, string illegalMods)
    {
        if (modsTextComp != null)
            modsTextComp.text = "Mods   -   " + (string.IsNullOrWhiteSpace(legalMods) ? "None" : legalMods);

        if (cheatsTextComp != null)
            cheatsTextComp.text = "Cheats   -   " + (string.IsNullOrWhiteSpace(illegalMods) ? "None" : illegalMods);
    }
    
    
    TMP_Text roomInfoText;
    TMP_Text roomNameText;
    TMP_Text dateTimeText;

    private void InitRoomInfo(GameObject menuObj)
    {
        Transform menu = menuObj.transform;

        roomInfoText = menu.Find("RoomInfo")?.GetComponent<TMP_Text>();
        roomNameText = menu.Find("RoomName")?.GetComponent<TMP_Text>();
        dateTimeText = menu.Find("DateTime")?.GetComponent<TMP_Text>();

        if (roomInfoText == null) Debug.LogError("RoomInfo not found");
        if (roomNameText == null) Debug.LogError("RoomName not found");
        if (dateTimeText == null) Debug.LogError("DateTime not found");
    }
    
    private IEnumerator RoomInfoUpdater()
    {
        WaitForSeconds wait = new WaitForSeconds(1f);

        while (true)
        {
            if (isKeyValid && isMenuOpened)
            {
                string date = DateTime.Now.ToString("MM/dd/yy");
                string time = DateTime.Now.ToString("HH:mm");

                dateTimeText.text = $"{date}   |   {time}";

                string roomName = Photon.Pun.PhotonNetwork.CurrentRoom?.Name ?? "----";
                roomNameText.text = roomName;

                int players = Photon.Pun.PhotonNetwork.CurrentRoom?.PlayerCount ?? 0;
                int max = Photon.Pun.PhotonNetwork.CurrentRoom?.MaxPlayers ?? 10;
                int ping = Photon.Pun.PhotonNetwork.GetPing();
                string name = Photon.Pun.PhotonNetwork.NickName;

                roomInfoText.text = $"{players}/{max} Players  |  {ping} ms  |  {name}";
            }

            yield return wait;
        }
    }

    private void InitSelectorObj()
    {
        selectionObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        selectionObj.transform.SetParent(GTPlayer.Instance.RightHand.controllerTransform);
        selectionObj.transform.localPosition = Vector3.zero;
        selectionObj.transform.localRotation = Quaternion.identity;
        selectionObj.transform.localScale = Vector3.one * 0.01f;
        
        var col = selectionObj.GetComponent<SphereCollider>();
        if (col == null)
            col = selectionObj.AddComponent<SphereCollider>();
        col.isTrigger = true;
        
        Rigidbody rb = selectionObj.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        
        selectionObj.AddComponent<ButtonPresser>();
        
        var rend = selectionObj.GetComponent<Renderer>();
        rend.material.shader = ShaderCache.UberShader;
        rend.material.color = Theme.Accent;
    }
    
    private IEnumerator CloseMenu()
    {
        Tools.StopCoroutine(ref buttonRoutine);

        yield return StartCoroutine(
            MenuEffects.PopMenu(menuObj, Vector3.zero, menuObj.transform.localScale, false)
        );

        menuObj.SetActive(false);
        selectionObj.Destroy();
        DestroyButtons();

        foreach (GameObject selectorObj in selectorBtnObjs)
            selectorObj.SetActive(false);

        foreach (GameObject navBtn in navBtnObjs)
            navBtn.SetActive(false);
        
        
        Debug.Log("Menu Closed");
        Debug.Log(menuObj.activeSelf);
    }
    
    private string GenHWID() => SystemInfo.deviceUniqueIdentifier;
    
    private IEnumerator CreateButtons(string categoryName = "Networking")
    {
        const float startY = 0.31f;
        const float gap    = 0.09f;

        if (!Mods.Actions.TryGetValue(currentCategory, out var category))
            yield break;

        var actionList = category.Actions.ToList();
        int start = currentPage * PageSize;
        int end   = Math.Min(start + PageSize, actionList.Count);

        int index = 0;
        for (int i = start; i < end; i++)
        {
            var mod      = actionList[i];
            float height = startY - (index * gap);
            CreateButton(height, mod.Key, mod.Value.IsToggle, mod.Value.Cooldown);
            index++;
            yield return new WaitForSeconds(0.08f);
        }
    }
    
    private void SwitchCategory(string categoryName)
    {
        Tools.StopCoroutine(ref buttonRoutine);
        currentCategory = categoryName;
        currentPage = 0;

        if (categoryName == "Cosmetics")
            Mods.InitOutfitSlotActions();

        DestroyButtons();
        buttonRoutine = StartCoroutine(CreateButtons());
    }

    private void InitPageButtons(GameObject menuObj)
    {
        List<string> categories = Mods.Actions
            .Where(x => x.Value.ShowInMenu)
            .Select(x => x.Key)
            .ToList();

        foreach (Transform child in menuObj.GetComponentsInChildren<Transform>(true))
        {
            if (!child.name.Contains("SelectorBtn", StringComparison.OrdinalIgnoreCase))
                continue;
            
            child.gameObject.layer = 2;
            
            Collider col = child.gameObject.GetComponent<Collider>();
            if (col == null)
                col = child.gameObject.AddComponent<BoxCollider>();
            col.isTrigger = true;
            
            Rigidbody rb = child.GetComponent<Rigidbody>();
            if (rb == null)
                rb = child.gameObject.AddComponent<Rigidbody>();

            rb.isKinematic = true;
            rb.useGravity = false;
            
            
            ButtonTrigger trigger = child.AddComponent<ButtonTrigger>();
            trigger.BtnIdentifier = child.name;
            child.AddComponent<ButtonCollider>().trigger = trigger;
            
            Vector3 previousLocalPos = child.transform.localPosition;
            Quaternion previousLocalRot = child.transform.localRotation;

            child.transform.parent = null;

            var follow = child.AddComponent<FollowMenu>();
            follow.Target = menuObj.transform;
            follow.Position = previousLocalPos;
            follow.Rotation = previousLocalRot;

            MenuTheme.ApplySelectorButton(child);

            //if (child.transform.parent != null)
            //    Debug.Log($"[TUP] Parent: {child.transform.parent}");
            //else
            //    Debug.Log($"[TUP] No parent found");

            string numberPart = child.name.Replace("SelectorBtn", "");
            if (!int.TryParse(numberPart, out int index))
                continue;

            index -= 1;
            if (index < 0 || index >= categories.Count)
            {
                child.gameObject.SetActive(false);
                continue;
            }

            string categoryName = categories[index];
            trigger.CustomAction = () => SwitchCategory(categoryName);
            //Debug.Log($"[TUP] Assigned {child.name} -> {categoryName}");
            
            selectorBtnObjs.Add(child.gameObject);
        }
    }

    private void InitNavButtons(GameObject menuObj)
    {
        string[] navNames = { "PageBack", "PageNext" };
        foreach (string navName in navNames)
        {
            Transform navTransform = null;
            foreach (Transform child in menuObj.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.Equals(navName, StringComparison.OrdinalIgnoreCase))
                {
                    navTransform = child;
                    break;
                }
            }
            if (navTransform == null)
            {
                Debug.LogWarning($"[TUP] Nav button '{navName}' not found in menu prefab");
                continue;
            }

            navTransform.gameObject.layer = 2;

            Collider col = navTransform.GetComponent<Collider>();
            if (col == null)
                col = navTransform.gameObject.AddComponent<BoxCollider>();
            col.isTrigger = true;

            Rigidbody rb = navTransform.GetComponent<Rigidbody>();
            if (rb == null)
                rb = navTransform.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity  = false;

            ButtonTrigger trigger = navTransform.AddComponent<ButtonTrigger>();
            trigger.BtnIdentifier = navName;
            navTransform.AddComponent<ButtonCollider>().trigger = trigger;

            Vector3    prevLocalPos = navTransform.localPosition;
            Quaternion prevLocalRot = navTransform.localRotation;
            navTransform.parent = null;

            var follow    = navTransform.AddComponent<FollowMenu>();
            follow.Target   = menuObj.transform;
            follow.Position = prevLocalPos;
            follow.Rotation = prevLocalRot;

            MenuTheme.ApplyNavButton(navTransform);

            int delta = navName == "PageNext" ? 1 : -1;
            trigger.CustomAction = () => ChangePage(delta);

            navBtnObjs.Add(navTransform.gameObject);
        }
    }

    private void ChangePage(int delta)
    {
        if (!Mods.Actions.TryGetValue(currentCategory, out var category))
            return;

        int totalPages = Mathf.Max(1, Mathf.CeilToInt((float)category.Actions.Count / PageSize));
        currentPage = (currentPage + delta + totalPages) % totalPages;

        Tools.StopCoroutine(ref buttonRoutine);
        DestroyButtons();
        buttonRoutine = StartCoroutine(CreateButtons());
    }

    public void RefreshCurrentPage()
    {
        if (!isMenuOpened) return;
        Tools.StopCoroutine(ref buttonRoutine);
        DestroyButtons();
        buttonRoutine = StartCoroutine(CreateButtons());
    }

    public void AppendOutfitButton(int n)
    {
        if (!isMenuOpened || currentCategory != "Cosmetics") return;
        if (!Mods.Actions.TryGetValue("Cosmetics", out var category)) return;

        var actionList = category.Actions.ToList();
        int itemIndex  = actionList.FindIndex(kv => kv.Key == "Saved Outfit #" + n);
        if (itemIndex < 0) return;

        int pageOfItem = itemIndex / PageSize;
        if (pageOfItem != currentPage) return;

        const float startY = 0.31f;
        const float gap    = 0.09f;
        int   visualIndex  = itemIndex - (currentPage * PageSize);
        float height       = startY - (visualIndex * gap);

        CreateButton(height, "Saved Outfit #" + n, false, 0f);
    }

    void LoadAudio()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        creamySound        = LoadAudioClip(assembly, "ThatUtilsPad.Assets.Sounds.creamy.wav");
        startSound         = LoadAudioClip(assembly, "ThatUtilsPad.Assets.Sounds.tup_startup.wav");
        helloSound         = LoadAudioClip(assembly, "ThatUtilsPad.Assets.Sounds.hello.wav");
        menuOpenSound      = LoadAudioClip(assembly, "ThatUtilsPad.Assets.Sounds.UiEnter.wav");
        btnEnterSound      = LoadAudioClip(assembly, "ThatUtilsPad.Assets.Sounds.btnEnter.wav");
        minecraftSound     = LoadAudioClip(assembly, "ThatUtilsPad.Assets.Sounds.minecraft.wav");
        watchSound         = LoadAudioClip(assembly, "ThatUtilsPad.Assets.Sounds.watch.wav");
        destinySound       = LoadAudioClip(assembly, "ThatUtilsPad.Assets.Sounds.destiny.wav");
        wiiSound           = LoadAudioClip(assembly, "ThatUtilsPad.Assets.Sounds.Wii.wav");
        untitledClickSound = LoadAudioClip(assembly, "ThatUtilsPad.Assets.Sounds.untitled.wav");

        if (creamySound == null)
            Debug.LogError("[TUP] Failed to load click sound!");

        if (startSound == null)
            Debug.LogError("[TUP] Failed to load startup sound!");

        if (helloSound == null)
            Debug.LogError("[TUP] Failed to load hello sound!");
    }

    private AudioClip LoadAudioClip(Assembly assembly, string resourceName)
    {
        using Stream stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            Debug.LogWarning($"[TUP] {resourceName} not found in resources");
            return null;
        }

        byte[] buffer = new byte[stream.Length];
        stream.Read(buffer, 0, buffer.Length);

        return WavUtility.ToAudioClip(buffer, resourceName);
    }

    private void PlayStartSound()
    {
        if (startSound == null)
        {
            Debug.LogWarning("[TUP] startSound is null, cannot play");
            return;
        }

        AudioSource audioSource = gameObject.GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.PlayOneShot(startSound);
    }

    private void CheckAdminStatus()
    {
        StartCoroutine(WaitForPlayFabThenCheck());
    }

    private IEnumerator WaitForPlayFabThenCheck()
    {
        string localPlayfabId = "";

        while (string.IsNullOrEmpty(localPlayfabId))
        {
            localPlayfabId = PlayFabSettings.staticPlayer.PlayFabId;
            yield return new WaitForSeconds(1f);
        }

        try
        {
            using WebClient client = new();
            client.DownloadStringCompleted += (sender, e) =>
            {
                if (e.Error != null)
                {
                    Debug.LogWarning("[TUP] failed to fetch admin list: " + e.Error.Message);
                    return;
                }

                Debug.Log("[TUP] raw response: " + e.Result);

                try
                {
                    JObject root   = JObject.Parse(e.Result);
                    JArray  admins = (JArray)root["admins"];

                    foreach (JToken entry in admins)
                    {
                        string userId = entry["userId"]?.ToString();
                        string name   = entry["name"]?.ToString();

                        if (string.IsNullOrEmpty(userId)) continue;
                        if (userId != localPlayfabId)    continue;

                        IsAdmin   = true;
                        AdminName = name ?? "Admin";
                        break;
                    }
                }
                catch (Exception parseEx)
                {
                    Debug.LogWarning("[TUP] failed to parse admin JSON: " + parseEx.Message);
                }

                if (IsAdmin)
                {
                    Debug.Log("[TUP] Logged in as Admin: " + AdminName);
                    PlayHelloSound();
                    //SpeakWelcome(AdminName);
                }
                else
                {
                    Debug.Log("[TUP] Logged in as normal user");
                }
            };

            client.DownloadStringAsync(new Uri(AdminsUrl));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TUP] admin check failed: " + e.Message);
        }
    }

    private void SpeakWelcome(string name)
    {
        try
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = "powershell",
                    Arguments       = $"-Command \"Add-Type -AssemblyName System.Speech; $s = New-Object System.Speech.Synthesis.SpeechSynthesizer; $s.SelectVoiceByHints([System.Speech.Synthesis.VoiceGender]::Male); $s.Speak('Welcome to That Utils Pad, {name}')\"",
                    UseShellExecute = false,
                    CreateNoWindow  = true,
                });
            });
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TUP] texttospeech failed: " + e.Message);
        }
    }

    private void PlayHelloSound()
    {
        if (helloSound == null) return;

        AudioSource audioSource = gameObject.GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.PlayOneShot(helloSound);
    }
    
    
private void CreateButton(float zOffset, string btnName, bool isToggle = false, float cooldown = 0f)
{
    string prefabPath = "assets/fonts/figtree.asset"; // "assets/fonts/minecraftia.asset"
    AssetBundle bundle = FontCache.figtreeBundle; // minecraftiaBundle
    TMP_FontAsset figtreeFont = bundle.LoadAsset<TMP_FontAsset>(prefabPath); // minecraftiaFont

    GameObject btn = Instantiate(btnPrefab, menuObj.transform);
    btn.name = btnName;
    GameObject btnCollider = GameObject.CreatePrimitive(PrimitiveType.Cube);
    
    Transform slider = btn.transform.Find("Slider");
    Transform knob = btn.transform.Find("Slider/Knob");
    
    var colliderFollow = btnCollider.AddComponent<FollowMenu>();
    colliderFollow.Target = btn.transform;
    colliderFollow.Rotation = Quaternion.Euler(90f, 0f, 0f);
    colliderFollow.Position = Vector3.zero;

    Vector3 stackedPos = buttonBasePosition + new Vector3(0.01f, zOffset, 0f);

    btn.transform.localPosition = stackedPos;
    btn.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

    btnCollider.transform.localPosition = stackedPos;
    btnCollider.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
    btnCollider.transform.localScale = new Vector3(0.05f, 0.065f, 0.55f) * 0.35f;
    btnCollider.layer = 2;

    ButtonTrigger trigger = btnCollider.AddComponent<ButtonTrigger>();

    trigger.BtnIdentifier = btnName;
    trigger.Slider = slider;
    trigger.Knob = knob;
    trigger.IsToggle = isToggle;
    trigger.Cooldown = cooldown;
    trigger.ButtonRoot = btn.transform;
    btnCollider.AddComponent<ButtonCollider>().trigger = trigger;
    btnCollider.GetComponent<Collider>().isTrigger = true;
    Destroy(btnCollider.GetComponent<Rigidbody>());
    
    MenuTheme.ApplyMenuButton(btn.transform, out Renderer renderer, out Renderer outlineRenderer);
    trigger.BodyRenderer = renderer;

    Renderer rendererCollider = btnCollider.GetComponentInChildren<Renderer>();
    if (rendererCollider != null)
    {
        rendererCollider.material.shader = ShaderCache.TextShader;
        rendererCollider.material.color = new Color32(255, 0, 0, 50);
        rendererCollider.enabled = false;
    }

    trigger.OutlineRenderer = outlineRenderer;

    TextMeshPro text = btn.transform.Find("ButtonText").GetComponent<TextMeshPro>();
    if (text != null)
        text.text = btnName;
    else
        Debug.LogError("[TUP] Text not found: " + btnName);

    if (isToggle && Mods.SavedToggleStates.TryGetValue(btnName, out bool savedOn) && savedOn)
    {
        trigger.IsOn = true;
        MenuEffects.SnapActivated(knob, slider, trigger.BodyRenderer, trigger.OutlineRenderer);
    }

    if (btnName.StartsWith("Saved Outfit #") &&
        int.TryParse(btnName.Substring("Saved Outfit #".Length), out int outfitN))
    {
        if (text != null)
            text.text = Mods.GetOutfitDisplayName(outfitN);

        Transform renameSprite = btn.transform.Find("Rename");
        if (renameSprite != null) renameSprite.gameObject.SetActive(true);

        Transform renameCollider = btn.transform.Find("RenameCollider");
        if (renameCollider != null)
        {
            renameCollider.gameObject.SetActive(true);
            renameCollider.gameObject.layer = 2;

            Collider rc = renameCollider.GetComponent<Collider>();
            if (rc == null) rc = renameCollider.gameObject.AddComponent<BoxCollider>();
            rc.isTrigger = true;

            Rigidbody rrb = renameCollider.GetComponent<Rigidbody>();
            if (rrb != null) Destroy(rrb);

            TMP_Text capturedLabel = text;
            int      capturedN     = outfitN;

            ButtonTrigger renameTrigger = renameCollider.gameObject.AddComponent<ButtonTrigger>();
            renameTrigger.BtnIdentifier = btnName + "_Rename";
            renameTrigger.CustomAction  = () => Mods.StartRename(
                capturedN,
                capturedLabel,
                menuObj.transform.Find("Keyboard")?.gameObject
            );
            // No ButtonCollider ? PC raycast finds ButtonTrigger via TryGetComponent;
            // VR ButtonPresser sphere won't accidentally fire rename instead of the main press.
        }
    }

    StartCoroutine(MenuEffects.PopButton(btn, buttonBaseScale, Vector3.zero, true));

    AudioSource audioSource = gameObject.GetComponent<AudioSource>();
    if (audioSource == null)
        audioSource = gameObject.AddComponent<AudioSource>();

    audioSource.volume = 0.1f;
    audioSource.PlayOneShot(btnEnterSound);
    
    btnObjs.Add(btn);
    btnObjs.Add(btnCollider);
    
    InitCycleBtns();
}

    private void InitKeyboard()
    {
        Transform keyboard = menuObj.transform.Find("Keyboard");
        if (keyboard == null)
        {
            Debug.LogError("[TUP] Keyboard not found in menu prefab");
            return;
        }

        keyboard.gameObject.SetActive(false);
        MenuTheme.ApplyKeyboard(keyboard);

        HashSet<Transform> keyRoots = new HashSet<Transform>();
        List<Renderer> keyboardOutlines = GetKeyboardOutlines(keyboard);
        foreach (Transform key in keyboard.GetComponentsInChildren<Transform>(true))
        {
            if (key == keyboard)
                continue;

            string keyName = ResolveKeyboardKey(key);
            if (string.IsNullOrEmpty(keyName))
                continue;

            Transform keyRoot = GetKeyboardKeyRoot(key, keyboard);
            if (keyRoot == null || !keyRoots.Add(keyRoot))
                continue;

            SetLayerRecursive(keyRoot, 2);

            Collider kc = keyRoot.GetComponent<Collider>();
            if (kc == null) kc = keyRoot.gameObject.AddComponent<BoxCollider>();
            kc.isTrigger = true;

            Rigidbody krb = keyRoot.GetComponent<Rigidbody>();
            if (krb != null) Destroy(krb);

            ButtonTrigger trigger = keyRoot.gameObject.AddComponent<ButtonTrigger>();
            trigger.IsKeyboardKey = true;
            trigger.BtnIdentifier = keyName;
            trigger.ButtonRoot = keyRoot;
            MenuTheme.ApplyKeyboardKey(keyRoot, out Renderer bodyRenderer, out Renderer[] outlineRenderers);
            Renderer siblingOutline = FindNearestKeyboardOutline(keyRoot, keyboardOutlines);
            if (siblingOutline != null)
                outlineRenderers = outlineRenderers.Concat(new[] { siblingOutline }).Distinct().ToArray();
            trigger.BodyRenderer = bodyRenderer;
            trigger.OutlineRenderer = outlineRenderers.Length > 0 ? outlineRenderers[0] : null;
            trigger.KeyboardOutlineRenderers = outlineRenderers;
            string captured = keyName;
            trigger.CustomAction = () => HandleKeyInput(captured);

            keyRoot.gameObject.AddComponent<ButtonCollider>().trigger = trigger;
        }
    }

    private static List<Renderer> GetKeyboardOutlines(Transform keyboard)
    {
        List<Renderer> outlines = new List<Renderer>();
        foreach (Transform child in keyboard)
        {
            if (!child.name.Equals("Outline", StringComparison.OrdinalIgnoreCase))
                continue;

            Renderer renderer = MenuTheme.ApplyKeyboardOutline(child);
            if (renderer != null)
                outlines.Add(renderer);
        }

        return outlines;
    }

    private static Renderer FindNearestKeyboardOutline(Transform key, List<Renderer> outlines)
    {
        Renderer nearest = null;
        float nearestDistance = float.MaxValue;
        Vector3 keyPosition = key.localPosition;

        foreach (Renderer outline in outlines)
        {
            if (outline == null)
                continue;

            float distance = (outline.transform.localPosition - keyPosition).sqrMagnitude;
            if (distance >= nearestDistance)
                continue;

            nearestDistance = distance;
            nearest = outline;
        }

        return nearest;
    }

    private static Transform GetKeyboardKeyRoot(Transform key, Transform keyboard)
    {
        if (key == null || keyboard == null)
            return null;

        if (key.GetComponent<TMP_Text>() != null && key.parent != null && key.parent != keyboard)
            return key.parent;

        return key.parent == keyboard || key.GetComponent<Collider>() != null || key.GetComponent<Renderer>() != null
            ? key
            : null;
    }

    private static string ResolveKeyboardKey(Transform key)
    {
        string keyName = NormalizeKeyboardKey(key.name);
        if (!string.IsNullOrEmpty(keyName))
            return keyName;

        TMP_Text text = key.GetComponent<TMP_Text>() ?? key.GetComponentInChildren<TMP_Text>(true);
        return text != null ? NormalizeKeyboardKey(text.text) : "";
    }

    private static string NormalizeKeyboardKey(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        string key = raw.Trim();
        key = key.Replace("\r", "").Replace("\n", "").Trim();
        if (key.Length == 0)
            return "";

        string upper = key.ToUpperInvariant();
        return upper switch
        {
            "ENTER" or "RETURN" => "Enter",
            "DELETE" or "DEL" or "BACKSPACE" => "Delete",
            "CLOSE" or "ESC" or "ESCAPE" => "Close",
            _ when key.Length == 1 => key,
            _ => "",
        };
    }

    private static void SetLayerRecursive(Transform root, int layer)
    {
        root.gameObject.layer = layer;

        foreach (Transform child in root)
            SetLayerRecursive(child, layer);
    }

    public static void ShowKeyboard(GameObject keyboard)
    {
        if (keyboard == null)
            return;

        if (!KeyboardScales.TryGetValue(keyboard, out Vector3 targetScale))
        {
            targetScale = keyboard.transform.localScale;
            KeyboardScales[keyboard] = targetScale;
        }

        StopKeyboardScaleRoutine(keyboard);
        keyboard.SetActive(true);
        keyboard.transform.localScale = Vector3.zero;
        KeyboardScaleRoutines[keyboard] = Instance.StartCoroutine(ScaleKeyboard(keyboard, targetScale, true));
    }

    public static void HideKeyboard(GameObject keyboard)
    {
        if (keyboard == null)
            return;

        if (!KeyboardScales.TryGetValue(keyboard, out Vector3 targetScale))
            targetScale = keyboard.transform.localScale;

        StopKeyboardScaleRoutine(keyboard);
        KeyboardScaleRoutines[keyboard] = Instance.StartCoroutine(ScaleKeyboard(keyboard, targetScale, false));
    }

    private static void StopKeyboardScaleRoutine(GameObject keyboard)
    {
        if (keyboard == null || !KeyboardScaleRoutines.TryGetValue(keyboard, out Coroutine routine) || routine == null)
            return;

        Instance.StopCoroutine(routine);
        KeyboardScaleRoutines.Remove(keyboard);
    }

    private static IEnumerator ScaleKeyboard(GameObject keyboard, Vector3 targetScale, bool show)
    {
        if (keyboard == null)
            yield break;

        keyboard.SetActive(true);
        Vector3 from = show ? Vector3.zero : keyboard.transform.localScale;
        Vector3 to = show ? targetScale : Vector3.zero;

        yield return MenuEffects.PopMenu(keyboard, to, from, show);

        if (keyboard == null)
            yield break;

        keyboard.transform.localScale = show ? targetScale : targetScale;
        if (!show)
            keyboard.SetActive(false);

        KeyboardScaleRoutines.Remove(keyboard);
    }

    private void HandleKeyInput(string key)
    {
        switch (key)
        {
            case "Enter":  Mods.ConfirmRename(); break;
            case "Delete": Mods.DeleteChar();    break;
            case "Close":  Mods.CancelRename();  break;
            default:
                if (key.Length == 1) Mods.TypeChar(key[0]);
                break;
        }
    }

    public void SetMenuSmoothing(bool smooth, float strength = 30f)
    {
        menuSmoothingEnabled = smooth;
        menuSmoothingStrength = Mathf.Max(1f, strength);

        SmoothFollowMenu sf = menuObj?.GetComponent<SmoothFollowMenu>();
        if (sf != null)
            sf.Smoothing = smooth ? menuSmoothingStrength : 1000f;
    }

    public void SetMenuScale(float scale)
    {
        menuScale = Mathf.Clamp(scale, 0.2f, 0.6f);

        if (menuObj != null && menuObj.activeSelf)
        {
            if (menuScaleRoutine != null)
                StopCoroutine(menuScaleRoutine);

            menuScaleRoutine = StartCoroutine(SmoothMenuScale(menuScale));
        }
    }

    private IEnumerator SmoothMenuScale(float targetScale)
    {
        if (menuObj == null)
        {
            menuScaleRoutine = null;
            yield break;
        }

        Vector3 startScale = menuObj.transform.localScale;
        Vector3 endScale = Vector3.one * targetScale;
        const float duration = 0.2f;
        float elapsed = 0f;

        while (elapsed < duration && menuObj != null && menuObj.activeSelf)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            menuObj.transform.localScale = Vector3.LerpUnclamped(startScale, endScale, t);
            yield return null;
        }

        if (menuObj != null && menuObj.activeSelf)
            menuObj.transform.localScale = endScale;

        menuScaleRoutine = null;
    }

    public void SetHeadDistance(float distance)
    {
        headMenuDistance = Mathf.Clamp(distance, 0.35f, 1.0f);

        SmoothFollowMenu sf = menuObj?.GetComponent<SmoothFollowMenu>();
        if (sf != null && currentOpenType == MenuOpenType.Head)
            sf.LocalPosition = new Vector3(-0.03f, -0.02f, headMenuDistance);
    }

    public void PlayBtnCickSound()
    {
        AudioSource audioSource = gameObject.GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.PlayOneShot(currentClickSound);
    }

    private void DestroyButtons()
    {
        foreach (GameObject btnObj in btnObjs)
            Destroy(btnObj);
        btnObjs.Clear();
    }

    private void LoadBundles()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        foreach (var name in assembly.GetManifestResourceNames())
            Debug.Log("[TUP RESOURCE] " + name);
        
        buttonBundle             = LoadBundle(assembly, "ThatUtilsPad.Assets.Models.buttonmodel-rename");
        menuReduxBundle          = LoadBundle(assembly, "ThatUtilsPad.Assets.Models.tup-overv9");
        notifBundle              = LoadBundle(assembly, "ThatUtilsPad.Assets.UI.notifpls");
        Mods.nameBundle          = LoadBundle(assembly, "ThatUtilsPad.Assets.UI.tup-nametagui");
    }

    private AssetBundle LoadBundle(Assembly assembly, string resourceName)
    {
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        byte[] buffer = new byte[stream.Length];
        stream.Read(buffer, 0, buffer.Length);
        return AssetBundle.LoadFromMemory(buffer);
    }

    public static Camera GetActiveCamera() => ThirdPersonCamera.gameObject.activeInHierarchy
                                                      ? ThirdPersonCamera
                                                      : FirstPersonCamera;

    private enum MenuOpenType { Hand, Head }
}

public static class WavUtility
{
    public static AudioClip ToAudioClip(byte[] fileBytes, string name = "wav")
    {
        int channels   = BitConverter.ToInt16(fileBytes, 22);
        int sampleRate = BitConverter.ToInt32(fileBytes, 24);
        int bitDepth   = BitConverter.ToInt16(fileBytes, 34);

        int dataIndex = 12;
        while (dataIndex < fileBytes.Length - 8)
        {
            string chunkId = System.Text.Encoding.ASCII.GetString(fileBytes, dataIndex, 4);
            int chunkSize  = BitConverter.ToInt32(fileBytes, dataIndex + 4);
            if (chunkId == "data") break;
            dataIndex += 8 + chunkSize;
        }
        dataIndex += 8;

        int     sampleCount = (fileBytes.Length - dataIndex) / (bitDepth / 8);
        float[] data        = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            int offset = dataIndex + i * (bitDepth / 8);
            data[i] = bitDepth == 16
                ? BitConverter.ToInt16(fileBytes, offset) / 32768f
                : (fileBytes[offset] - 128) / 128f;
        }

        AudioClip clip = AudioClip.Create(name, sampleCount / channels, channels, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
