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
    private const string AdminsUrl      = "https://playfabswapping.hu/data/tup/admins.json";
    private const string KeysUrl        = "https://playfabswapping.hu/data/tup/keys/keys.json";
    private const string ApiUrl         = "https://playfabswapping.hu/data/tup/keys/api.php";
    private const string AdminKeyBypass = "mc-is-trash-peakest";
    private const string KeyFile        = "BepInEx/config/tup_key.txt";

    private const bool UseSakuraTheme   = true;
    private const bool AlwaysShowMenu   = false;
    private const bool toggleMenuMethod = true;

    public static Main Instance;
    public bool   IsAdmin;
    public string AdminName = "";

    private bool       isKeyValid = false;
    private GameObject keyInputUI;

    private readonly Color32          accentColor = new(203, 166, 247, 255);
    private readonly Color32          borderColor = new(12,  12,  22,  255);
    private readonly List<GameObject> btnObjs     = [];
    private readonly List<GameObject> selectorBtnObjs = new List<GameObject>();

    private readonly Vector3    buttonBasePosition = new(-0.044f, 0f, 0f);
    private readonly Quaternion buttonBaseRotation = Quaternion.Euler(0f, 0f, 180f);
    private readonly Vector3    buttonBaseScale    = Vector3.one * 1.2f;

    private readonly Color32 buttonColor        = new(30, 30, 46, 255);
    private readonly Color32 buttonOutlineColor = new(18, 18, 36, 255);

    private bool previousControllerState;

    private readonly Color32 mainColor = new(17, 17, 27, 255);

    private readonly Vector3 menuGripPosition = new(0f, -0.17f, 0f);
    private readonly Vector3 menuHandOffset   = new(0f,  0f,    0f);

    private GameObject  btnPrefab;
    private AssetBundle buttonBundle;

    private MenuOpenType currentOpenType;
    private AudioClip    helloSound;
    private bool         isMenuOpened = false;

    private AssetBundle menuBundle;
    private float       menuGripRotaton = -30f;

    private GameObject  selectorObj;
    private GameObject  menuObj;
    private AssetBundle sakuraBundle;
    private AssetBundle menuPanelExpansionBundle;

    private List<string> categories;
    private string currentCategory = "Networking";
    
    private AudioClip  startSound;
    private GameObject stumpInfo;

    public static Camera FirstPersonCamera { get; private set; }
    public static Camera ThirdPersonCamera { get; private set; }

    private void Awake() => Instance = this;

    private void Start()
    {
        Debug.Log("\n \n" +
                  "   ================:  ┌○○○─TUP────────────────────────────── x ┐\n" +
                  "   .::=*=-+*--++-:.   ┣────────────────────────────────────────┫\n" +
                  "   .+*************-   │ TUP: ThatUtilsPad                      │\n" +
                  "      :*.     ++.     │ Discord: https://discord.gg/fuJcTWsn   │\n" +
                  "      :*.     ++.     │ Made by Jelly and Kwyf <3              │\n" +
                  "                      └────────────────────────────────────────┘\n");

        GorillaTagger.OnPlayerSpawned(OnPlayerSpawned);
        Debug.Log(GenHWID());
    }

    private void OnPlayerSpawned()
    {
        FirstPersonCamera = GTPlayer.Instance.mainCamera;
        ThirdPersonCamera = GorillaTagger.Instance.thirdPersonCamera.transform.GetChild(0).GetComponent<Camera>();

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
            statusText.text  = "invalid key — contact support";
            btn.interactable = true;
        }
    }

    private void OnKeyAccepted()
    {
        Debug.Log("[TUP] key accepted, loading mod");
        PlayStartSound();
        CheckAdminStatus();
        Mods.Init();
        categories = Mods.Actions.Keys.ToList();
        LoadBundles();
        ShaderCache.Init();
        new GameObject("TUP_CoroutineHandler").AddComponent<CoroutineHandler>();
        btnPrefab = buttonBundle.LoadAsset<GameObject>("assets/prefabs/tup-buttonmodel.prefab");
    }

    private void Update()
    {
        if (!isKeyValid)
            return;

        bool currentControllerState     = ControllerInputPoller.instance.leftControllerSecondaryButton;
        bool controllerPressedThisFrame = currentControllerState && !previousControllerState;
        previousControllerState = currentControllerState;

        bool keyboardPressedThisFrame = Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame;
        bool keyboardHeld             = Keyboard.current != null && Keyboard.current.tKey.isPressed;

        if (toggleMenuMethod && !AlwaysShowMenu)
        {
            bool togglePressed = controllerPressedThisFrame || keyboardPressedThisFrame;

            if (togglePressed)
            {
                if (isMenuOpened)
                {
                    CloseMenu();
                    isMenuOpened = false;
                }
                else
                {
                    currentOpenType = currentControllerState ? MenuOpenType.Hand : MenuOpenType.Head;
                    OpenMenu();
                    isMenuOpened = true;
                    CreateButtons();
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
                CreateButtons();
            }
            else if (isMenuOpened && !controllerHeld && !keyboardIsHeld && !AlwaysShowMenu)
            {
                CloseMenu();
                isMenuOpened = false;
            }
        }

        if (!isMenuOpened)
            return;

        if (!Mouse.current.leftButton.wasPressedThisFrame)
            return;

        Camera raycastCamera = GetActiveCamera();
        Ray    ray           = raycastCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f, 1 << 18, QueryTriggerInteraction.Collide))
            return;

        StartCoroutine(MenuEffects.SpawnHitCircle(hit.point, hit.transform));

        if (hit.collider.TryGetComponent(out ButtonTrigger buttonTrigger))
            ButtonTrigger.PcPress(buttonTrigger);
    }

    private string GenHWID() => SystemInfo.deviceUniqueIdentifier;

    private void CreateButtons(string categoryName = "Networking")
    {
        const float startY = 0.38f;
        const float gap    = 0.13f;
        int index = 0;

        if (!Mods.Actions.TryGetValue(currentCategory, out var category))
            return;
        
        foreach (var mod in category)
        {
            float height = startY - (index * gap);
            CreateButton(height, mod.Key);
            index++;
        }
    }
    
    private void SwitchCategory(string categoryName)
    {
        currentCategory = categoryName;

        DestroyButtons();
        CreateButtons();
    }

    private void InitPageButtons(GameObject menuObj)
    {
        List<string> categories = Mods.Actions.Keys.ToList();

        foreach (Transform child in menuObj.GetComponentsInChildren<Transform>(true))
        {
            if (!child.name.Contains("SelectorBtn", StringComparison.OrdinalIgnoreCase))
                continue;
            
            child.gameObject.layer = 18;

            var trigger = child.gameObject.AddComponent<ButtonTrigger>();
            Collider col = child.gameObject.GetComponent<Collider>();
            if (col == null)
                col = child.gameObject.AddComponent<BoxCollider>();
            col.isTrigger = true;
            Destroy(child.gameObject.GetComponent<Rigidbody>());
            
            Vector3 previousLocalPos = child.transform.localPosition;
            Quaternion previousLocalRot = child.transform.localRotation;

            child.transform.parent = null;

            var follow = child.AddComponent<FollowMenu>();
            follow.Target = menuObj.transform;
            follow.Position = previousLocalPos;
            follow.Rotation = previousLocalRot;
            
            Renderer rend = child.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.material.shader = ShaderCache.UberShader;
                rend.material.color = buttonColor;
            }
            
            //if (child.transform.parent != null)
            //    Debug.Log($"[TUP] Parent: {child.transform.parent}");
            //else
            //    Debug.Log($"[TUP] No parent found");

            string numberPart = child.name.Replace("SelectorBtn", "");
            if (!int.TryParse(numberPart, out int index))
                continue;

            index -= 1;
            if (index < 0 || index >= categories.Count)
                continue;

            string categoryName = categories[index];
            
            trigger.CustomAction = () => SwitchCategory(categoryName);
            //Debug.Log($"[TUP] Assigned {child.name} -> {categoryName}");
            
            selectorBtnObjs.Add(child.gameObject);
        }
    }

    private void LoadAudio()
    {
        try
        {
            Assembly assembly      = Assembly.GetExecutingAssembly();
            string[] resourceNames = assembly.GetManifestResourceNames();

            Debug.Log("[TUP] embedded resources:");
            foreach (string name in resourceNames)
                Debug.Log("[TUP] - " + name);

            string startPath = null;
            string helloPath = null;

            foreach (string name in resourceNames)
            {
                if (name.Contains("tup_startup") || name.Contains("startup"))
                {
                    startPath = name;
                    Debug.Log("[TUP] found startup sound at: " + name);
                }

                if (name.Contains("hello"))
                    helloPath = name;
            }

            if (startPath != null)
                startSound = LoadAudioClip(assembly, startPath);
            else
                Debug.LogWarning("[TUP] could not find startup sound");

            if (helloPath != null)
                helloSound = LoadAudioClip(assembly, helloPath);
            else
                Debug.LogWarning("[TUP] could not find hello sound");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TUP] failed to load audio: " + e.Message);
        }
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
                    SpeakWelcome(AdminName);
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

    private void CloseMenu()
    {
        isMenuOpened = false;
        Destroy(menuObj);
        DestroyButtons();
        menuObj = null;
        
        foreach (GameObject selectorObj in selectorBtnObjs)
            Destroy(selectorObj);
        selectorBtnObjs.Clear();
    }

    private void OpenMenu()
    {
        string prefabPath = UseSakuraTheme
            ? "assets/prefabs/tup-model-panelexpansion.prefab"
            : "assets/prefabs/tup-modelsmooth.prefab";

        AssetBundle bundle = UseSakuraTheme ? menuPanelExpansionBundle : menuBundle;
        GameObject  prefab = bundle.LoadAsset<GameObject>(prefabPath);

        Transform parent = currentOpenType == MenuOpenType.Head
            ? GetActiveCamera().transform
            : GTPlayer.Instance.LeftHand.controllerTransform;

        menuObj = Instantiate(prefab, parent, true);
        menuObj.transform.localScale = Vector3.one * 0.625f;
        
        InitPageButtons(menuObj);

        if (currentOpenType == MenuOpenType.Head)
        {
            menuObj.transform.localPosition = new Vector3(-0.15f, -0.07f, 1.2f);
            menuObj.transform.localRotation = Quaternion.Euler(0f, 270f, 0f);
        }
        else
        {
            menuObj.transform.localPosition = Vector3.zero + menuHandOffset + menuGripPosition;
            menuObj.transform.localRotation = Quaternion.Euler(270f, 180f, 0f);
        }

        Rigidbody? rb       = menuObj.GetComponent<Rigidbody>();
        Collider?  collider = menuObj.GetComponent<Collider>();
        Renderer?  renderer = menuObj.GetComponentInChildren<Renderer>();

        if (rb       != null) Destroy(rb);
        if (collider != null) Destroy(collider);

        if (renderer != null)
        {
            renderer.material.shader = ShaderCache.UberShader;
            renderer.material.color  = new Color32(171, 0, 63, 255);
        }

        if (UseSakuraTheme)
            MenuTheme.AssignSakura(menuObj, mainColor, borderColor, accentColor);
        else
            MenuTheme.Assign(menuObj, mainColor, borderColor, buttonColor, accentColor);
    }

    private void CreateButton(float zOffset, string btnName)
    {
        GameObject btn         = Instantiate(btnPrefab, menuObj.transform);
        GameObject btnOutline  = Instantiate(btnPrefab, menuObj.transform);
        GameObject btnCollider = GameObject.CreatePrimitive(PrimitiveType.Cube);

        var colliderFollow    = btnCollider.AddComponent<FollowMenu>();
        colliderFollow.Target   = btn.transform;
        colliderFollow.Rotation = Quaternion.identity;
        colliderFollow.Position = Vector3.zero;

        Vector3 stackedPos = buttonBasePosition + new Vector3(0f, zOffset, 0f);

        btn.transform.localPosition        = stackedPos;
        btn.transform.localRotation        = buttonBaseRotation;
        btn.transform.localScale           = buttonBaseScale;

        btnOutline.transform.localPosition = stackedPos;
        btnOutline.transform.localRotation = buttonBaseRotation;
        btnOutline.transform.localScale    = buttonBaseScale * 1.05f;

        btnCollider.transform.localPosition = stackedPos;
        btnCollider.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
        btnCollider.transform.localScale    = new Vector3(0.05f, 0.065f, 0.45f) * 0.8f;
        btnCollider.layer = 18;

        ButtonTrigger trigger = btnCollider.AddComponent<ButtonTrigger>();
        trigger.BtnIdentifier         = btnName;
        trigger.pressButtonSoundIndex = 28;

        btnCollider.GetComponent<Collider>().isTrigger = true;
        Destroy(btnCollider.GetComponent<Rigidbody>());

        Renderer renderer = btn.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            renderer.material.shader = ShaderCache.UberShader;
            renderer.material.color  = buttonColor;
        }

        Renderer rendererOutline = btnOutline.GetComponentInChildren<Renderer>();
        if (rendererOutline != null)
        {
            rendererOutline.material.shader = ShaderCache.UberShader;
            rendererOutline.material.color  = buttonOutlineColor;
        }

        Renderer rendererCollider = btnCollider.GetComponentInChildren<Renderer>();
        rendererCollider.material.shader = ShaderCache.TextShader;
        rendererCollider.material.color  = new Color32(255, 0, 0, 50);
        rendererCollider.enabled         = false;

        GameObject textObj = new("ButtonLabel");
        textObj.transform.SetParent(btn.transform, false);
        textObj.transform.localPosition = new Vector3(0.02f, 0.003f, 0f);
        textObj.transform.localRotation = Quaternion.Euler(0f, 270f, 180f);

        TextMeshPro text = textObj.AddComponent<TextMeshPro>();
        text.text             = btnName.ToUpper();
        text.fontSize         = 20;
        text.alignment        = TextAlignmentOptions.Center;
        text.color            = Color.white;
        text.font             = VRRig.LocalRig.playerText1.font;
        text.enableAutoSizing = false;
        text.transform.localScale = Vector3.one * 0.02f;

        btnObjs.Add(btn);
        btnObjs.Add(btnOutline);
        btnObjs.Add(btnCollider);
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

        menuBundle               = LoadBundle(assembly, "ThatUtilsPad.Assets.tup-prefab");
        sakuraBundle             = LoadBundle(assembly, "ThatUtilsPad.Assets.tupsakura-prefab");
        menuPanelExpansionBundle = LoadBundle(assembly, "ThatUtilsPad.Assets.tupmenu-panelexpansion");
        buttonBundle             = LoadBundle(assembly, "ThatUtilsPad.Assets.tupbutton-prefab");
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