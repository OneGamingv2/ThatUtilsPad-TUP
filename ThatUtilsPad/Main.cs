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
public partial class Main : BaseUnityPlugin
{
    private const string AdminsUrl      = "https://playfabswapping.hu/admin/admins.json";

    private const bool UseSakuraTheme   = true;
    private const bool AlwaysShowMenu   = false;
    private const bool toggleMenuMethod = true;
    private bool       menuMadeAlready  = false;
    private bool       modAwakeAlready   = false;
    private int        currentPage      = 0;
    private const int  PageSize         = 7;
    private static readonly MenuThemePalette Theme = MenuTheme.Themes.Sakura;

    public static Main Instance;
    public bool   IsAdmin;
    public string AdminName = "";

    private readonly List<GameObject> buttons     = [];
    private readonly List<GameObject> tabButtonObjs = new List<GameObject>();
    private readonly List<GameObject> pageButtonObjs      = new List<GameObject>();
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

    private float menuOffsetRight = 0.03f;
    private float menuOffsetUp = -0.13f;
    private float menuOffsetOut = 0.024f;
    private readonly Vector3 menuHandOffset = new(0f, 0f, 0f);
    private float menuGripTilt = 45f;

    private GameObject  btnPrefab;
    private Transform buttonShelf;
    private GameObject buttonSeed;
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
    private float       menuScale = 0.375f;
    private float       headMenuDistance = 0.6f;
    private const float HeadMenuX = -0.03f;
    private const float HeadMenuY = -0.02f;
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
    private float lastVolumeDisplayWidth = float.NaN;
    private Image checkerMonkeColorImage;
    private SpriteRenderer checkerMonkeColorSprite;
    private Image checkerPlatformIconImage;
    private Image moreInfoPlatformIconImage;
    private Sprite platformSpriteSteam;
    private Sprite platformSpriteMeta;
    private Sprite platformSpritePc;
    private Sprite platformSpriteUnknown;
    private bool platformSpritesLoaded;
    private string lastCheckerName;
    private string lastCheckerFpsPing;
    private string lastCheckerPlatform;
    private string lastCheckerDate;
    private Color lastCheckerColor;
    private bool hasLastCheckerColor;
    private string lastCheckerLegalMods;
    private string lastCheckerIllegalMods;
    private string[] lastCheckerUnknownPropList = Array.Empty<string>();
    private string lastCheckerColorStr = "--";
    private Transform cheatsTitleTransform;
    private Transform modsTitleTransform;
    private bool moreInfoVisible;
    private bool moreInfoModsExpanded;
    private TMP_Text moreInfoBodyText;
    private MoreInfoModEntry[] moreInfoModList = Array.Empty<MoreInfoModEntry>();
    private int moreInfoModsPage;
    private const int MoreInfoModsPerPage = 6;
    private Transform moreInfoNextArrow;
    private Transform moreInfoBackArrow;
    private TMP_Text moreInfoPageText;
    private ButtonTrigger moreInfoOpenTrigger;

    private enum MoreInfoModKind
    {
        Legal,
        Illegal,
        Unknown
    }

    private struct MoreInfoModEntry
    {
        public string Name;
        public MoreInfoModKind Kind;

        public MoreInfoModEntry(string name, MoreInfoModKind kind)
        {
            Name = name;
            Kind = kind;
        }
    }

    private const float VolumeDisplayMaxWidth = 406.54f;
    private const float VolumeDisplayTweenDuration = 0.5f;

    private sealed class CheckerTileGroup
    {
        public string TitleName;
        public GameObject[] Tiles = Array.Empty<GameObject>();
        public TMP_Text[] Texts = Array.Empty<TMP_Text>();
        public RectTransform[] TileRects = Array.Empty<RectTransform>();
        public RectTransform[] OutlineRects = Array.Empty<RectTransform>();
        public GameObject[] BackObjs = Array.Empty<GameObject>();
        public RectTransform[] BackRects = Array.Empty<RectTransform>();
        public Vector2[] BackSizes = Array.Empty<Vector2>();
        public GameObject NoneTitle;
        public RectTransform NoneRect;
        public TMP_Text NoneText;
        public TMP_Text PageText;
        public string[] Values = Array.Empty<string>();
        public Coroutine Routine;
        public int FocusedIndex = -1;
        public int FocusedValueIndex = -1;
        public int PageIndex;
        public Transform NextArrow;
        public Transform BackArrow;
        public bool OwnsPageArrows;
    }
    
    public static TMP_Text queueText;
    public static TMP_Text modeText;
    public static TMP_Text regionText;
    public static TMP_Text clickSoundText;
    public static TMP_Text startupSoundText;
    public static TMP_Text menuOpenSoundText;
    public static TMP_Text buttonPopSoundText;
    public static TMP_Text notifSoundText;
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
    
    public static TMP_Text volumeText;

    public static Camera FirstPersonCamera { get; private set; }
    public static Camera ThirdPersonCamera { get; private set; }
    
