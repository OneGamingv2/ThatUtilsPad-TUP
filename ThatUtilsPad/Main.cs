using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
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
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace ThatUtilsPad;

[BepInPlugin("that.utils.pad", "ThatUtilsPad", "1.0.0")]
public class Main : BaseUnityPlugin
{
    private const string AdminsUrl      = "https://playfabswapping.hu/admin/admins.json";

    private const bool UseSakuraTheme   = true;
    private const bool AlwaysShowMenu   = false;
    private const bool toggleMenuMethod = true;
    private bool       menuInitialized  = false;
    private bool       modInitialized   = false;
    private int        currentPage      = 0;
    private const int  PageSize         = 7;
    private static readonly MenuThemePalette Theme = MenuTheme.Themes.Sakura;

    public static Main Instance;
    public bool   IsAdmin;
    public string AdminName = "";


    private readonly List<GameObject> btnObjs     = [];
    private readonly List<GameObject> selectorBtnObjs = new List<GameObject>();
    private readonly List<GameObject> navBtnObjs      = new List<GameObject>();
    private readonly List<GameObject> spotifyHudButtonColliders = new List<GameObject>();
    private GameObject spotifyHudPage;
    private TMP_Text spotifySongText;
    private TMP_Text spotifyArtistText;
    private TMP_Text spotifyDurationText;
    private TMP_Text spotifyStatusText;
    private TMP_Text spotifyHintText;
    private Transform spotifyPlayIcon;
    private Transform spotifyPauseIcon;
    private bool spotifyHudWasVisible;
    private Coroutine spotifyHudWaveRoutine;
    private readonly Dictionary<Transform, Vector3> spotifyHudScales = new Dictionary<Transform, Vector3>();
    private RawImage spotifyAlbumIcon;
    private Image spotifyAlbumImage;
    private Sprite spotifyAlbumSprite;
    private Renderer spotifyAlbumRenderer;
    private Material spotifyAlbumMaterial;
    private int spotifyAlbumThumbnailHash;
    private readonly object spotifyAlbumWebLock = new object();
    private bool spotifyAlbumWebQueryRunning;
    private string spotifyAlbumWebRequestedKey = "";
    private string spotifyAlbumWebBytesKey = "";
    private string spotifyAlbumWebTextureKey = "";
    private byte[] spotifyAlbumWebBytes;
    private Texture2D spotifyAlbumWebTexture;
    private string spotifyDisplayedAlbumKey = "";
    private Texture2D spotifyDisplayedAlbumTexture;
    private Coroutine spotifyAlbumTransitionRoutine;
    private string spotifyAlbumTransitionTargetKey = "";
    private const float SpotifyAlbumFadeSeconds = 0.5f;
    private const float SpotifyAlbumBlackHoldSeconds = 0.5f;
    private Image spotifyProgressFill;
    private float nextSpotifyHudRefresh;
    private Vector3 spotifySongBaseLocalPosition;
    private Color spotifySongBaseColor = Color.white;
    private Mods.SpotifyTrackInfo spotifyLastGoodInfo;
    private bool spotifyHasLastGoodInfo;
    private string spotifyDisplayedSong = "";
    private string spotifyPendingSong = "";
    private string spotifyDisplayedArtist = "";
    private string spotifyPendingArtist = "";
    private Color spotifyArtistBaseColor = Color.white;
    private string spotifyMarqueeText = "";
    private float spotifySongMarqueeOffset;
    private float spotifySongMarqueeResetWidth = 0.05f;
    private float spotifySongMarqueeTimer;
    private const float SpotifySongMarqueeSpeed = 4.8f;
    private const string SpotifySongMarqueeGap = "   ";
    private const int SpotifySongMarqueeCopies = 99;
    private const float SpotifySongFixedY = -10.3f;
    private const float SpotifySongFixedZ = -0.1f;
    private const float SpotifySongVisibleSeconds = 5f;
    private const float SpotifySongFadeSeconds = 0.5f;
    private const float SpotifySongHiddenSeconds = 0.5f;

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
    private TMP_Text keyboardPreviewText;
    private Coroutine keyboardPreviewCharRoutine;

    private readonly Vector3 menuGripPosition = new(0f, -0.17f, 0f);
    private readonly Vector3 menuHandOffset   = new(0f,  0f,    0f);

    private GameObject  btnPrefab;
    private Transform runtimeButtonsHolder;
    private GameObject runtimeButtonTemplate;
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
    public AudioClip    notifSound;
    private bool        isMenuOpened = false;
    private bool        isMenuClosing = false;

    private AssetBundle menuBundle;
    private float       menuGripRotaton = 45f;
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
    private GameObject leftKeyboardSelectionObj;
    private float selectorCooldown = 0.2f;
    private float lastSelectorPress = 0f;

    private string currentCategory = "Networking";
    
    private AudioClip  startSound;
    private GameObject stumpInfo;
    
    private TMP_Text nameTextComp;
    private TMP_Text fpsTextComp;
    private TMP_Text fpsPingTextComp;
    private TMP_Text platformTextComp;
    private TMP_Text trustTextComp;
    private TMP_Text pingTextComp;
    private TMP_Text colorTextComp;
    private TMP_Text dateTextComp;
    private TMP_Text modsTextComp;
    private TMP_Text cheatsTextComp;
    private TMP_Text modsCountTextComp;
    private TMP_Text cheatsCountTextComp;
    private GameObject[] modsTileObjs = Array.Empty<GameObject>();
    private TMP_Text[] modsTileTextComps = Array.Empty<TMP_Text>();
    private GameObject[] cheatsTileObjs = Array.Empty<GameObject>();
    private TMP_Text[] cheatsTileTextComps = Array.Empty<TMP_Text>();
    private CheckerTileGroup modsTileGroup;
    private CheckerTileGroup cheatsTileGroup;
    private RectTransform volumeInnerRect;
    private Coroutine volumeInnerRoutine;
    private const float VolumeDisplayMaxWidth = 406.54f;
    private const float VolumeDisplayTweenDuration = 0.5f;

    private sealed class CheckerTileGroup
    {
        public string TitleName;
        public GameObject[] Tiles = Array.Empty<GameObject>();
        public TMP_Text[] Texts = Array.Empty<TMP_Text>();
        public RectTransform[] TileRects = Array.Empty<RectTransform>();
        public GameObject[] BackObjs = Array.Empty<GameObject>();
        public RectTransform[] BackRects = Array.Empty<RectTransform>();
        public GameObject NoneTitle;
        public RectTransform NoneRect;
        public TMP_Text NoneText;
        public string[] Values = Array.Empty<string>();
        public Coroutine Routine;
        public int FocusedIndex = -1;
    }
    
    public static TMP_Text queueText;
    public static TMP_Text modeText;
    public static TMP_Text regionText;
    public static TMP_Text clickSoundText;
    public static TMP_Text smoothingText;
    public static TMP_Text smoothingStrengthText;
    public static TMP_Text menuScaleText;
    public static TMP_Text headDistanceText;
    public static TMP_Text doubleOpenText;
    public static TMP_Text openBindText;
    public static TMP_Text themeText;
    public static TMP_Text scanModeText;
    public static TMP_Text nameTagSizeText;
    public static TMP_Text nameTagFadeDistanceText;
    public static TMP_Text speedStrictnessText;
    