    public static Dictionary<string, float> VolumeByPlayerID = new Dictionary<string, float>();

    private void Awake()
    {
        Instance = this;
        CameraMod.Camera.Patches.HarmonyPatcher.ApplyHarmonyPatches();
    }

    private void Start()
    {
        GorillaTagger.OnPlayerSpawned(OnPlayerSpawned);
    }

    private void OnDestroy()
    {
        DestroySpotifyHudResources();
        Mods.ShutdownAnticheatHud();
        Mods.Shutdown();
        Mods.ShutdownSpotifyMedia();
        if (ReferenceEquals(Instance, this))
            Instance = null;
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
        WakeUpMod();
    }

    private void WakeUpMod()
    {
        if (modAwakeAlready) return;
        modAwakeAlready = true;
        CheckAdminStatus();
        Mods.Init();
        PlayStartSound();
        LoadBundles();
        FontCache.LoadFonts();
        ShaderCache.Init();
        new GameObject("TUP_CoroutineHandler").AddComponent<CoroutineHandler>();
        CameraMod.Camera.Patches.StartPatch.EnsureStarted();
        Mods.StartEnabledLoops();
        btnPrefab = buttonBundle.LoadAsset<GameObject>("assets/prefabs/buttonmodelui2.prefab");
        InitMenu();
        InitSettings();
        Mods.ApplySavedSettings();
        Mods.StartRegionStatsRefresh();
        InitStumpCreditText();
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
        
        Transform queueButton = FindChildByName(menuObj.transform, "Queue");
        TMP_Text qText = queueButton != null ? GetButtonTitle(queueButton) : null;
        if (qText != null)
        {
            queueText = qText;
            queueText.text = GetButtonDisplayText("Queue");
        }
        Transform queueSliderTransform = FindButtonChild(queueButton, "Slider");
        Transform queueArrowTransform = FindButtonChild(queueButton, "NextArrow");

        if (queueSliderTransform != null && queueArrowTransform != null)
        {
            queueSliderTransform.transform.gameObject.SetActive(false);
            queueArrowTransform.transform.gameObject.SetActive(true);
        }

        Transform modeButton = FindChildByName(menuObj.transform, "Mode");
        TMP_Text mText = modeButton != null ? GetButtonTitle(modeButton) : null;
        if (mText != null)
        {
            modeText = mText;
            modeText.text = GetButtonDisplayText("Mode");
        }
        Transform modeSliderTransform = FindButtonChild(modeButton, "Slider");
        Transform modeArrowTransform = FindButtonChild(modeButton, "NextArrow");

        if (modeSliderTransform != null && modeArrowTransform != null)
        {
            modeSliderTransform.transform.gameObject.SetActive(false);
            modeArrowTransform.transform.gameObject.SetActive(true);
        }

        InitCycleButtonText("Region", ref regionText, "Region  :  " + Mods.GetRegionLabel());
        InitCycleButtonText("Click Sound", ref clickSoundText, "Click Sound  :  " + Mods.GetClickSoundLabel());
        InitCycleButtonText("Startup Sound", ref startupSoundText, "Startup Sound  :  " + Mods.GetStartupSoundLabel());
        InitCycleButtonText("Menu Open Sound", ref menuOpenSoundText, "Menu Open Sound  :  " + Mods.GetMenuOpenSoundLabel());
        InitCycleButtonText("Button Pop Sound", ref buttonPopSoundText, "Button Pop Sound  :  " + Mods.GetButtonPopSoundLabel());
        InitCycleButtonText("Notif Sound", ref notifSoundText, "Notif Sound  :  " + Mods.GetNotifSoundLabel());

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
        Mods.AutoScanLoop();
        Mods.PadNetworkingLoop();
        Mods.AnticheatHudLoop();
        UpdateSpotifyHudLoop();
        UpdatePlayerModelPreviewLive();
        
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
                        if (isMenuOpened)
                            Mods.BroadcastPadNetworkState(force: true);
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
                    if (isMenuOpened)
                        Mods.BroadcastPadNetworkState(force: true);
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
            return;
        }

        StartCoroutine(MenuEffects.SpawnHitCircle(hit.point, hit.transform));

        VolumeSliderCollider slider = IsInputColliderObject(hit.collider) ? hit.collider.GetComponent<VolumeSliderCollider>() : null;
        if (slider != null)
        {
            slider.SetFromWorldPoint(hit.point);
            return;
        }

        if (!hit.collider.TryGetComponent(out ButtonTrigger buttonTrigger))
            buttonTrigger = hit.collider.GetComponentInParent<ButtonTrigger>();
        if (buttonTrigger == null)
        {
            ButtonCollider buttonCollider = hit.collider.GetComponent<ButtonCollider>() ?? hit.collider.GetComponentInParent<ButtonCollider>();
            buttonTrigger = buttonCollider != null ? buttonCollider.trigger : null;
        }
        if (buttonTrigger != null)
            ButtonTrigger.PcPress(buttonTrigger);
    }
    
    private enum MenuOpenType { Hand, Head }
}