    public static TMP_Text volumeText;

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
        InitializeMod();
    }


    private void InitializeMod()
    {
        if (modInitialized) return;
        modInitialized = true;
        Debug.Log("[TUP] loading mod");
        PlayStartSound();
        CheckAdminStatus();
        Mods.Init();
        LoadBundles();
        FontCache.LoadFonts();
        ShaderCache.Init();
        new GameObject("TUP_CoroutineHandler").AddComponent<CoroutineHandler>();
        foreach (var n in buttonBundle.GetAllAssetNames()) Debug.Log("[TUP BUNDLE] " + n);
        btnPrefab = buttonBundle.LoadAsset<GameObject>("assets/prefabs/buttonmodelui2.prefab");
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
        if (queueTextTransform != null && queueTextTransform.TryGetComponent(out TMP_Text qText))
        {
            queueText = qText;
            queueText.text = "Queue  :  " + gorillaComputer.currentQueue;
        }
        Transform queueSliderTransform = FindButtonChild(menuObj.transform.Find("Queue"), "Slider");
        Transform queueArrowTransform = FindButtonChild(menuObj.transform.Find("Queue"), "NextArrow");

        if (queueSliderTransform != null && queueArrowTransform != null)
        {
            queueSliderTransform.transform.gameObject.SetActive(false);
            queueArrowTransform.transform.gameObject.SetActive(true);
        }
        

        Transform modeTransform = menuObj.transform.Find("Mode/ButtonText");
        if (modeTransform != null && modeTransform.TryGetComponent(out TMP_Text mText))
        {
            modeText = mText;
            modeText.text = "Mode  :  " + gorillaComputer.currentGameMode.ToString();
        }
        Transform modeSliderTransform = FindButtonChild(menuObj.transform.Find("Mode"), "Slider");
        Transform modeArrowTransform = FindButtonChild(menuObj.transform.Find("Mode"), "NextArrow");

        if (modeSliderTransform != null && modeArrowTransform != null)
        {
            modeSliderTransform.transform.gameObject.SetActive(false);
            modeArrowTransform.transform.gameObject.SetActive(true);
        }

        InitCycleButtonText("Region", ref regionText, "Region  :  " + Mods.GetRegionLabel());
        
        
        Transform clickSoundTransform = menuObj.transform.Find("Click Sound/ButtonText");
        if (clickSoundTransform != null && clickSoundTransform.TryGetComponent(out TMP_Text csText))
        {
            clickSoundText = csText;
            string rawName   = Mods.clickSounds.Count > 0 ? Mods.clickSounds[Mods.currentClickIndex].Name : "-";
            string soundName = rawName.Length > 0 ? char.ToUpper(rawName[0]) + rawName.Substring(1).ToLower() : "-";
            clickSoundText.text = "Click Sound  :  " + soundName;
        }
        Transform clickSoundSliderTransform = FindButtonChild(menuObj.transform.Find("Click Sound"), "Slider");
        Transform clickSoundArrowTransform = FindButtonChild(menuObj.transform.Find("Click Sound"), "NextArrow");

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
        InitCycleButtonText("Scan Mode", ref scanModeText, "Scan Mode  :  " + Mods.GetScanModeLabel());
        InitCycleButtonText("Nametag Size", ref nameTagSizeText, "Nametag Size  :  " + Mods.GetNameTagSizeLabel());
        InitCycleButtonText("Nametag Fade Distance", ref nameTagFadeDistanceText, "Nametag Fade Distance  :  " + Mods.GetNameTagFadeDistanceLabel());
        InitCycleButtonText("Speed Strictness", ref speedStrictnessText, "Speed Strictness  :  " + Mods.GetSpeedStrictnessLabel());
        Mods.UpdateSettingsLabels();
    }

    private static Transform FindButtonChild(Transform button, string path)
    {
        if (button == null || string.IsNullOrEmpty(path))
            return null;

        Transform direct = button.Find(path);
        if (direct != null)
            return direct;

        Transform underMainUi = button.Find("MainUI/" + path);
        if (underMainUi != null)
            return underMainUi;

        string leaf = path.Contains("/") ? path.Substring(path.LastIndexOf('/') + 1) : path;
        return FindChildByName(button, leaf);
    }
    private void InitCycleButtonText(string buttonName, ref TMP_Text target, string defaultText)
    {
        Transform button = menuObj != null ? FindChildByName(menuObj.transform, buttonName) : null;
        TMP_Text text = button != null ? GetButtonTitle(button) : null;
        if (text != null)
        {
            target = text;
            target.text = defaultText;
        }

        Transform sliderTransform = FindButtonChild(button, "Slider");
        Transform arrowTransform = FindButtonChild(button, "NextArrow");
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
        Mods.NameTagsLoop();
        Mods.AntiCheatLoop();
        Mods.AutoScanLoop();
        UpdateSpotifyHudLoop();
        
        bool currentControllerState     = IsMenuOpenBindPressed();
        bool controllerPressedThisFrame = currentControllerState && !previousControllerState;
        previousControllerState = currentControllerState;

        bool keyboardPressedThisFrame = Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame;
        bool keyboardHeld             = Keyboard.current != null && Keyboard.current.tKey.isPressed;

        if (isMenuClosing)
            return;

        if (true)
        {
            if (toggleMenuMethod && !AlwaysShowMenu)
            {
                bool togglePressed = ConsumeMenuOpenBindPress(controllerPressedThisFrame) || keyboardPressedThisFrame;

                if (togglePressed)
                {
                    if (isMenuOpened)
                    {
                        if (Mods.IsRenaming)
                        {
                            Mods.CancelRename();
                        }
                        else
                        {
                            StartCoroutine(CloseMenu());
                            isMenuOpened = false;
                        }
                    }
                    else
                    {
                        currentOpenType = currentControllerState ? MenuOpenType.Hand : MenuOpenType.Head;
                        isMenuOpened = OpenMenu();
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
                    isMenuOpened = OpenMenu();
                }
                else if (isMenuOpened && !controllerHeld && !keyboardIsHeld && !AlwaysShowMenu)
                {
                    if (Mods.IsRenaming)
                    {
                        Mods.CancelRename();
                    }
                    else
                    {
                        StartCoroutine(CloseMenu());
                        isMenuOpened = false;
                    }
                }
            }
        }

        if (!isMenuOpened)
            return;

        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
            return;

        Camera raycastCamera = GetActiveCamera();
        Ray    ray           = raycastCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        RaycastHit[] menuHits = Physics.RaycastAll(ray, 100f, 1 << 2, QueryTriggerInteraction.Collide);
        if (menuHits == null || menuHits.Length == 0)
            return;

        RaycastHit hit = menuHits
            .OrderBy(x => x.distance)
            .FirstOrDefault(x => x.collider != null && IsInputColliderObject(x.collider) && x.collider.GetComponent<ButtonHoverCollider>() == null);
        if (hit.collider == null)
        {
            Debug.Log("[TUP PC HIT] hover-only hit ignored");
            return;
        }

        StartCoroutine(MenuEffects.SpawnHitCircle(hit.point, hit.transform));

        VolumeSliderCollider slider = IsInputColliderObject(hit.collider) ? hit.collider.GetComponent<VolumeSliderCollider>() : null;
        if (slider != null)
        {
            slider.SetFromWorldPoint(hit.point);
            return;
        }

        Debug.Log($"[TUP PC HIT] {hit.collider.name} path={GetTransformPath(hit.collider.transform)} layer={hit.collider.gameObject.layer}");

        if (!hit.collider.TryGetComponent(out ButtonTrigger buttonTrigger))
            buttonTrigger = hit.collider.GetComponentInParent<ButtonTrigger>();
        if (buttonTrigger == null)
        {
            ButtonCollider buttonCollider = hit.collider.GetComponent<ButtonCollider>() ?? hit.collider.GetComponentInParent<ButtonCollider>();
            buttonTrigger = buttonCollider != null ? buttonCollider.trigger : null;
        }

        if (buttonTrigger != null)
            ButtonTrigger.PcPress(buttonTrigger);
        else
            Debug.LogWarning("[TUP PC HIT] no ButtonTrigger/ButtonCollider trigger for " + hit.collider.name);
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
            "Left:JoystickButton" => IsControllerInputSystemButtonPressed(true, "primary2daxisclick", "thumbstickclicked", "joystickclick", "stickclick"),
            "Left:SecondaryButton" => poller.leftControllerSecondaryButton,
            "Left:PrimaryButton" => poller.leftControllerPrimaryButton,
            "Left:TriggerButton" => poller.leftControllerTriggerButton || poller.leftControllerIndexFloat > 0.75f,
            "Left:GripButton" => poller.leftControllerGripFloat > 0.75f,
            "Right:JoystickButton" => IsControllerInputSystemButtonPressed(false, "primary2daxisclick", "thumbstickclicked", "joystickclick", "stickclick"),
            "Right:SecondaryButton" => poller.rightControllerSecondaryButton,
            "Right:PrimaryButton" => poller.rightControllerPrimaryButton,
            "Right:TriggerButton" => poller.rightControllerTriggerButton || poller.rightControllerIndexFloat > 0.75f,
            "Right:GripButton" => poller.rightControllerGripFloat > 0.75f,
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
            "Left:JoystickButton" => "Left Joystick",
            "Left:SecondaryButton" => "Left Secondary",
            "Left:PrimaryButton" => "Left Primary",
            "Left:TriggerButton" => "Left Trigger",
            "Left:GripButton" => "Left Grip",
            "Right:JoystickButton" => "Right Joystick",
            "Right:SecondaryButton" => "Right Secondary",
            "Right:PrimaryButton" => "Right Primary",
            "Right:TriggerButton" => "Right Trigger",
            "Right:GripButton" => "Right Grip",
            _ => "Left Secondary"
        };
    }

    private static bool IsInputSystemButtonPressed(string path)
    {
        UnityEngine.InputSystem.InputControl control = InputSystem.FindControl(path);
        return control is UnityEngine.InputSystem.Controls.ButtonControl button && button.isPressed;
    }
    private static bool IsControllerInputSystemButtonPressed(bool leftHand, params string[] controlNames)
    {
        foreach (UnityEngine.InputSystem.InputDevice device in InputSystem.devices)
        {
            if (device == null || device is Keyboard || device is Mouse)
                continue;

            bool matchesHand = device.usages.Any(usage => usage.ToString().IndexOf(leftHand ? "LeftHand" : "RightHand", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!matchesHand)
                continue;

            foreach (UnityEngine.InputSystem.InputControl control in device.allControls)
            {
                if (control is not UnityEngine.InputSystem.Controls.ButtonControl button || !button.isPressed)
                    continue;

                string name = (button.name ?? "").Replace(" ", "").ToLowerInvariant();
                string path = (button.path ?? "").Replace(" ", "").ToLowerInvariant();
                foreach (string controlName in controlNames)
                {
                    string normalized = (controlName ?? "").Replace(" ", "").ToLowerInvariant();
                    if (name.Contains(normalized) || path.Contains(normalized))
                        return true;
                }
            }
        }

        string hand = leftHand ? "LeftHand" : "RightHand";
        return IsInputSystemButtonPressed("<XRController>{" + hand + "}/primary2DAxisClick") ||
               IsInputSystemButtonPressed("<XRController>{" + hand + "}/thumbstickClicked");
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
        "Left:JoystickButton",
        "Left:SecondaryButton",
        "Left:PrimaryButton",
        "Left:TriggerButton",
        "Left:GripButton",
        "Right:JoystickButton",
        "Right:SecondaryButton",
        "Right:PrimaryButton",
        "Right:TriggerButton",
        "Right:GripButton",
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
            ? "assets/prefabs/TUP-overv15.prefab"
            : "assets/prefabs/tup-modelsmooth.prefab";

        AssetBundle bundle = UseSakuraTheme ? menuReduxBundle : menuBundle;
        GameObject  prefab = bundle.LoadAsset<GameObject>(prefabPath);
        
        
        menuObj = Instantiate(prefab);
        menuObj.transform.localScale = Vector3.one * menuScale;
        
        volumeText = menuObj.transform.Find("SideHolder/VolumeControls/Main/Volume")?.GetComponent<TMP_Text>()
                     ?? menuObj.transform.Find("SideHolder/VolumePercent")?.GetComponent<TMP_Text>();
        PrepareRuntimeButtonTemplate();
        
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
        InitSpotifyHudPage(menuObj);
        InitKeyboard();
        HideAllColliderDebugVisuals();

        menuObj.SetActive(false);
        menuInitialized = true;
        StartCoroutine(RoomInfoUpdater());
    }
    
    private bool OpenMenu()
{
    if (!menuInitialized || menuObj == null || isMenuClosing)
        return false;

    Transform parent = currentOpenType == MenuOpenType.Head  
        ? GetActiveCamera().transform
        : GTPlayer.Instance.LeftHand.controllerTransform;

    menuObj.transform.SetParent(null);

    SmoothFollowMenu smooth = menuObj.GetComponent<SmoothFollowMenu>() ?? menuObj.AddComponent<SmoothFollowMenu>();
    smooth.YawOnly = false;
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
        smooth.LocalRotation = Quaternion.Euler(270f, 180f, 0f);
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
            "FPS/Ping",
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
            "ModsTitle",
            "CheatsTitle",
            "AddToSaved",
            "SavedPlayers",
            "Saved Players"
        };

        foreach (string part in checkerParts)
        {
            Transform t = sideHolder.Find(part) ?? FindChildByName(sideHolder, part);
            if (t != null)
                t.localScale = Vector3.zero;
        }
    }
    
    foreach (GameObject selectorObj in selectorBtnObjs)
    {
        selectorObj.SetActive(true);
        LogSelectorButtonVisualState(selectorObj.transform, "open");
    }

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
    return true;
}
    

    private TMP_Text FindText(GameObject obj, string parentName)
    {
        Transform parent = obj.transform.Find(parentName) ?? FindChildByName(obj.transform, parentName);
        if (parent == null)
        {
            Debug.LogError($"[TUP] Missing parent: {parentName}");
            return null;
        }

        TMP_Text tmp = parent.GetComponentInChildren<TMP_Text>(true);
        if (tmp == null)
            Debug.LogError($"[TUP] No TMP_Text under: {parentName}");

        return tmp;
    }

    private TMP_Text FindTextPath(Transform root, string path)
    {
        Transform target = root.Find(path);
        if (target == null)
        {
            string[] parts = path.Split('/' );
            target = root;
            foreach (string part in parts)
            {
                target = FindChildByName(target, part);
                if (target == null) break;
            }
        }

        if (target == null)
        {
            Debug.LogError("[TUP] Missing checker text path: " + path);
            return null;
        }

        TMP_Text tmp = target.GetComponentInChildren<TMP_Text>(true);
        if (tmp == null)
            Debug.LogError("[TUP] No TMP_Text under checker path: " + path);
        return tmp;
    }


    private TMP_Text FindCheckerTextAny(Transform root, params string[] names)
    {
        if (root == null)
            return null;

        foreach (string name in names)
        {
            Transform target = root.Find(name) ?? FindChildByName(root, name);
            TMP_Text text = target != null ? target.GetComponent<TMP_Text>() ?? target.GetComponentInChildren<TMP_Text>(true) : null;
            if (text != null)
                return text;
        }

        Debug.LogError("[TUP] Missing checker FPS/Ping text");
        return null;
    }

    private static void EnsureCheckerTextVisible(TMP_Text text)
    {
        if (text == null)
            return;

        text.gameObject.SetActive(true);
        text.enabled = true;
        Color color = text.color;
        if (color.a <= 0.01f)
        {
            color.a = 1f;
            text.color = color;
        }
    }
    private void InitCheckerTileGroup(Transform side, string titleName, out GameObject[] tileObjs, out TMP_Text[] tileTexts, TMP_FontAsset font)
    {
        string[] tileNames = { "Tile", "Tile2", "Tile3" };
        CheckerTileGroup group = new CheckerTileGroup
        {
            TitleName = titleName,
            Tiles = new GameObject[tileNames.Length],
            Texts = new TMP_Text[tileNames.Length],
            TileRects = new RectTransform[tileNames.Length],
            BackObjs = new GameObject[tileNames.Length],
            BackRects = new RectTransform[tileNames.Length]
        };

        tileObjs = group.Tiles;
        tileTexts = group.Texts;

        Transform title = FindChildByName(side, titleName);
        Transform main = side.Find(titleName + "/Main") ?? FindChildByName(title, "Main");
        if (main == null)
        {
            Debug.LogError("[TUP] Missing checker tile holder: " + titleName + "/Main");
            return;
        }

        Transform noneTitle = main.Find("NoneTitle") ?? main.Find("NoneTile") ?? FindChildByName(main, "NoneTitle") ?? FindChildByName(main, "NoneTile");
        if (noneTitle != null)
        {
            group.NoneTitle = noneTitle.gameObject;
            group.NoneRect = noneTitle as RectTransform ?? noneTitle.GetComponent<RectTransform>();
            group.NoneText = noneTitle.GetComponent<TMP_Text>() ?? noneTitle.GetComponentInChildren<TMP_Text>(true);
            if (group.NoneText != null && font != null)
                group.NoneText.font = font;
            group.NoneTitle.SetActive(false);
            SetRectWidth(group.NoneRect, 0f);
        }

        for (int i = 0; i < tileNames.Length; i++)
        {
            Transform tile = main.Find(tileNames[i]) ?? FindChildByName(main, tileNames[i]);
            if (tile == null)
            {
                Debug.LogError("[TUP] Missing checker tile: " + titleName + "/Main/" + tileNames[i]);
                continue;
            }

            int index = i;
            group.Tiles[i] = tile.gameObject;
            group.TileRects[i] = tile as RectTransform ?? tile.GetComponent<RectTransform>();
            group.Texts[i] = tile.GetComponentInChildren<TMP_Text>(true);
            if (group.Texts[i] != null && font != null)
                group.Texts[i].font = font;

            Transform back = tile.Find("Back") ?? FindChildByName(tile, "Back");
            if (back != null)
            {
                group.BackObjs[i] = back.gameObject;
                group.BackRects[i] = back as RectTransform ?? back.GetComponent<RectTransform>();
                back.gameObject.SetActive(false);
                SetRectWidth(group.BackRects[i], 0f);
            if (group.BackObjs[i] != null) group.BackObjs[i].transform.localScale = Vector3.zero;
                Transform backCollider = back.Find("Collider") ?? FindChildByName(back, "Collider");
                if (backCollider != null)
                    WireInteractive(back, titleName + "_TileBack_" + i, () => UnfocusCheckerTile(group), backCollider);
            }

            Transform tileCollider = tile.Find("Collider") ?? FindChildByName(tile, "Collider");
            if (tileCollider != null)
                WireInteractive(tile, titleName + "_TileFocus_" + i, () => FocusCheckerTile(group, index), tileCollider);

            SetRectWidth(group.TileRects[i], CheckerTileDefaultWidth);
            tile.gameObject.SetActive(false);
        }

        if (titleName.Equals("ModsTitle", StringComparison.OrdinalIgnoreCase))
            modsTileGroup = group;
        else if (titleName.Equals("CheatsTitle", StringComparison.OrdinalIgnoreCase))
            cheatsTileGroup = group;
    }

    private void SetCheckerTiles(CheckerTileGroup group, string[] values)
    {
        if (group == null)
            return;

        values ??= Array.Empty<string>();
        group.Values = values;
        group.FocusedIndex = -1;
        if (group.Routine != null)
            StopCoroutine(group.Routine);
        group.Routine = StartCoroutine(ApplyCheckerTiles(group));
    }

    private IEnumerator ApplyCheckerTiles(CheckerTileGroup group)
    {
        bool any = group.Values.Length > 0;
        if (group.NoneTitle != null)
        {
            group.NoneTitle.SetActive(!any);
            if (!any)
            {
                if (group.NoneText != null)
                    group.NoneText.text = "None";
                yield return AnimateRectWidth(group.NoneRect, GetRectWidth(group.NoneRect), CheckerTileExpandedWidth, CheckerTileDuration, false);
            }
            else
            {
                SetRectWidth(group.NoneRect, 0f);
                group.NoneTitle.SetActive(false);
            }
        }

        for (int i = 0; i < group.Tiles.Length; i++)
        {
            bool visible = i < group.Values.Length;
            if (group.Tiles[i] != null)
                group.Tiles[i].SetActive(visible);
            if (group.BackObjs[i] != null)
                group.BackObjs[i].SetActive(false);
            SetRectWidth(group.BackRects[i], 0f);
            if (group.BackObjs[i] != null) group.BackObjs[i].transform.localScale = Vector3.zero;
            if (visible)
            {
                SetRectWidth(group.TileRects[i], CheckerTileDefaultWidth);
                if (group.Texts[i] != null)
                    group.Texts[i].text = ShortTileName(group.Values[i]);
            }
            else
            {
                SetRectWidth(group.TileRects[i], 0f);
            }
        }
    }

    private void FocusCheckerTile(CheckerTileGroup group, int index)
    {
        if (group == null || index < 0 || index >= group.Values.Length || group.FocusedIndex == index)
            return;

        if (group.Routine != null)
            StopCoroutine(group.Routine);
        group.Routine = StartCoroutine(FocusCheckerTileRoutine(group, index));
    }

    private IEnumerator FocusCheckerTileRoutine(CheckerTileGroup group, int index)
    {
        group.FocusedIndex = index;
        for (int i = 0; i < group.Tiles.Length; i++)
        {
            if (i >= group.Values.Length || group.Tiles[i] == null)
                continue;

            if (i != index)
                StartCoroutine(AnimateRectWidth(group.TileRects[i], GetRectWidth(group.TileRects[i]), 0f, CheckerTileDuration, true));
        }

        yield return AnimateRectWidth(group.TileRects[index], GetRectWidth(group.TileRects[index]), CheckerTileExpandedWidth, CheckerTileDuration, false);
        if (group.Texts[index] != null)
            yield return AnimateCheckerTextWave(group.Texts[index], group.Values[index]);

        if (group.BackObjs[index] != null)
        {
            group.BackObjs[index].SetActive(true);
            SetRectWidth(group.BackRects[index], CheckerTileExpandedWidth);
            group.BackObjs[index].transform.localScale = Vector3.zero;
            yield return AnimateLocalScale(group.BackObjs[index].transform, Vector3.zero, CheckerBackTargetScale, CheckerTileDuration);
        }
    }

    private void UnfocusCheckerTile(CheckerTileGroup group)
    {
        if (group == null || group.FocusedIndex < 0)
            return;

        if (group.Routine != null)
            StopCoroutine(group.Routine);
        group.Routine = StartCoroutine(UnfocusCheckerTileRoutine(group));
    }

    private IEnumerator UnfocusCheckerTileRoutine(CheckerTileGroup group)
    {
        int focused = group.FocusedIndex;
        group.FocusedIndex = -1;

        if (focused >= 0 && focused < group.BackObjs.Length && group.BackObjs[focused] != null)
        {
            yield return AnimateLocalScale(group.BackObjs[focused].transform, group.BackObjs[focused].transform.localScale, Vector3.zero, CheckerTileDuration);
            group.BackObjs[focused].SetActive(false);
            SetRectWidth(group.BackRects[focused], 0f);
        }

        for (int i = 0; i < group.Tiles.Length; i++)
        {
            if (i >= group.Values.Length || group.Tiles[i] == null)
                continue;

            group.Tiles[i].SetActive(true);
            if (group.Texts[i] != null)
                group.Texts[i].text = ShortTileName(group.Values[i]);
            StartCoroutine(AnimateRectWidth(group.TileRects[i], GetRectWidth(group.TileRects[i]), CheckerTileDefaultWidth, CheckerTileDuration, true));
        }
    }

    private const float CheckerTileDefaultWidth = 394.8f;
    private const float CheckerTileExpandedWidth = 1263.3f;
    private const float CheckerTileDuration = 0.5f;
    private static readonly Vector3 CheckerBackTargetScale = new Vector3(0.6030598f, 0.6030598f, 0.6030598f);

    private static IEnumerator AnimateRectWidth(RectTransform rect, float from, float to, float duration, bool disableAtZero)
    {
        if (rect == null)
            yield break;

        GameObject owner = rect.gameObject;
        owner.SetActive(true);
        for (float time = 0f; time < duration; time += Time.deltaTime)
        {
            float p = EaseOutQuad(Mathf.Clamp01(time / duration));
            SetRectWidth(rect, Mathf.LerpUnclamped(from, to, p));
            yield return null;
        }

        SetRectWidth(rect, to);
        if (disableAtZero && to <= 0.001f)
            owner.SetActive(false);
    }

    private static IEnumerator AnimateCheckerTextWave(TMP_Text text, string value)
    {
        if (text == null)
            yield break;

        text.text = value ?? string.Empty;
        text.ForceMeshUpdate();
        TMP_TextInfo info = text.textInfo;
        int charCount = info.characterCount;
        if (charCount == 0)
            yield break;

        Vector3[][] baseVertices = new Vector3[info.meshInfo.Length][];
        for (int i = 0; i < info.meshInfo.Length; i++)
            baseVertices[i] = (Vector3[])info.meshInfo[i].vertices.Clone();

        float total = (charCount - 1) * 0.1f + 0.25f;
        for (float time = 0f; time < total; time += Time.deltaTime)
        {
            for (int m = 0; m < info.meshInfo.Length; m++)
                Array.Copy(baseVertices[m], info.meshInfo[m].vertices, baseVertices[m].Length);

            for (int c = 0; c < charCount; c++)
            {
                TMP_CharacterInfo ch = info.characterInfo[c];
                if (!ch.isVisible)
                    continue;

                float p = EaseOutQuad(Mathf.Clamp01((time - c * 0.1f) / 0.25f));
                int mat = ch.materialReferenceIndex;
                int vert = ch.vertexIndex;
                Vector3[] verts = info.meshInfo[mat].vertices;
                Vector3 center = (baseVertices[mat][vert] + baseVertices[mat][vert + 2]) * 0.5f;
                for (int v = 0; v < 4; v++)
                    verts[vert + v] = center + (baseVertices[mat][vert + v] - center) * p;
            }

            for (int m = 0; m < info.meshInfo.Length; m++)
                text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
            yield return null;
        }

        for (int m = 0; m < info.meshInfo.Length; m++)
        {
            Array.Copy(baseVertices[m], info.meshInfo[m].vertices, baseVertices[m].Length);
            text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
        }
    }

    private static IEnumerator AnimateLocalScale(Transform target, Vector3 from, Vector3 to, float duration)
    {
        if (target == null)
            yield break;

        target.localScale = from;
        for (float time = 0f; time < duration; time += Time.deltaTime)
        {
            float p = EaseOutQuad(Mathf.Clamp01(time / duration));
            target.localScale = Vector3.LerpUnclamped(from, to, p);
            yield return null;
        }

        target.localScale = to;
    }
    private static float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);

    private static float GetRectWidth(RectTransform rect) => rect != null ? rect.sizeDelta.x : 0f;

    private static void SetRectWidth(RectTransform rect, float width)
    {
        if (rect == null)
            return;

        Vector2 size = rect.sizeDelta;
        size.x = Mathf.Max(0f, width);
        rect.sizeDelta = size;
    }

    private static string ShortTileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        value = value.Trim();
        return value.Length <= 9 ? value : value.Substring(0, 6) + "...";
    }    

    private void InitCheckerText(GameObject menuObj)
    {
        string figPrefabPath = "assets/fonts/figtree.asset";
        AssetBundle figBundle = FontCache.figtreeBundle;
        TMP_FontAsset figtreeFont = figBundle.LoadAsset<TMP_FontAsset>(figPrefabPath);

        GameObject side = menuObj.transform.Find("SideHolder").gameObject;
        Transform sideTransform = side.transform;
        nameTextComp        = FindText(side, "Name");
        platformTextComp    = FindText(side, "Platform");
        fpsPingTextComp     = FindCheckerTextAny(sideTransform, "FPS/Ping", "FPS", "Ping");
        fpsTextComp         = fpsPingTextComp;
        pingTextComp        = fpsPingTextComp;
        dateTextComp        = FindText(side, "Date");
        modsCountTextComp   = FindTextPath(sideTransform, "ModsTitle/NumberActive");
        cheatsCountTextComp = FindTextPath(sideTransform, "CheatsTitle/NumberActive");
        InitCheckerTileGroup(sideTransform, "ModsTitle", out modsTileObjs, out modsTileTextComps, figtreeFont);
        InitCheckerTileGroup(sideTransform, "CheatsTitle", out cheatsTileObjs, out cheatsTileTextComps, figtreeFont);
        
        if (nameTextComp != null) { nameTextComp.text = "-----"; nameTextComp.font = figtreeFont; }
        if (fpsPingTextComp != null) { fpsPingTextComp.text = "--Hz \u2022 --Ms"; fpsPingTextComp.font = figtreeFont; EnsureCheckerTextVisible(fpsPingTextComp); }
        if (platformTextComp != null) { platformTextComp.text = "Platform"; platformTextComp.font = figtreeFont; }
        if (dateTextComp != null) { dateTextComp.text = "Date: --/--/----"; dateTextComp.font = figtreeFont; }
        if (modsCountTextComp != null) { modsCountTextComp.text = "0"; modsCountTextComp.font = figtreeFont; }
        if (cheatsCountTextComp != null) { cheatsCountTextComp.text = "0"; cheatsCountTextComp.font = figtreeFont; }
        SetCheckerTiles(modsTileGroup, Array.Empty<string>());
        SetCheckerTiles(cheatsTileGroup, Array.Empty<string>());
        InitVolumeSlider(sideTransform);
        InitVolumeButtons(sideTransform);
    }

    private void InitVolumeButtons(Transform sideTransform)
    {
        WireVolumeButton(sideTransform, "PlusVolume", "PlusVolumeCollider", "VolumeUp");
        WireVolumeButton(sideTransform, "MinusVolume", "MinusVolumeCollider", "VolumeDown");
        WireVolumeButton(sideTransform, "Mute", "MuteCollider", "Mute");
        WireVolumeButton(sideTransform, "MuteElse", "MuteElseCollider", "MuteElse");
    }

    private void WireVolumeButton(Transform sideTransform, string visualName, string colliderName, string identifier)
    {
        Transform visual = FindChildAtPath(sideTransform, $"VolumeControls/Main/{visualName}") ?? FindChildByName(sideTransform, visualName);
        Transform collider = FindChildAtPath(sideTransform, $"VolumeControls/{colliderName}") ?? FindChildByName(sideTransform, colliderName);
        if (visual == null || collider == null)
        {
            Debug.LogWarning($"[TUP] Volume button missing visual={visualName} collider={colliderName}");
            return;
        }

        ButtonTrigger trigger = WireInteractive(visual, identifier, null, collider);
        trigger.ButtonRoot = visual;
        Debug.Log($"[TUP] Volume wire {identifier}: visual={GetTransformPath(visual)} collider={GetTransformPath(collider)}");
    }

    private void InitVolumeSlider(Transform sideTransform)
    {
        Transform slider = FindChildAtPath(sideTransform, "VolumeControls/Main/Slider") ?? FindChildByName(sideTransform, "Slider");
        Transform inner = slider != null ? slider.Find("Inner") ?? FindChildByName(slider, "Inner") : null;
        if (slider == null || inner == null)
        {
            Debug.LogWarning("[TUP] Volume display missing. Need VolumeControls/Main/Slider/Inner.");
            return;
        }

        volumeInnerRect = inner as RectTransform ?? inner.GetComponent<RectTransform>();
        SetVolumeDisplayVolume(Mods.GetSelectedVolume(), false);
    }

    public void SetVolumeSliderValue01(float value)
    {
        SetVolumeDisplayVolume(Mathf.Clamp01(value) * 2f, true);
    }

    public void SetVolumeDisplayVolume(float volume, bool smooth = true)
    {
        float targetWidth = Mathf.Clamp(volume, 0f, 2f) * (VolumeDisplayMaxWidth * 0.5f);
        if (volumeInnerRect == null)
            return;

        if (volumeInnerRoutine != null)
            StopCoroutine(volumeInnerRoutine);
        if (smooth)
            volumeInnerRoutine = StartCoroutine(AnimateVolumeInnerWidth(GetRectWidth(volumeInnerRect), targetWidth));
        else
            SetRectWidth(volumeInnerRect, targetWidth);
    }

    private IEnumerator AnimateVolumeInnerWidth(float from, float to)
    {
        if (volumeInnerRect == null)
            yield break;

        for (float time = 0f; time < VolumeDisplayTweenDuration; time += Time.deltaTime)
        {
            float p = EaseOutQuad(Mathf.Clamp01(time / VolumeDisplayTweenDuration));
            SetRectWidth(volumeInnerRect, Mathf.LerpUnclamped(from, to, p));
            yield return null;
        }

        SetRectWidth(volumeInnerRect, to);
        volumeInnerRoutine = null;
    }

    private static void ConfigureRectCollider(RectTransform rect, Collider collider)
    {
        BoxCollider box = collider as BoxCollider;
        if (rect == null || box == null) return;
        Vector2 size = rect.rect.size;
        if (size.sqrMagnitude <= 0.001f) size = rect.sizeDelta;
        if (size.sqrMagnitude > 1f)
            size *= 0.0001f;
        if (size.sqrMagnitude <= 0.001f) return;
        box.center = Vector3.zero;
        box.size = new Vector3(Mathf.Max(0.002f, size.x), Mathf.Max(0.002f, size.y), 0.01f);
    }

    public void UpdateCheckerText(string plrName, int plrFPS, string plrPlatform, string plrCreationDate, string plrColor, Color colorBaseForm, int plrPing)
    {
        if (nameTextComp != null) nameTextComp.text = string.IsNullOrWhiteSpace(plrName) ? "-----" : plrName;
        if (fpsPingTextComp != null) { EnsureCheckerTextVisible(fpsPingTextComp); fpsPingTextComp.text = $"{plrFPS}Hz \u2022 {plrPing}Ms"; }
        if (platformTextComp != null) platformTextComp.text = string.IsNullOrWhiteSpace(plrPlatform) ? "Unknown" : plrPlatform;
        if (dateTextComp != null) dateTextComp.text = "Date: " + (string.IsNullOrWhiteSpace(plrCreationDate) ? "--/--/----" : plrCreationDate);
        Transform monkeColorTransform = Main.menuObj?.transform.Find("SideHolder/MonkeColor");
        SpriteRenderer monkeColor = monkeColorTransform != null ? monkeColorTransform.GetComponent<SpriteRenderer>() : null;
        if (monkeColor != null) monkeColor.color = colorBaseForm;
    }

    public void UpdateCheckerProperties(string legalMods, string illegalMods)
    {
        string[] legalList = SplitCheckerList(legalMods);
        string[] illegalList = SplitCheckerList(illegalMods);
        if (modsCountTextComp != null) modsCountTextComp.text = legalList.Length.ToString();
        if (cheatsCountTextComp != null) cheatsCountTextComp.text = illegalList.Length.ToString();
        SetCheckerTiles(modsTileGroup, legalList);
        SetCheckerTiles(cheatsTileGroup, illegalList);
    }

    private static string[] SplitCheckerList(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("None", StringComparison.OrdinalIgnoreCase)) return Array.Empty<string>();
        return value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(item => item.Trim()).Where(item => item.Length > 0 && !item.Equals("None", StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    TMP_Text roomInfoText;
    TMP_Text roomNameText;
    TMP_Text dateTimeText;

    private void InitRoomInfo(GameObject menuObj)
    {
        Transform menu = menuObj.transform;

        roomInfoText = FindChildByName(menu, "RoomInfo")?.GetComponent<TMP_Text>();
        roomNameText = FindChildByName(menu, "RoomName")?.GetComponent<TMP_Text>();
        dateTimeText = FindChildByName(menu, "DateTime")?.GetComponent<TMP_Text>();

        if (roomInfoText == null) Debug.LogError("RoomInfo not found");
        if (roomNameText == null) Debug.LogError("RoomName not found");
        if (dateTimeText == null) Debug.LogError("DateTime not found");
    }
    
    private IEnumerator RoomInfoUpdater()
    {
        WaitForSeconds wait = new WaitForSeconds(1f);

        while (true)
        {
            if (isMenuOpened)
            {
                string date = DateTime.Now.ToString("MM/dd/yy");
                string time = DateTime.Now.ToString("HH:mm");

                if (dateTimeText != null) dateTimeText.text = $"{date}   |   {time}";

                string roomName = Photon.Pun.PhotonNetwork.CurrentRoom?.Name ?? "----";
                if (roomNameText != null) roomNameText.text = roomName;

                int players = Photon.Pun.PhotonNetwork.CurrentRoom?.PlayerCount ?? 0;
                int max = Photon.Pun.PhotonNetwork.CurrentRoom?.MaxPlayers ?? 10;
                int ping = Photon.Pun.PhotonNetwork.GetPing();
                string name = Photon.Pun.PhotonNetwork.NickName;

                if (roomInfoText != null) roomInfoText.text = $"{players}/{max} Players  |  {ping} ms  |  {name}";
            }

            yield return wait;
        }
    }

    private void InitSelectorObj()
    {
        selectionObj = CreateSelectorPresser(GTPlayer.Instance.RightHand.controllerTransform, false);
    }

    private GameObject CreateSelectorPresser(Transform controller, bool isLeft)
    {
        if (controller == null)
            return null;

        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        obj.transform.SetParent(controller);
        obj.transform.localPosition = GetSelectorLocalPosition(isLeft);
        obj.transform.localRotation = Quaternion.identity;
        obj.transform.localScale = Vector3.one * 0.01f;
        obj.layer = LayerMask.NameToLayer("Ignore Raycast");

        SphereCollider col = obj.GetComponent<SphereCollider>() ?? obj.AddComponent<SphereCollider>();
        col.isTrigger = true;

        Rigidbody rb = obj.GetComponent<Rigidbody>() ?? obj.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        ButtonPresser presser = obj.GetComponent<ButtonPresser>() ?? obj.AddComponent<ButtonPresser>();
        presser.isLeft = isLeft;

        Renderer rend = obj.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material.shader = ShaderCache.UberShader;
            rend.material.color = Theme.Accent;
        }

        return obj;
    }

    private Transform GetMainMenuRoot()
    {
        Transform direct = menuObj != null ? menuObj.transform.Find("MainMenu") : null;
        if (direct != null)
            return direct;

        return menuObj != null
            ? menuObj.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t != null && t.name.Equals("MainMenu", StringComparison.OrdinalIgnoreCase) && FindChildByName(t, "ButtonsHolder") != null)
            : null;
    }

    private void PrepareRuntimeButtonTemplate()
    {
        Transform mainMenu = GetMainMenuRoot();
        runtimeButtonsHolder = mainMenu != null ? mainMenu.Find("ButtonsHolder") ?? FindChildByName(mainMenu, "ButtonsHolder") : null;
        runtimeButtonTemplate = runtimeButtonsHolder != null ? runtimeButtonsHolder.Find("Button")?.gameObject : null;
        if (runtimeButtonTemplate == null)
            return;

        runtimeButtonTemplate.SetActive(false);
        runtimeButtonTemplate.name = "ButtonTemplate";
    }

    private static TMP_Text GetButtonTitle(Transform button)
    {
        Transform title = button?.Find("MainUI/Title") ?? FindChildByName(button, "Title") ?? button?.Find("ButtonText");
        return title != null ? title.GetComponent<TMP_Text>() ?? title.GetComponentInChildren<TMP_Text>(true) : null;
    }

    private static Transform GetInteractiveCollider(Transform root, string preferredName)
    {
        if (root == null)
            return null;

        Transform preferred = root.Find(preferredName) ?? FindChildByName(root, preferredName);
        if (preferred != null)
            return preferred;

        return root.GetComponent<Collider>() != null ? root : null;
    }

    private ButtonTrigger WireInteractive(Transform visualRoot, string identifier, Action action = null, Transform colliderRoot = null)
    {
        if (visualRoot == null)
            return null;

        Transform target = colliderRoot ?? GetInteractiveCollider(visualRoot, "Collider") ?? GetInteractiveCollider(visualRoot, "ClickCollider") ?? visualRoot;
        target.gameObject.layer = 2;

        Collider col = target.GetComponent<Collider>();
        if (col == null)
            col = target.gameObject.AddComponent<BoxCollider>();
        col.isTrigger = true;
        ConfigureInteractiveBox(target, visualRoot, col);
        HideColliderDebugVisual(target);

        Rigidbody rb = target.GetComponent<Rigidbody>();
        if (rb == null)
            rb = target.gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        ButtonTrigger trigger = target.GetComponent<ButtonTrigger>() ?? target.gameObject.AddComponent<ButtonTrigger>();
        trigger.BtnIdentifier = identifier;
        trigger.CustomAction = action;
        trigger.ButtonRoot = visualRoot;

        ButtonCollider buttonCollider = target.GetComponent<ButtonCollider>() ?? target.gameObject.AddComponent<ButtonCollider>();
        buttonCollider.trigger = trigger;
        return trigger;
    }

    private static void AddButtonRootPressFallback(Transform buttonRoot, ButtonTrigger trigger)
    {
        if (buttonRoot == null || trigger == null)
            return;

        buttonRoot.gameObject.layer = 2;
        Collider col = buttonRoot.GetComponent<Collider>();
        if (col == null)
            col = buttonRoot.gameObject.AddComponent<BoxCollider>();
        col.isTrigger = true;
        ConfigureInteractiveBox(buttonRoot, buttonRoot, col);


        Rigidbody rb = buttonRoot.GetComponent<Rigidbody>();
        if (rb == null)
            rb = buttonRoot.gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        ButtonCollider buttonCollider = buttonRoot.GetComponent<ButtonCollider>() ?? buttonRoot.gameObject.AddComponent<ButtonCollider>();
        buttonCollider.trigger = trigger;
        Debug.Log($"[TUP BUTTON WIRE] root={GetTransformPath(buttonRoot)} id={trigger.BtnIdentifier}");
    }

    private static void ConfigureInteractiveBox(Transform target, Transform visualRoot, Collider collider)
    {
        BoxCollider box = collider as BoxCollider;
        if (target == null || box == null)
            return;

        MeshFilter meshFilter = target.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            Bounds bounds = meshFilter.sharedMesh.bounds;
            if (bounds.size.sqrMagnitude > 0.000001f)
            {
                box.center = bounds.center;
                box.size = new Vector3(Mathf.Max(0.002f, bounds.size.x), Mathf.Max(0.002f, bounds.size.y), Mathf.Max(0.002f, bounds.size.z));
                return;
            }
        }

        RectTransform targetRect = target as RectTransform ?? target.GetComponent<RectTransform>();
        RectTransform visualRect = visualRoot as RectTransform ?? visualRoot?.GetComponent<RectTransform>();
        Vector2 size = targetRect != null ? targetRect.rect.size : Vector2.zero;
        if (size.sqrMagnitude <= 0.001f && visualRect != null)
            size = visualRect.rect.size;
        if (size.sqrMagnitude > 1f)
            size *= 0.0001f;
        if (size.sqrMagnitude <= 0.001f)
            size = new Vector2(0.08f, 0.08f);

        box.center = Vector3.zero;
        box.size = new Vector3(Mathf.Max(0.002f, size.x), Mathf.Max(0.002f, size.y), 0.01f);
    }

    private static bool IsInputColliderObject(Component component)
    {
        return component != null && IsInputColliderObject(component.gameObject);
    }

    private static bool IsInputColliderObject(GameObject obj)
    {
        if (obj == null)
            return false;
        if (obj.name.IndexOf("HoverCollider", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;
        return obj.name.Equals("Collider", StringComparison.OrdinalIgnoreCase) || obj.name.EndsWith("Collider", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDebugVisibleColliderObject(Transform transform)
    {
        if (transform == null)
            return false;
        if (transform.name.IndexOf("Collider", StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        return transform.name.IndexOf("HoverCollider", StringComparison.OrdinalIgnoreCase) < 0;
    }
    private void HideAllColliderDebugVisuals()
    {
        if (menuObj == null)
            return;

        foreach (Transform child in menuObj.GetComponentsInChildren<Transform>(true))
        {
            if (child == null)
                continue;
            if (child.name.IndexOf("Collider", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (child.name.IndexOf("HoverCollider", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            HideColliderDebugVisual(child);
        }
    }

    private static void HideColliderDebugVisual(Transform colliderTransform)
    {
        if (colliderTransform == null)
            return;
        if (colliderTransform.name.IndexOf("Collider", StringComparison.OrdinalIgnoreCase) < 0)
            return;
        if (colliderTransform.name.IndexOf("HoverCollider", StringComparison.OrdinalIgnoreCase) >= 0)
            return;

        MeshRenderer meshRenderer = colliderTransform.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
            meshRenderer.enabled = false;
    }
    private static Vector3 GetSelectorLocalPosition(bool isLeft)
    {
        if (GTPlayer.Instance == null)
            return Vector3.forward * 0.13f;

        var hand = isLeft ? GTPlayer.Instance.LeftHand : GTPlayer.Instance.RightHand;
        return hand.handOffset + (hand.handRotOffset * Vector3.forward * 0.13f);
    }

    private void EnsureKeyboardLeftSelector()
    {
        if (leftKeyboardSelectionObj != null)
            return;

        leftKeyboardSelectionObj = CreateSelectorPresser(GTPlayer.Instance.LeftHand.controllerTransform, true);
    }

    private void DestroyKeyboardLeftSelector()
    {
        if (leftKeyboardSelectionObj == null)
            return;

        Destroy(leftKeyboardSelectionObj);
        leftKeyboardSelectionObj = null;
    }
    
    private void SaveVisibleButtonStates()
    {
        foreach (GameObject btnObj in btnObjs)
        {
            if (btnObj == null) continue;
            ButtonTrigger trigger = btnObj.GetComponent<ButtonTrigger>() ?? btnObj.GetComponentInChildren<ButtonTrigger>(true);
            if (trigger != null && trigger.IsToggle)
                Mods.SaveToggleState(trigger.BtnIdentifier, trigger.IsOn);
        }
    }
    private IEnumerator CloseMenu()
    {
        if (isMenuClosing)
            yield break;

        if (Mods.IsRenaming)
            Mods.CancelRename();

        isMenuClosing = true;
        Tools.StopCoroutine(ref buttonRoutine);

        if (menuObj != null && menuObj.activeSelf)
        {
            yield return StartCoroutine(
                MenuEffects.PopMenu(menuObj, Vector3.zero, menuObj.transform.localScale, false)
            );
        }

        if (menuObj != null)
            menuObj.SetActive(false);

        if (selectionObj != null)
        {
            Destroy(selectionObj);
            selectionObj = null;
        }

        DestroyKeyboardLeftSelector();

        DestroyButtons();

        foreach (GameObject selectorObj in selectorBtnObjs)
            if (selectorObj != null)
                selectorObj.SetActive(false);

        foreach (GameObject navBtn in navBtnObjs)
            if (navBtn != null)
                navBtn.SetActive(false);

        isMenuClosing = false;
        Debug.Log("Menu Closed");
        if (menuObj != null)
            Debug.Log(menuObj.activeSelf);
    }
    
    private IEnumerator CreateButtons(string categoryName = "Networking")
    {
        const float gap = 0.094f;

        if (!Mods.Actions.TryGetValue(currentCategory, out var category))
            yield break;

        SetSpotifyHudVisible();
        if (IsSpotifyHudPageSelected())
            yield break;

        var pageItems = BuildPageItems(category);
        int start = currentPage * PageSize;
        int end   = Math.Min(start + PageSize, pageItems.Count);

        int index = 0;
        for (int i = start; i < end; i++)
        {
            PageButtonItem item = pageItems[i];
            float offset = -(index * gap);
            CreateButton(offset, item.Label, item.IsToggle, item.Cooldown, item.CustomAction);
            index++;
            yield return new WaitForSeconds(0.08f);
        }
    }
    
    private readonly struct PageButtonItem
    {
        public readonly string Label;
        public readonly bool IsToggle;
        public readonly float Cooldown;
        public readonly Action CustomAction;

        public PageButtonItem(string label, bool isToggle, float cooldown, Action customAction)
        {
            Label = label;
            IsToggle = isToggle;
            Cooldown = cooldown;
            CustomAction = customAction;
        }
    }

    private List<PageButtonItem> BuildPageItems(ModCategory category)
    {
        List<PageButtonItem> items = category.Actions
            .Select(action => new PageButtonItem(action.Key, action.Value.IsToggle, action.Value.Cooldown, null))
            .ToList();

        if (currentCategory != "SelectUser")
            return items;

        foreach (VRRig rig in Mods.GetSelectableRigs())
        {
            if (rig == null)
                continue;

            VRRig capturedRig = rig;
            items.Add(new PageButtonItem(Mods.GetRigDisplayName(rig), false, 0f, () => Mods.SelectRigFromMenu(capturedRig)));
        }

        return items;
    }
    private void SwitchCategory(string categoryName)
    {
        Tools.StopCoroutine(ref buttonRoutine);
        currentCategory = categoryName;
        currentPage = 0;

        if (categoryName == "Cosmetics")
            Mods.InitOutfitSlotActions();

        DestroyButtons();
        SetSpotifyHudVisible();
        buttonRoutine = StartCoroutine(CreateButtons());
    }

    private void InitPageButtons(GameObject menuObj)
    {
        selectorBtnObjs.Clear();
        List<string> categories = new List<string>
        {
            "Networking",
            "Room",
            "Cosmetics",
            "SelectUser",
            "Settings",
            "Anticheat",
            "Spotify"
        }.Where(category => Mods.Actions.ContainsKey(category) && Mods.Actions[category].ShowInMenu).ToList();

        foreach (Transform child in menuObj.GetComponentsInChildren<Transform>(true))
        {
            if (!child.name.Contains("SelectorBtn", StringComparison.OrdinalIgnoreCase))
                continue;
            
            Transform selectorCollider = GetInteractiveCollider(child, "Collider");
            ButtonTrigger trigger = WireInteractive(child, child.name, null, selectorCollider);
            MenuTheme.ApplySelectorButton(child);
            ForceSelectorButtonVisible(child);
            LogSelectorButtonVisualState(child, "init");

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
            string capturedCategory = categoryName;
            trigger.SkipSwitchAnimation = true;
            trigger.CustomAction = () => SwitchCategory(capturedCategory);
            Debug.Log($"[TUP SELECTOR WIRE] {child.name} -> {capturedCategory}");
            
            selectorBtnObjs.Add(child.gameObject);
        }
    }

    private static void ForceSelectorButtonVisible(Transform selectorButton)
    {
        if (selectorButton == null)
            return;

        selectorButton.gameObject.SetActive(true);
        foreach (Image image in selectorButton.GetComponentsInChildren<Image>(true))
        {
            if (image == null)
                continue;
            image.enabled = true;
            Color c = image.color;
            if (c.a < 0.35f)
                c.a = 1f;
            image.color = c;
        }

        foreach (RawImage image in selectorButton.GetComponentsInChildren<RawImage>(true))
        {
            if (image == null)
                continue;
            image.enabled = true;
            Color c = image.color;
            if (c.a < 0.35f)
                c.a = 1f;
            image.color = c;
        }
    }
    private static void LogSelectorButtonVisualState(Transform selectorButton, string stage)
    {
        if (selectorButton == null)
        {
            Debug.LogWarning($"[TUP SELECTOR DEBUG] {stage}: null selector");
            return;
        }

        Image[] images = selectorButton.GetComponentsInChildren<Image>(true);
        RawImage[] rawImages = selectorButton.GetComponentsInChildren<RawImage>(true);
        Canvas[] canvases = selectorButton.GetComponentsInParent<Canvas>(true);
        string imageState = string.Join(" | ", images.Select(i => i == null ? "null" : $"{i.name}:enabled={i.enabled}:alpha={i.color.a:F2}:active={i.gameObject.activeSelf}/{i.gameObject.activeInHierarchy}"));
        string rawState = string.Join(" | ", rawImages.Select(i => i == null ? "null" : $"{i.name}:enabled={i.enabled}:alpha={i.color.a:F2}:active={i.gameObject.activeSelf}/{i.gameObject.activeInHierarchy}:texture={(i.texture == null ? "null" : i.texture.name)}"));
        string canvasState = string.Join(" | ", canvases.Select(c => c == null ? "null" : $"{c.name}:enabled={c.enabled}:active={c.gameObject.activeSelf}/{c.gameObject.activeInHierarchy}"));
        Debug.Log($"[TUP SELECTOR DEBUG] {stage} path={GetTransformPath(selectorButton)} active={selectorButton.gameObject.activeSelf}/{selectorButton.gameObject.activeInHierarchy} scale={selectorButton.localScale} images=[{imageState}] raw=[{rawState}] canvases=[{canvasState}]");
    }
    private void InitSpotifyHudPage(GameObject menuObj)
    {
        spotifyHudPage = FindChildByName(menuObj.transform, "SpotifyHudPage")?.gameObject
                         ?? FindChildByName(menuObj.transform, "SpotifyPage")?.gameObject
                         ?? FindChildByName(menuObj.transform, "SpotifyHUD")?.gameObject;

        if (spotifyHudPage == null)
        {
            Debug.LogWarning("[TUP] SpotifyHudPage not found. Add hierarchy from final notes.");
            return;
        }

        spotifySongText = FindTmpTextAtPath(spotifyHudPage.transform, "SongNameCont/SongName")
                          ?? FindTmpText(spotifyHudPage.transform, "SongName", "Song", "Title", "TrackTitle", "TrackName");
        spotifyArtistText = FindTmpText(spotifyHudPage.transform, "ArtistName", "AuthorName", "Artist", "Author", "Subtitle", "TrackArtist");
        spotifyDurationText = FindTmpText(spotifyHudPage.transform, "Duration", "Time", "TrackTime");
        spotifyStatusText = FindTmpText(spotifyHudPage.transform, "Status", "PlaybackStatus");
        spotifyHintText = FindTmpText(spotifyHudPage.transform, "Hint", "Source", "HelperText");
        BindSpotifyTextFallbacks();
        Transform albumIconTransform = FindChildAtPath(menuObj.transform, "SpotifyHudPage/AlbumIconCont/AlbumIcon")
                                       ?? FindChildAtPath(spotifyHudPage.transform, "AlbumIconCont/AlbumIcon")
                                       ?? FindSpotifyAlbumTransform(spotifyHudPage.transform);
        spotifyAlbumIcon = EnsureSpotifyAlbumRawImage(albumIconTransform ?? spotifyHudPage.transform);
        spotifyAlbumImage = albumIconTransform != null ? albumIconTransform.GetComponent<Image>() ?? albumIconTransform.GetComponentInChildren<Image>(true) : spotifyHudPage.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.name.IndexOf("progress", StringComparison.OrdinalIgnoreCase) < 0);
        if (spotifyAlbumIcon != null) HideAlbumSlotImages(albumIconTransform ?? spotifyAlbumIcon.transform);
        if (spotifyAlbumIcon != null) spotifyAlbumIcon.transform.SetAsLastSibling();
        spotifyAlbumRenderer = albumIconTransform != null ? albumIconTransform.GetComponent<Renderer>() ?? albumIconTransform.GetComponentInChildren<Renderer>(true) : spotifyHudPage.GetComponentInChildren<Renderer>(true);
        if (spotifyAlbumRenderer != null)
        {
            spotifyAlbumMaterial = new Material(spotifyAlbumRenderer.material);
            spotifyAlbumRenderer.material = spotifyAlbumMaterial;
        }
        Debug.Log("[TUP SPOTIFY UI] Page=" + GetTransformPath(spotifyHudPage.transform) + "; texts song=" + (spotifySongText != null ? GetTransformPath(spotifySongText.transform) : "null") + ", artist=" + (spotifyArtistText != null ? GetTransformPath(spotifyArtistText.transform) : "null") + "; album=" + (albumIconTransform != null ? GetTransformPath(albumIconTransform) : "null") + ", raw=" + (spotifyAlbumIcon != null) + ", image=" + (spotifyAlbumImage != null) + ", renderer=" + (spotifyAlbumRenderer != null));
        spotifyProgressFill = FindChildByName(spotifyHudPage.transform, "ProgressFill")?.GetComponent<Image>();

        WireSpotifyHudButton("PlayPauseButton", SpotifyPlayPauseFromHud);
        Transform playPauseButton = FindChildByName(spotifyHudPage.transform, "PlayPauseButton");
        spotifyPlayIcon = playPauseButton != null ? playPauseButton.Find("Play") ?? FindChildAtPath(playPauseButton, "Play") ?? FindChildByName(playPauseButton, "Play") : null;
        spotifyPauseIcon = playPauseButton != null ? playPauseButton.Find("Pause") ?? FindChildAtPath(playPauseButton, "Pause") ?? FindChildByName(playPauseButton, "Pause") : null;
        WireSpotifyHudButton("NextButton", Mods.SpotifyNext);
        WireSpotifyHudButton("PreviousButton", Mods.SpotifyPrevious);

        spotifyHudPage.SetActive(false);
        RefreshSpotifyHud();
    }

    private void SpotifyPlayPauseFromHud()
    {
        Mods.SpotifyPlayPause();
        RefreshSpotifyHudDelayed(0.15f);
    }

    private void ToggleSpotifyPlaybackIconsImmediate_UNUSED()
    {
        if (spotifyPlayIcon == null || spotifyPauseIcon == null)
            return;

        bool nextPlaying = !spotifyPauseIcon.gameObject.activeSelf;
        SetSpotifyPlaybackIcons(nextPlaying);
    }
    private void WireSpotifyHudButton(string buttonName, Action action)
    {
        if (spotifyHudPage == null) return;

        Transform button = FindChildByName(spotifyHudPage.transform, buttonName);
        if (button == null)
        {
            Debug.LogWarning("[TUP] Spotify HUD button missing: " + buttonName);
            return;
        }

        Transform colliderTransform = button.Find("Collider") ?? FindChildByName(button, "Collider");
        if (colliderTransform == null)
        {
            Debug.LogWarning("[TUP] Spotify HUD button has no Collider child: " + buttonName);
            return;
        }

        colliderTransform.gameObject.layer = 2;

        Collider col = colliderTransform.GetComponent<Collider>();
        if (col == null)
            col = colliderTransform.gameObject.AddComponent<BoxCollider>();
        col.isTrigger = true;

        Rigidbody rb = colliderTransform.GetComponent<Rigidbody>();
        if (rb == null)
            rb = colliderTransform.gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        ButtonTrigger trigger = colliderTransform.GetComponent<ButtonTrigger>() ?? colliderTransform.gameObject.AddComponent<ButtonTrigger>();
        trigger.BtnIdentifier = "Spotify " + buttonName.Replace("Button", "");
        trigger.CustomAction = action;
        trigger.ButtonRoot = button;

        ButtonCollider buttonCollider = colliderTransform.GetComponent<ButtonCollider>() ?? colliderTransform.gameObject.AddComponent<ButtonCollider>();
        buttonCollider.trigger = trigger;

        if (!spotifyHudButtonColliders.Contains(colliderTransform.gameObject))
            spotifyHudButtonColliders.Add(colliderTransform.gameObject);
    }

    private static Transform FindChildByName(Transform root, string name)
    {
        if (root == null) return null;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return child;
        return null;
    }

    private static TMP_Text FindTmpText(Transform root, params string[] names)
    {
        foreach (string name in names)
        {
            Transform child = FindChildByName(root, name);
            TMP_Text text = child != null ? child.GetComponent<TMP_Text>() ?? child.GetComponentInChildren<TMP_Text>(true) : null;
            if (text != null)
                return text;
        }

        return null;
    }

    private static TMP_Text FindTmpTextAtPath(Transform root, string path)
    {
        Transform child = root != null ? root.Find(path) : null;
        return child != null ? child.GetComponent<TMP_Text>() ?? child.GetComponentInChildren<TMP_Text>(true) : null;
    }

    private static Transform FindChildAtPath(Transform root, string path)
    {
        return root != null ? root.Find(path) : null;
    }

    private static string GetTransformPath(Transform transform)
    {
        if (transform == null)
            return "null";

        List<string> parts = new List<string>();
        for (Transform current = transform; current != null; current = current.parent)
            parts.Add(current.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    private void BindSpotifyTextFallbacks()
    {
        if (spotifyHudPage == null)
            return;

        TMP_Text[] texts = spotifyHudPage.GetComponentsInChildren<TMP_Text>(true)
            .Where(text => text != null && !IsSpotifyControlText(text))
            .OrderByDescending(text => TextNameScore(text, "song", "title", "track"))
            .ThenBy(text => text.transform.GetSiblingIndex())
            .ToArray();

        if (spotifySongText == null)
            spotifySongText = texts.FirstOrDefault();

        if (spotifyArtistText == null)
        {
            spotifyArtistText = texts
                .OrderByDescending(text => TextNameScore(text, "artist", "author", "subtitle"))
                .ThenBy(text => text == spotifySongText ? 1 : 0)
                .FirstOrDefault(text => text != spotifySongText);
        }

        if (spotifyDurationText == null)
            spotifyDurationText = texts.FirstOrDefault(text => NameContains(text, "duration", "time"));
        if (spotifyStatusText == null)
            spotifyStatusText = texts.FirstOrDefault(text => NameContains(text, "status", "playback"));
    }

    private static bool IsSpotifyControlText(TMP_Text text)
    {
        string path = GetTransformPath(text.transform).ToLowerInvariant();
        return path.Contains("button") || path.Contains("next") || path.Contains("previous") || path.Contains("playpause") || path.Contains("page");
    }

    private static int TextNameScore(TMP_Text text, params string[] terms)
    {
        string path = GetTransformPath(text.transform).ToLowerInvariant();
        int score = 0;
        foreach (string term in terms)
            if (path.Contains(term)) score += 10;
        return score;
    }

    private static bool NameContains(TMP_Text text, params string[] terms)
    {
        string path = GetTransformPath(text.transform).ToLowerInvariant();
        return terms.Any(term => path.Contains(term));
    }

private static Transform FindSpotifyAlbumTransform(Transform root)
    {
        string[] names = { "AlbumIcon", "AlbumImage", "AlbumArt", "Artwork", "Cover", "CoverArt", "SongIcon", "TrackIcon" };
        foreach (string name in names)
        {
            Transform child = FindChildByName(root, name);
            if (child != null && !IsSpotifyControlPath(child))
                return child;
        }

        return root.GetComponentsInChildren<Transform>(true)
            .Where(t => t != null && !IsSpotifyControlPath(t))
            .OrderByDescending(t => TextNameScore(GetTransformPath(t), "album", "artwork", "cover", "songicon", "trackicon"))
            .FirstOrDefault(t => TextNameScore(GetTransformPath(t), "album", "artwork", "cover", "songicon", "trackicon") > 0);
    }

    private static bool IsSpotifyControlPath(Transform transform)
    {
        string path = GetTransformPath(transform).ToLowerInvariant();
        return path.Contains("button") || path.Contains("next") || path.Contains("previous") || path.Contains("playpause") || path.Contains("progress");
    }

    private static int TextNameScore(string path, params string[] terms)
    {
        path = (path ?? "").ToLowerInvariant();
        int score = 0;
        foreach (string term in terms)
            if (path.Contains(term)) score += 10;
        return score;
    }

    private static RawImage EnsureSpotifyAlbumRawImage(Transform slot)
    {
        if (slot == null)
            return null;

        RawImage rawImage = slot.GetComponent<RawImage>() ?? slot.GetComponentInChildren<RawImage>(true);
        if (rawImage != null)
        {
            rawImage.raycastTarget = false;
            return rawImage;
        }

        RectTransform slotRect = slot as RectTransform ?? slot.GetComponent<RectTransform>();
        if (slotRect == null)
            return null;

        GameObject imageObject = new GameObject("RuntimeAlbumRawImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        imageObject.transform.SetParent(slot, false);

        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.localPosition = Vector3.zero;

        rawImage = imageObject.GetComponent<RawImage>();
        rawImage.raycastTarget = false;
        rawImage.color = new Color(1f, 1f, 1f, 0.35f);
        return rawImage;
    }

    private static void HideAlbumSlotImages(Transform slot)
    {
        if (slot == null)
            return;

        foreach (Image image in slot.GetComponentsInChildren<Image>(true))
            image.enabled = false;
    }

    private bool IsSpotifyHudPageSelected()
    {
        if (currentCategory != "Spotify") return false;
        if (!Mods.Actions.TryGetValue(currentCategory, out var category)) return true;
        return currentPage == GetSpotifyHudPageIndex(category);
    }

    private int GetSpotifyHudPageIndex(ModCategory category)
    {
        int normalPages = Mathf.CeilToInt((float)category.Actions.Count / PageSize);
        return Mathf.Max(0, normalPages);
    }

    private int GetCategoryTotalPages(string categoryName, ModCategory category)
    {
        int itemCount = category.Actions.Count + (categoryName == "SelectUser" ? Mods.GetSelectableRigs().Count : 0);
        int normalPages = Mathf.CeilToInt((float)itemCount / PageSize);
        if (categoryName == "Spotify")
            return Mathf.Max(1, normalPages + 1);
        return Mathf.Max(1, normalPages);
    }

    private void SetSpotifyHudVisible()
    {
        if (spotifyHudPage == null) return;
        bool visible = IsSpotifyHudPageSelected();
        bool wasVisible = spotifyHudWasVisible && spotifyHudPage.activeSelf;
        spotifyHudPage.SetActive(visible);
        spotifyHudWasVisible = visible;
        if (visible)
        {
            RefreshSpotifyHud();
            if (!wasVisible)
                StartSpotifyHudWave();
        }
        else if (spotifyHudWaveRoutine != null)
        {
            StopCoroutine(spotifyHudWaveRoutine);
            spotifyHudWaveRoutine = null;
        }
    }

    private void StartSpotifyHudWave()
    {
        if (spotifyHudPage == null)
            return;

        if (spotifyHudWaveRoutine != null)
            StopCoroutine(spotifyHudWaveRoutine);

        spotifyHudWaveRoutine = StartCoroutine(SpotifyHudWaveIn());
    }

    private IEnumerator SpotifyHudWaveIn()
    {
        if (spotifyHudPage == null)
            yield break;

        List<Transform> parts = new List<Transform>();
        foreach (Transform child in spotifyHudPage.transform)
        {
            if (child == null || !child.gameObject.activeSelf)
                continue;

            if (!spotifyHudScales.ContainsKey(child) || spotifyHudScales[child] == Vector3.zero)
                spotifyHudScales[child] = child.localScale == Vector3.zero ? Vector3.one : child.localScale;

            child.localScale = Vector3.zero;
            parts.Add(child);
        }

        foreach (Transform part in parts)
        {
            if (part == null)
                continue;

            Vector3 targetScale = spotifyHudScales.TryGetValue(part, out Vector3 cachedScale) ? cachedScale : Vector3.one;
            StartCoroutine(MenuEffects.PopButton(part.gameObject, targetScale, Vector3.zero, true));
            PlayButtonEnterSound();
            yield return new WaitForSeconds(0.06f);
        }

        spotifyHudWaveRoutine = null;
    }

    private void PlayButtonEnterSound()
    {
        AudioSource audioSource = gameObject.GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.volume = 0.1f;
        audioSource.PlayOneShot(btnEnterSound);
    }

    private void UpdateSpotifyHudLoop()
    {
        if (!isMenuOpened || spotifyHudPage == null || !spotifyHudPage.activeSelf) return;
        UpdateSpotifySongMarquee();
        if (Time.time < nextSpotifyHudRefresh) return;
        nextSpotifyHudRefresh = Time.time + 0.5f;
        RefreshSpotifyHud();
    }

    public void RefreshSpotifyHudDelayed(float delay = 1.1f)
    {
        StartCoroutine(RefreshSpotifyHudAfterDelay(delay));
    }

    private IEnumerator RefreshSpotifyHudAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        Mods.InvalidateSpotifyCache();
        RefreshSpotifyHud();
    }

    public void RefreshSpotifyHud()
    {
        Mods.SpotifyTrackInfo rawInfo = Mods.GetSpotifyTrackInfo();
        Mods.SpotifyTrackInfo info = GetStableSpotifyInfo(rawInfo);
        QueueSpotifySongText(info.Song, info.Artist);
        if (spotifyDurationText != null) spotifyDurationText.text = info.Duration;
        if (spotifyStatusText != null) spotifyStatusText.text = info.Status;
        if (spotifyHintText != null) spotifyHintText.text = info.HasTrack ? "Spotify desktop" : info.Artist;
        UpdateSpotifyPlaybackIcons(info);
        Texture2D albumTexture = ResolveSpotifyAlbumTexture(info) ?? ResolveSpotifyAlbumWebTexture(info);
        if (albumTexture == null) StartSpotifyAlbumArtworkDownload(info);
        UpdateSpotifyAlbumTransition(info, albumTexture);
        if (spotifyProgressFill != null) spotifyProgressFill.fillAmount = info.EndTime > 0f ? Mathf.Clamp01(info.ElapsedTime / info.EndTime) : 0f;
    }

    private void InitSpotifySongMarquee()
    {
        if (spotifySongText == null)
            return;

        spotifySongBaseLocalPosition = spotifySongText.transform.localPosition;
        spotifyDisplayedSong = "";
        spotifyMarqueeText = "";
        spotifySongMarqueeOffset = 0f;
        ApplySpotifySongMarqueeVisuals(1f);
    }

    private void QueueSpotifySongText(string song, string artist)
    {
        song = string.IsNullOrWhiteSpace(song) ? "No song" : song.Trim().Replace('\r', ' ').Replace('\n', ' ');
        artist = string.IsNullOrWhiteSpace(artist) ? "Unknown Artist" : artist.Trim().Replace('\r', ' ').Replace('\n', ' ');
        spotifyPendingSong = song;
        spotifyPendingArtist = artist;
        ApplySpotifyPendingText();
    }

    private void ApplySpotifyPendingText()
    {
        if (!string.IsNullOrEmpty(spotifyPendingSong))
            SetSpotifySongText(spotifyPendingSong);
        if (spotifyArtistText != null && !string.IsNullOrEmpty(spotifyPendingArtist) && spotifyPendingArtist != spotifyDisplayedArtist)
        {
            spotifyDisplayedArtist = spotifyPendingArtist;
            spotifyArtistText.text = spotifyDisplayedArtist;
        }
    }
    private void SetSpotifySongText(string song)
    {
        if (spotifySongText == null)
            return;

        song = string.IsNullOrWhiteSpace(song) ? "No song" : song.Trim();
        song = song.Replace('\r', ' ').Replace('\n', ' ');
        if (song == spotifyDisplayedSong)
            return;

        spotifyDisplayedSong = song;
        spotifyMarqueeText = string.Join(SpotifySongMarqueeGap, Enumerable.Repeat(song, SpotifySongMarqueeCopies));
        spotifySongText.text = spotifyMarqueeText;
        spotifySongText.ForceMeshUpdate();
        spotifySongMarqueeResetWidth = Mathf.Max(0.01f, spotifySongText.textBounds.size.x / SpotifySongMarqueeCopies);
        spotifySongMarqueeOffset = 0f;
        ApplySpotifySongMarqueeVisuals(1f);
    }

    private void UpdateSpotifySongMarquee()
    {
        if (spotifySongText == null || string.IsNullOrEmpty(spotifyMarqueeText))
            return;

        spotifySongMarqueeTimer += Time.deltaTime;
        float fadeOutStart = SpotifySongVisibleSeconds;
        float hiddenStart = fadeOutStart + SpotifySongFadeSeconds;
        float fadeInStart = hiddenStart + SpotifySongHiddenSeconds;
        float cycleEnd = fadeInStart + SpotifySongFadeSeconds;
        float alpha = 1f;

        if (spotifySongMarqueeTimer < fadeOutStart)
        {
            spotifySongMarqueeOffset += Time.deltaTime * SpotifySongMarqueeSpeed;
        }
        else if (spotifySongMarqueeTimer < hiddenStart)
        {
            spotifySongMarqueeOffset += Time.deltaTime * SpotifySongMarqueeSpeed;
            alpha = 1f - ((spotifySongMarqueeTimer - fadeOutStart) / SpotifySongFadeSeconds);
        }
        else if (spotifySongMarqueeTimer < fadeInStart)
        {
            spotifySongMarqueeOffset = 0f;
            alpha = 0f;
        }
        else if (spotifySongMarqueeTimer < cycleEnd)
        {
            spotifySongMarqueeOffset = 0f;
            alpha = (spotifySongMarqueeTimer - fadeInStart) / SpotifySongFadeSeconds;
        }
        else
        {
            spotifySongMarqueeTimer = 0f;
            spotifySongMarqueeOffset = 0f;
            alpha = 1f;
        }

        if (spotifySongMarqueeOffset >= spotifySongMarqueeResetWidth)
            spotifySongMarqueeOffset -= spotifySongMarqueeResetWidth;
        ApplySpotifySongMarqueeVisuals(alpha);
    }

    private void ApplySpotifySongMarqueeVisuals(float alpha)
    {
        if (spotifySongText == null)
            return;

        Vector3 position = spotifySongBaseLocalPosition;
        position.x += spotifySongMarqueeOffset;
        position.y = SpotifySongFixedY;
        position.z = SpotifySongFixedZ;
        spotifySongText.transform.localPosition = position;
        Color color = spotifySongBaseColor;
        color.a = spotifySongBaseColor.a * Mathf.Clamp01(alpha);
        spotifySongText.color = color;
    }

    private Mods.SpotifyTrackInfo GetStableSpotifyInfo(Mods.SpotifyTrackInfo info)
    {
        if (info.HasTrack)
        {
            spotifyLastGoodInfo = info;
            spotifyHasLastGoodInfo = true;
            return info;
        }

        if (spotifyHasLastGoodInfo)
        {
            string status = info.Status.Equals("Paused", StringComparison.OrdinalIgnoreCase) ? "Paused" : spotifyLastGoodInfo.Status;
            return new Mods.SpotifyTrackInfo(spotifyLastGoodInfo.Song, spotifyLastGoodInfo.Artist, spotifyLastGoodInfo.Duration, status, true, spotifyLastGoodInfo.Icon, spotifyLastGoodInfo.StartTime, spotifyLastGoodInfo.EndTime, spotifyLastGoodInfo.ElapsedTime, spotifyLastGoodInfo.ThumbnailBase64);
        }

        return info;
    }

    private void UpdateSpotifyAlbumTransition(Mods.SpotifyTrackInfo info, Texture2D albumTexture)
    {
        string key = GetSpotifyAlbumWebKey(info);
        if (string.IsNullOrEmpty(key))
            return;

        if (key == spotifyDisplayedAlbumKey)
        {
            if (spotifyDisplayedAlbumTexture == null && albumTexture != null)
            {
                spotifyDisplayedAlbumTexture = albumTexture;
                ApplySpotifyAlbumTexture(albumTexture);
                ApplySpotifyPendingText();
            SetSpotifyTransitionColor(Color.white);
            }
            return;
        }

        if (albumTexture == null)
            return;

        if (spotifyAlbumTransitionRoutine != null && spotifyAlbumTransitionTargetKey == key)
            return;

        if (spotifyAlbumTransitionRoutine != null)
            StopCoroutine(spotifyAlbumTransitionRoutine);
        spotifyAlbumTransitionTargetKey = key;
        spotifyAlbumTransitionRoutine = StartCoroutine(SpotifyAlbumTransition(key, albumTexture));
    }

    private IEnumerator SpotifyAlbumTransition(string newKey, Texture2D newTexture)
    {
        if (spotifyDisplayedAlbumTexture == null)
        {
            spotifyDisplayedAlbumKey = newKey;
            spotifyDisplayedAlbumTexture = newTexture;
            ApplySpotifyAlbumTexture(newTexture);
            ApplySpotifyPendingText();
            SetSpotifyTransitionColor(Color.white);
            spotifyAlbumTransitionTargetKey = "";
            spotifyAlbumTransitionRoutine = null;
            yield break;
        }

        float t = 0f;
        while (t < SpotifyAlbumFadeSeconds)
        {
            t += Time.deltaTime;
            SetSpotifyTransitionColor(Color.Lerp(Color.white, Color.black, Mathf.Clamp01(t / SpotifyAlbumFadeSeconds)));
            yield return null;
        }

        SetSpotifyTransitionColor(Color.black);
        yield return new WaitForSeconds(SpotifyAlbumBlackHoldSeconds);

        spotifyDisplayedAlbumKey = newKey;
        spotifyDisplayedAlbumTexture = newTexture;
        ApplySpotifyAlbumTexture(newTexture);

        t = 0f;
        while (t < SpotifyAlbumFadeSeconds)
        {
            t += Time.deltaTime;
            SetSpotifyTransitionColor(Color.Lerp(Color.black, Color.white, Mathf.Clamp01(t / SpotifyAlbumFadeSeconds)));
            yield return null;
        }

        SetSpotifyTransitionColor(Color.white);
        spotifyAlbumTransitionTargetKey = "";
        spotifyAlbumTransitionRoutine = null;
    }

    private void ApplySpotifyAlbumTexture(Texture2D albumTexture)
    {
        if (albumTexture == null)
            return;

        if (spotifyAlbumIcon != null)
        {
            spotifyAlbumIcon.texture = albumTexture;
            spotifyAlbumIcon.enabled = true;
        }

        if (spotifyAlbumImage != null && spotifyAlbumIcon == null)
        {
            spotifyAlbumSprite = Sprite.Create(albumTexture, new Rect(0f, 0f, albumTexture.width, albumTexture.height), new Vector2(0.5f, 0.5f), 100f);
            spotifyAlbumImage.sprite = spotifyAlbumSprite;
            spotifyAlbumImage.overrideSprite = spotifyAlbumSprite;
            spotifyAlbumImage.type = Image.Type.Simple;
            spotifyAlbumImage.preserveAspect = true;
            spotifyAlbumImage.enabled = true;
        }

        if (spotifyAlbumMaterial != null)
        {
            spotifyAlbumMaterial.mainTexture = albumTexture;
            if (spotifyAlbumMaterial.HasProperty("_BaseMap")) spotifyAlbumMaterial.SetTexture("_BaseMap", albumTexture);
            if (spotifyAlbumMaterial.HasProperty("_MainTex")) spotifyAlbumMaterial.SetTexture("_MainTex", albumTexture);
        }
    }

    private void SetSpotifyTransitionColor(Color color)
    {
        if (spotifyAlbumIcon != null) spotifyAlbumIcon.color = color;
        if (spotifyAlbumImage != null && spotifyAlbumIcon == null) spotifyAlbumImage.color = color;
        if (spotifyAlbumMaterial != null) spotifyAlbumMaterial.color = color;

    }
    private Texture2D ResolveSpotifyAlbumWebTexture(Mods.SpotifyTrackInfo info)
    {
        string key = GetSpotifyAlbumWebKey(info);
        if (string.IsNullOrEmpty(key))
            return null;

        lock (spotifyAlbumWebLock)
        {
            if (spotifyAlbumWebTexture != null && spotifyAlbumWebTextureKey == key)
                return spotifyAlbumWebTexture;

            if (spotifyAlbumWebBytes == null || spotifyAlbumWebBytesKey != key)
                return null;

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(spotifyAlbumWebBytes, false))
                return null;

            spotifyAlbumWebTexture = texture;
            spotifyAlbumWebTextureKey = key;
            return spotifyAlbumWebTexture;
        }
    }

    private void StartSpotifyAlbumArtworkDownload(Mods.SpotifyTrackInfo info)
    {
        string key = GetSpotifyAlbumWebKey(info);
        if (string.IsNullOrEmpty(key))
            return;

        lock (spotifyAlbumWebLock)
        {
            if (spotifyAlbumWebQueryRunning || spotifyAlbumWebRequestedKey == key || spotifyAlbumWebBytesKey == key)
                return;
            spotifyAlbumWebQueryRunning = true;
            spotifyAlbumWebRequestedKey = key;
        }

        string song = info.Song;
        string artist = info.Artist;
        Task.Run(() => DownloadSpotifyAlbumArtwork(song, artist, key));
    }

    private void DownloadSpotifyAlbumArtwork(string song, string artist, string key)
    {
        try
        {
            string query = WebUtility.UrlEncode((song + " " + artist).Trim());
            string searchUrl = "https://itunes.apple.com/search?term=" + query + "&entity=song&limit=1";
            using WebClient client = new WebClient();
            client.Headers[HttpRequestHeader.UserAgent] = "ThatUtilsPad/1.0";
            string json = client.DownloadString(searchUrl);
            string artworkUrl = (string)JObject.Parse(json)["results"]?.First?["artworkUrl100"];
            if (string.IsNullOrWhiteSpace(artworkUrl))
                return;

            artworkUrl = artworkUrl.Replace("100x100bb", "600x600bb");
            byte[] bytes = client.DownloadData(artworkUrl);
            if (bytes == null || bytes.Length == 0)
                return;


            lock (spotifyAlbumWebLock)
            {
                spotifyAlbumWebBytes = bytes;
                spotifyAlbumWebBytesKey = key;
                spotifyAlbumWebTexture = null;
                spotifyAlbumWebTextureKey = "";
            }
            Debug.Log("[TUP SPOTIFY] Downloaded web album image (" + bytes.Length + " bytes)");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TUP SPOTIFY] Web album download failed: " + e.Message);
        }
        finally
        {
            lock (spotifyAlbumWebLock)
            {
                spotifyAlbumWebQueryRunning = false;
            }
        }
    }

    private static string GetSpotifyAlbumWebKey(Mods.SpotifyTrackInfo info)
    {
        if (!info.HasTrack || string.IsNullOrWhiteSpace(info.Song) || info.Song.Equals("No song", StringComparison.OrdinalIgnoreCase))
            return "";
        return ((info.Song ?? "") + "|" + (info.Artist ?? "")).Trim().ToLowerInvariant();
    }
    private void UpdateSpotifyPlaybackIcons(Mods.SpotifyTrackInfo info)
    {
        bool playing = info.Status.Equals("Playing", StringComparison.OrdinalIgnoreCase);
        SetSpotifyPlaybackIcons(playing);
    }
    private void SetSpotifyPlaybackIcons(bool playing)
    {
        if (spotifyPlayIcon != null) spotifyPlayIcon.gameObject.SetActive(!playing);
        if (spotifyPauseIcon != null) spotifyPauseIcon.gameObject.SetActive(playing);
    }
    private Texture2D ResolveSpotifyAlbumTexture(Mods.SpotifyTrackInfo info)
    {
        if (info.Icon != null)
            return info.Icon;

        if (string.IsNullOrWhiteSpace(info.ThumbnailBase64))
            return null;

        int hash = info.ThumbnailBase64.GetHashCode();
        if (spotifyAlbumThumbnailHash == hash && spotifyAlbumIcon != null && spotifyAlbumIcon.texture is Texture2D cached)
            return cached;

        try
        {
            byte[] bytes = Convert.FromBase64String(info.ThumbnailBase64);
            if (bytes.Length == 0)
                return null;

            Texture2D texture = spotifyAlbumIcon != null && spotifyAlbumIcon.texture is Texture2D existing
                ? existing
                : new Texture2D(2, 2, TextureFormat.RGBA32, false);

            if (!texture.LoadImage(bytes, false))
            {
                Debug.LogWarning("[TUP SPOTIFY] Album texture LoadImage failed. Bytes=" + bytes.Length);
                return null;
            }

            spotifyAlbumThumbnailHash = hash;
            return texture;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TUP SPOTIFY] Album decode failed: " + e.Message);
            return null;
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

            int delta = navName == "PageNext" ? 1 : -1;
            ButtonTrigger trigger = WireInteractive(navTransform, navName, () => ChangePage(delta), GetInteractiveCollider(navTransform, "Collider"));
            MenuTheme.ApplyNavButton(navTransform);

            navBtnObjs.Add(navTransform.gameObject);
        }
    }

    private void ChangePage(int delta)
    {
        if (!Mods.Actions.TryGetValue(currentCategory, out var category))
            return;

        int totalPages = GetCategoryTotalPages(currentCategory, category);
        currentPage = (currentPage + delta + totalPages) % totalPages;

        Tools.StopCoroutine(ref buttonRoutine);
        DestroyButtons();
        SetSpotifyHudVisible();
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

        const float gap = 0.094f;
        int   visualIndex  = itemIndex - (currentPage * PageSize);
        float height       = -(visualIndex * gap);

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
        StartCoroutine(LoadNotificationSound(assembly));

        if (creamySound == null)
            Debug.LogError("[TUP] Failed to load click sound!");

        if (startSound == null)
            Debug.LogError("[TUP] Failed to load startup sound!");

        if (helloSound == null)
            Debug.LogError("[TUP] Failed to load hello sound!");
    }

    private IEnumerator LoadNotificationSound(Assembly assembly)
    {
        using Stream stream = assembly.GetManifestResourceStream("ThatUtilsPad.Assets.Sounds.notifSound.mp3");
        if (stream == null)
        {
            Debug.LogWarning("[TUP] notifSound.mp3 not found in resources");
            yield break;
        }

        string tempPath = Path.Combine(Application.temporaryCachePath, "tup_notifSound.mp3");
        byte[] bytes = new byte[stream.Length];
        stream.Read(bytes, 0, bytes.Length);
        File.WriteAllBytes(tempPath, bytes);

        using UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip("file:///" + tempPath.Replace("\\", "/"), AudioType.MPEG);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning("[TUP] Failed to load notifSound.mp3: " + request.error);
            yield break;
        }

        notifSound = DownloadHandlerAudioClip.GetContent(request);
    }

    public static void PlayNotificationSound()
    {
        if (Instance == null || Instance.notifSound == null) return;

        GameObject soundObject = new GameObject("TUP_NotificationSound");
        AudioSource audioSource = soundObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 0f;
        audioSource.volume = 0.35f;
        audioSource.PlayOneShot(Instance.notifSound);
        Destroy(soundObject, Instance.notifSound.length + 0.2f);
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
    
    
    private void CreateButton(float yOffset, string btnName, bool isToggle = false, float cooldown = 0f, Action customAction = null)
    {
        GameObject btn;
        bool usingRuntimeTemplate = runtimeButtonTemplate != null && runtimeButtonsHolder != null;

        if (usingRuntimeTemplate)
        {
            btn = Instantiate(runtimeButtonTemplate, runtimeButtonsHolder);
            btn.name = btnName;
            btn.SetActive(true);
            btn.transform.localPosition = runtimeButtonTemplate.transform.localPosition + new Vector3(0f, yOffset, 0f);
            btn.transform.localRotation = runtimeButtonTemplate.transform.localRotation;
            btn.transform.localScale = runtimeButtonTemplate.transform.localScale;
        }
        else
        {
            btn = Instantiate(btnPrefab, menuObj.transform);
            btn.name = btnName;
            btn.transform.localPosition = buttonBasePosition + new Vector3(0.01f, yOffset, 0f);
            btn.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        }

        Transform legacyClickCollider = FindButtonChild(btn.transform, "ClickCollider");
        if (legacyClickCollider != null && FindButtonChild(btn.transform, "Collider") == null)
            legacyClickCollider.name = "Collider";

        Transform slider = FindButtonChild(btn.transform, "Slider");
        Transform knob = FindButtonChild(btn.transform, "Slider/Knob");
        Transform clickCollider = GetInteractiveCollider(btn.transform, "Collider");
        ButtonTrigger trigger;

        if (usingRuntimeTemplate && clickCollider != null)
        {
            trigger = WireInteractive(btn.transform, btnName, customAction, clickCollider);
        }
        else
        {
            GameObject btnCollider = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var colliderFollow = btnCollider.AddComponent<FollowMenu>();
            colliderFollow.Target = btn.transform;
            colliderFollow.Rotation = Quaternion.Euler(90f, 0f, 0f);
            colliderFollow.Position = Vector3.zero;
            btnCollider.transform.localPosition = btn.transform.localPosition;
            btnCollider.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            btnCollider.transform.localScale = new Vector3(0.05f, 0.065f, 0.55f) * 0.35f;
            btnCollider.layer = 2;
            trigger = btnCollider.AddComponent<ButtonTrigger>();
            trigger.BtnIdentifier = btnName;
            trigger.CustomAction = customAction;
            trigger.ButtonRoot = btn.transform;
            btnCollider.AddComponent<ButtonCollider>().trigger = trigger;
            btnCollider.GetComponent<Collider>().isTrigger = true;
            Destroy(btnCollider.GetComponent<Rigidbody>());
            Renderer rendererCollider = btnCollider.GetComponentInChildren<Renderer>();
            if (rendererCollider != null)
            {
                rendererCollider.material.shader = ShaderCache.TextShader;
                rendererCollider.material.color = new Color32(255, 0, 0, 50);
                rendererCollider.enabled = false;
            }
            btnObjs.Add(btnCollider);
        }

        if (clickCollider == null)
            AddButtonRootPressFallback(btn.transform, trigger);



        trigger.Slider = slider;


        trigger.Knob = knob;


        trigger.IsToggle = isToggle;


        trigger.Cooldown = cooldown;

        MenuTheme.ApplyMenuButton(btn.transform, out Renderer renderer, out Renderer outlineRenderer);
        trigger.BodyRenderer = renderer;
        trigger.OutlineRenderer = outlineRenderer;

        TMP_Text text = GetButtonTitle(btn.transform);
        if (text != null)
            text.text = btnName;
        else
            Debug.LogError("[TUP] Text not found: " + btnName);

        Transform hoverBar = FindButtonChild(btn.transform, "HoverBar");
        if (hoverBar != null)
        {
            hoverBar.localScale = Vector3.zero;
            hoverBar.gameObject.SetActive(false);
            trigger.HoverBar = hoverBar;
        }
        if (text != null)
        {
            trigger.HoverTitle = text.transform;
            Vector3 titlePos = trigger.HoverTitle.localPosition;
            titlePos.x = 0.00943f;
            trigger.HoverTitle.localPosition = titlePos;
        }

        Transform hoverCollider = GetInteractiveCollider(btn.transform, "HoverCollider");
        if (hoverCollider != null)
        {
            hoverCollider.gameObject.layer = 2;
            Collider hc = hoverCollider.GetComponent<Collider>();
            if (hc == null) hc = hoverCollider.gameObject.AddComponent<BoxCollider>();
            hc.isTrigger = true;
            ConfigureInteractiveBox(hoverCollider, btn.transform, hc);

            Rigidbody hrb = hoverCollider.GetComponent<Rigidbody>();
            if (hrb == null) hrb = hoverCollider.gameObject.AddComponent<Rigidbody>();
            hrb.isKinematic = true;
            hrb.useGravity = false;
            ButtonHoverCollider hover = hoverCollider.GetComponent<ButtonHoverCollider>() ?? hoverCollider.gameObject.AddComponent<ButtonHoverCollider>();
            hover.trigger = trigger;
        }

        ButtonTrigger.RestoreCooldown(trigger);

        if (text != null)
        {
            if (btnName == "Nametag Size") { nameTagSizeText = text; text.text = "Nametag Size  :  " + Mods.GetNameTagSizeLabel(); }
            else if (btnName == "Nametag Fade Distance") { nameTagFadeDistanceText = text; text.text = "Nametag Fade Distance  :  " + Mods.GetNameTagFadeDistanceLabel(); }
        }

        if (isToggle && Mods.SavedToggleStates.TryGetValue(btnName, out bool savedOn) && savedOn)
        {
            trigger.IsOn = true;
            MenuEffects.SnapActivated(knob, slider, trigger.BodyRenderer, trigger.OutlineRenderer);
        }

        if (btnName.StartsWith("Saved Outfit #") && int.TryParse(btnName.Substring("Saved Outfit #".Length), out int outfitN))
        {
            if (text != null)
                text.text = Mods.GetOutfitDisplayName(outfitN);

            Transform renameSprite = FindButtonChild(btn.transform, "Rename");
            if (renameSprite != null) renameSprite.gameObject.SetActive(true);

            Transform renameCollider = FindButtonChild(btn.transform, "Rename/RenameCollider");
            if (renameCollider != null)
            {
                renameCollider.gameObject.SetActive(true);
                TMP_Text capturedLabel = text;
                int capturedN = outfitN;
                ButtonTrigger renameTrigger = WireInteractive(renameSprite != null ? renameSprite : btn.transform, btnName + "_Rename", () => Mods.StartRename(capturedN, capturedLabel, menuObj.transform.Find("Keyboard")?.gameObject), renameCollider);
            }
        }

        Vector3 targetScale = usingRuntimeTemplate ? btn.transform.localScale : buttonBaseScale;
        btn.transform.localScale = Vector3.zero;
        StartCoroutine(MenuEffects.PopButton(btn, targetScale, Vector3.zero, true));

        AudioSource audioSource = gameObject.GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        audioSource.volume = 0.1f;
        audioSource.PlayOneShot(btnEnterSound);

        btnObjs.Add(btn);
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
            if (key == keyboard || key.name.Equals("Base", StringComparison.OrdinalIgnoreCase) || key.name.Equals("Preview", StringComparison.OrdinalIgnoreCase))
                continue;

            string keyName = ResolveKeyboardKey(key);
            if (string.IsNullOrEmpty(keyName))
                continue;

            Transform keyRoot = GetKeyboardKeyRoot(key, keyboard);
            if (keyRoot == null || !keyRoots.Add(keyRoot))
                continue;

            bool protectedVisual = IsProtectedKeyboardVisualRoot(keyRoot, keyName);
            SetLayerRecursive(keyRoot, 2);

            Collider kc = keyRoot.GetComponent<Collider>();
            if (kc == null) kc = keyRoot.gameObject.AddComponent<BoxCollider>();
            ConfigureKeyboardCollider(keyRoot, kc);
            kc.isTrigger = true;

            Rigidbody krb = keyRoot.GetComponent<Rigidbody>();
            if (krb != null) Destroy(krb);

            ButtonTrigger trigger = keyRoot.GetComponent<ButtonTrigger>() ?? keyRoot.gameObject.AddComponent<ButtonTrigger>();
            trigger.IsKeyboardKey = true;
            trigger.BtnIdentifier = keyName;
            trigger.ButtonRoot = keyRoot;
            Renderer bodyRenderer = null;
            Renderer[] outlineRenderers = Array.Empty<Renderer>();
            if (!protectedVisual)
                MenuTheme.ApplyKeyboardKey(keyRoot, out bodyRenderer, out outlineRenderers);
            if (!protectedVisual)
            {
                Renderer siblingOutline = FindNearestKeyboardOutline(keyRoot, keyboardOutlines);
                if (siblingOutline != null)
                    outlineRenderers = outlineRenderers.Concat(new[] { siblingOutline }).Distinct().ToArray();
            }
            trigger.BodyRenderer = bodyRenderer;
            trigger.OutlineRenderer = outlineRenderers.Length > 0 ? outlineRenderers[0] : null;
            trigger.KeyboardOutlineRenderers = outlineRenderers;
            trigger.KeyboardImages = protectedVisual
                ? Array.Empty<Image>()
                : keyRoot.GetComponentsInChildren<Image>(true).Where(image => image != null).Distinct().ToArray();
            string captured = keyName;
            trigger.CustomAction = () => HandleKeyInput(captured);

            ButtonCollider keyCollider = keyRoot.GetComponent<ButtonCollider>() ?? keyRoot.gameObject.AddComponent<ButtonCollider>();
            keyCollider.trigger = trigger;
        }
    }



    private static bool IsKeyboardBasePath(Transform transform, Transform keyboard)
    {
        for (Transform current = transform; current != null && current != keyboard; current = current.parent)
            if (current.name.Equals("Base", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
    private static bool IsProtectedKeyboardVisualRoot(Transform keyRoot, string keyName)
    {
        if (keyRoot == null)
            return true;

        if (keyRoot.name.Equals("Base", StringComparison.OrdinalIgnoreCase))
            return true;

        return keyName.Equals("Enter", StringComparison.OrdinalIgnoreCase)
               || keyName.Equals("Delete", StringComparison.OrdinalIgnoreCase);
    }
    private static void ConfigureKeyboardCollider(Transform keyRoot, Collider collider)
    {
        BoxCollider box = collider as BoxCollider;
        if (box == null)
            return;

        RectTransform rect = keyRoot.GetComponent<RectTransform>();
        if (rect == null)
            rect = keyRoot.GetComponentInChildren<RectTransform>(true);
        if (rect == null)
            return;

        Vector2 size = rect.rect.size;
        if (size.sqrMagnitude <= 0.001f)
            size = rect.sizeDelta;
        if (size.sqrMagnitude <= 0.001f)
            return;

        box.center = Vector3.zero;
        box.size = new Vector3(Mathf.Max(0.02f, size.x), Mathf.Max(0.02f, size.y), 0.02f);
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

        return key.parent == keyboard || key.GetComponent<Collider>() != null || key.GetComponent<Renderer>() != null || key.GetComponent<Image>() != null || key.GetComponent<RawImage>() != null
            ? key
            : null;
    }

    private static string ResolveKeyboardKey(Transform key)
    {
        string keyName = NormalizeKeyboardKey(key.name);
        if (!string.IsNullOrEmpty(keyName))
            return keyName;

        TMP_Text text = key.GetComponent<TMP_Text>();
        if (text != null)
            return NormalizeKeyboardKey(text.text);

        foreach (Transform child in key)
        {
            if (!child.name.Equals("ButtonText", StringComparison.OrdinalIgnoreCase)
                && !child.name.Equals("Text", StringComparison.OrdinalIgnoreCase))
                continue;

            text = child.GetComponent<TMP_Text>();
            if (text != null)
                return NormalizeKeyboardKey(text.text);
        }

        return "";
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
            "SPACE" or "SPACEBAR" => "Space",
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
            targetScale = keyboard.transform.localScale * 1.35f;
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


    public void SetKeyboardPreview(GameObject keyboard, string text, bool animateLastCharacter)
    {
        if (keyboard == null)
            return;

        TMP_Text preview = GetKeyboardPreviewText(keyboard);
        if (preview == null)
            return;

        preview.gameObject.SetActive(true);
        preview.enabled = true;
        preview.text = text ?? "";
        preview.ForceMeshUpdate();

        if (keyboardPreviewCharRoutine != null)
        {
            StopCoroutine(keyboardPreviewCharRoutine);
            keyboardPreviewCharRoutine = null;
        }

        if (animateLastCharacter && !string.IsNullOrEmpty(preview.text))
            keyboardPreviewCharRoutine = StartCoroutine(ScaleKeyboardPreviewCharacter(preview, preview.text.Length - 1));
    }

    private TMP_Text GetKeyboardPreviewText(GameObject keyboard)
    {
        if (keyboard == null)
            return null;

        if (keyboardPreviewText != null && keyboardPreviewText.transform != null && keyboardPreviewText.transform.IsChildOf(keyboard.transform))
            return keyboardPreviewText;

        Transform previewText = keyboard.transform.Find("Preview/Text");
        if (previewText == null)
        {
            Transform preview = FindChildByName(keyboard.transform, "Preview");
            previewText = preview != null ? preview.Find("Text") ?? FindChildByName(preview, "Text") : null;
        }

        keyboardPreviewText = previewText != null
            ? previewText.GetComponent<TMP_Text>() ?? previewText.GetComponentInChildren<TMP_Text>(true)
            : null;

        if (keyboardPreviewText == null)
            Debug.LogWarning("[TUP] Keyboard preview text missing at Keyboard/Preview/Text");

        return keyboardPreviewText;
    }

    private IEnumerator ScaleKeyboardPreviewCharacter(TMP_Text preview, int charIndex)
    {
        const float duration = 0.16f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            float scale = Mathf.Lerp(0.05f, 1f, t * t * (3f - 2f * t));
            ApplyKeyboardPreviewCharacterScale(preview, charIndex, scale);
            elapsed += Time.deltaTime;
            yield return null;
        }

        ApplyKeyboardPreviewCharacterScale(preview, charIndex, 1f);
        if (keyboardPreviewCharRoutine != null)
            keyboardPreviewCharRoutine = null;
    }

    private static void ApplyKeyboardPreviewCharacterScale(TMP_Text preview, int charIndex, float scale)
    {
        if (preview == null || charIndex < 0)
            return;

        preview.ForceMeshUpdate();
        TMP_TextInfo textInfo = preview.textInfo;
        if (textInfo == null || charIndex >= textInfo.characterCount)
            return;

        TMP_CharacterInfo charInfo = textInfo.characterInfo[charIndex];
        if (!charInfo.isVisible)
            return;

        int materialIndex = charInfo.materialReferenceIndex;
        int vertexIndex = charInfo.vertexIndex;
        Vector3[] vertices = textInfo.meshInfo[materialIndex].vertices;
        Vector3 center = (vertices[vertexIndex] + vertices[vertexIndex + 2]) * 0.5f;

        for (int i = 0; i < 4; i++)
            vertices[vertexIndex + i] = center + ((vertices[vertexIndex + i] - center) * scale);

        preview.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
    }
    private void HandleKeyInput(string key)
    {
        switch (key)
        {
            case "Enter":  Mods.ConfirmRename(); break;
            case "Delete": Mods.DeleteChar();    break;
            case "Space":  Mods.TypeChar(' ');   break;
            case "Close":  Mods.CancelRename();  break;
            default:
                if (key.Length == 1) Mods.TypeChar(key[0]);
                break;
        }
    }

    public void ConfigureKeyboardMenuFollow()
    {
        if (menuObj == null)
            return;

        Camera camera = GetActiveCamera();
        if (camera == null)
            return;

        SmoothFollowMenu smooth = menuObj.GetComponent<SmoothFollowMenu>() ?? menuObj.AddComponent<SmoothFollowMenu>();
        smooth.Target = camera.transform;
        smooth.YawOnly = true;
        smooth.UseYawDeadzone = true;
        smooth.YawActivationDegrees = 30f;
        smooth.Smoothing = 12f;
        smooth.LocalPosition = new Vector3(0f, -0.05f, 0.6f);
        smooth.LocalRotation = Quaternion.Euler(0f, 270f, 0f);
        smooth.Frozen = false;
        smooth.ResetDeadzoneAnchor();
        EnsureKeyboardLeftSelector();
    }

    public void RestoreMenuFollowAfterKeyboard()
    {
        if (menuObj == null)
            return;

        DestroyKeyboardLeftSelector();

        Transform parent = currentOpenType == MenuOpenType.Head
            ? GetActiveCamera().transform
            : GTPlayer.Instance.LeftHand.controllerTransform;

        SmoothFollowMenu smooth = menuObj.GetComponent<SmoothFollowMenu>() ?? menuObj.AddComponent<SmoothFollowMenu>();
        smooth.YawOnly = false;
        smooth.UseYawDeadzone = false;
        smooth.Target = parent;
        smooth.Smoothing = 12f;
        smooth.Frozen = false;

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

        smooth.ResetDeadzoneAnchor();
        smooth.Snap();
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
        
        buttonBundle             = LoadBundle(assembly, "ThatUtilsPad.Assets.Models.buttonmodelui2");
        menuReduxBundle          = LoadBundle(assembly, "ThatUtilsPad.Assets.Models.tup-overv15");
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