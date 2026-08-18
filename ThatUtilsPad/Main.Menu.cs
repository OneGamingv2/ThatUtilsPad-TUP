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

public partial class Main
{
    private void InitMenu()
    {
        if (menuMadeAlready)
            return;

        AssetBundle bundle = menuReduxBundle != null ? menuReduxBundle : menuBundle;
        string prefabPath = bundle == menuReduxBundle
            ? "assets/prefabs/TUP-overv18.prefab"
            : "assets/prefabs/tup-modelsmooth.prefab";

        if (bundle == null)
        {
            Debug.LogError("[TUP] Menu asset bundle missing");
            return;
        }

        GameObject prefab = bundle.LoadAsset<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError("[TUP] Menu prefab missing: " + prefabPath);
            return;
        }

        menuObj = Instantiate(prefab);
        menuObj.transform.localScale = Vector3.one * menuScale;
        
        volumeText = menuObj.transform.Find("SideHolder/VolumeControls/Main/Volume")?.GetComponent<TMP_Text>()
                     ?? menuObj.transform.Find("SideHolder/VolumePercent")?.GetComponent<TMP_Text>();
        GrabButtonSeed();
        
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

        MenuTheme.Assign(menuObj, Theme);

        InitNavButtons(menuObj);
        InitSpotifyHudPage(menuObj);
        CachePageVisDots(menuObj);
        UpdatePageIndicators();
        InitKeyboard();
        PutCollidersBackInvis();

        menuObj.SetActive(false);
        menuMadeAlready = true;
        StartCoroutine(RoomInfoUpdater());
    }
    
    private bool OpenMenu()
{
    if (!menuMadeAlready || menuObj == null || isMenuClosing)
        return false;

    Camera activeCamera = GetActiveCamera();
    Transform parent = currentOpenType == MenuOpenType.Head  
        ? activeCamera != null ? activeCamera.transform : null
        : GetHandMenuTarget();

    if (parent == null)
        return false;

    menuObj.transform.SetParent(null);

    SmoothFollowMenu smooth = menuObj.GetComponent<SmoothFollowMenu>() ?? menuObj.AddComponent<SmoothFollowMenu>();
    smooth.YawOnly = false;
    smooth.Target = parent;
    smooth.Smoothing = menuSmoothingEnabled ? menuSmoothingStrength : 1000f;

    if (currentOpenType == MenuOpenType.Head)
    {
        smooth.LocalPosition = new Vector3(HeadMenuX, HeadMenuY, headMenuDistance);
        smooth.LocalRotation = GetHeadMenuLocalRotation();
        smooth.Snap();
    }
    else
    {

        smooth.LocalPosition = GetHandMenuLocalPosition();
        smooth.LocalRotation = GetHandMenuLocalRotation();
        smooth.Snap();
    }
    
    Tools.RememberPanelSizes(menuObj.transform);
    
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
    
    foreach (GameObject selectorObj in tabButtonObjs)
    {
        selectorObj.SetActive(true);
    }

    foreach (GameObject navBtn in pageButtonObjs)
        navBtn.SetActive(true);

    menuObj.SetActive(true);

    if (Mods.IsMenuOpenSoundEnabled())
        PlayUiClip(menuOpenSound);

    StartCoroutine(
            MenuEffects.PopMenu(
            menuObj,
            Vector3.one * menuScale,
            Vector3.zero,
            true
        )
    );

    StartCoroutine(MenuEffects.CheckerCompEnum(menuObj.transform));

    DestroyButtons();
    InitSelectorObj();
    buttonRoutine = StartCoroutine(CreateButtons());
    Mods.BroadcastPadNetworkState(force: true);
    return true;
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

    private Transform FindMainMenuPanel()
    {
        Transform direct = menuObj != null ? menuObj.transform.Find("MainMenu") : null;
        if (direct != null)
            return direct;

        return menuObj != null
            ? menuObj.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t != null && t.name.Equals("MainMenu", StringComparison.OrdinalIgnoreCase) && FindChildByName(t, "ButtonsHolder") != null)
            : null;
    }

    private void GrabButtonSeed()
    {
        Transform mainMenu = FindMainMenuPanel();
        buttonShelf = mainMenu != null ? mainMenu.Find("ButtonsHolder") ?? FindChildByName(mainMenu, "ButtonsHolder") : null;
        buttonSeed = null;
        if (buttonShelf != null)
        {
            buttonSeed = buttonShelf.Find("Button")?.gameObject
                ?? buttonShelf.Find("ButtonTemplate")?.gameObject
                ?? FindChildByName(buttonShelf, "Button")?.gameObject
                ?? FindChildByName(buttonShelf, "ButtonTemplate")?.gameObject;
        }
        if (buttonSeed == null)
        {
            Debug.LogError("[TUP] ButtonsHolder/Button seed missing — menu rows may sit in the wrong place");
            return;
        }

        buttonSeed.SetActive(false);
        buttonSeed.name = "ButtonTemplate";
    }

    private static TMP_Text GetButtonTitle(Transform button)
    {
        Transform title = button?.Find("MainUI/Title") ?? FindChildByName(button, "Title") ?? button?.Find("ButtonText");
        return title != null ? title.GetComponent<TMP_Text>() ?? title.GetComponentInChildren<TMP_Text>(true) : null;
    }

    private static string GetButtonDisplayText(string btnName)
    {
        if (string.Equals(btnName, "Queue", StringComparison.OrdinalIgnoreCase))
        {
            string queue = GorillaComputer.instance != null ? GorillaComputer.instance.currentQueue : "";
            if (string.IsNullOrWhiteSpace(queue)) queue = "Casual";
            return "Queue  :  " + FormatLabelValue(queue);
        }

        if (string.Equals(btnName, "Mode", StringComparison.OrdinalIgnoreCase))
        {
            string mode = "";
            if (GorillaComputer.instance != null && GorillaComputer.instance.currentGameMode != null)
                mode = GorillaComputer.instance.currentGameMode.Value;
            if (string.IsNullOrWhiteSpace(mode)) mode = "Casual";
            return "Mode  :  " + FormatLabelValue(mode);
        }

        if (string.Equals(btnName, "Region", StringComparison.OrdinalIgnoreCase))
            return "Region  :  " + Mods.GetRegionLabel();

        if (string.Equals(btnName, "Lobby Map", StringComparison.OrdinalIgnoreCase))
            return "Lobby Map  :  " + Mods.GetLobbyMapLabel();

        if (string.Equals(btnName, "Search Room", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Search", StringComparison.OrdinalIgnoreCase))
            return "Search";

        if (string.Equals(btnName, "Scan All", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Scan Lobby", StringComparison.OrdinalIgnoreCase))
            return "Scan All";

        if (string.Equals(btnName, "Aim Select", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Select User", StringComparison.OrdinalIgnoreCase))
            return "Aim Select";

        if (string.Equals(btnName, "Clear", StringComparison.OrdinalIgnoreCase))
            return "Clear";

        if (string.Equals(btnName, "Click Sound", StringComparison.OrdinalIgnoreCase))
            return "Click Sound  :  " + Mods.GetClickSoundLabel();

        if (string.Equals(btnName, "Startup Sound", StringComparison.OrdinalIgnoreCase))
            return "Startup Sound  :  " + Mods.GetStartupSoundLabel();

        if (string.Equals(btnName, "Menu Open Sound", StringComparison.OrdinalIgnoreCase))
            return "Menu Open Sound  :  " + Mods.GetMenuOpenSoundLabel();

        if (string.Equals(btnName, "Button Pop Sound", StringComparison.OrdinalIgnoreCase))
            return "Button Pop Sound  :  " + Mods.GetButtonPopSoundLabel();

        if (string.Equals(btnName, "Notif Sound", StringComparison.OrdinalIgnoreCase))
            return "Notif Sound  :  " + Mods.GetNotifSoundLabel();

        if (string.Equals(btnName, "Master Volume", StringComparison.OrdinalIgnoreCase))
            return "Master Volume  :  " + Mods.GetMasterVolumeLabel();

        if (string.Equals(btnName, "Menu Smoothing", StringComparison.OrdinalIgnoreCase))
            return "Menu Smoothing  :  " + (Mods.IsSmoothMenuEnabled() ? "On" : "Off");

        if (string.Equals(btnName, "Smooth Strength", StringComparison.OrdinalIgnoreCase))
            return "Smooth Strength  :  " + Mods.GetSmoothingStrengthLabel();

        if (string.Equals(btnName, "Menu Scale", StringComparison.OrdinalIgnoreCase))
            return "Menu Scale  :  " + Mods.GetMenuScaleLabel();

        if (string.Equals(btnName, "PC Distance", StringComparison.OrdinalIgnoreCase))
            return "PC Distance  :  " + Mods.GetHeadDistanceLabel();

        if (string.Equals(btnName, "Double Open", StringComparison.OrdinalIgnoreCase))
            return "Double Open  :  " + (Mods.IsDoubleClickOpenEnabled() ? "On" : "Off");

        if (string.Equals(btnName, "Open Bind", StringComparison.OrdinalIgnoreCase))
            return "Open Bind  :  " + GetMenuOpenBindDisplayName(Mods.GetMenuOpenBindCode());

        if (string.Equals(btnName, "Theme", StringComparison.OrdinalIgnoreCase))
            return "Theme  :  " + Mods.GetThemeLabel();

        if (string.Equals(btnName, "Diagnostics HUD", StringComparison.OrdinalIgnoreCase))
            return "Diagnostics HUD  :  " + Mods.GetVrDiagnosticsHudLabel();

        if (string.Equals(btnName, "FPS Counter", StringComparison.OrdinalIgnoreCase))
            return "FPS Counter  :  " + Mods.GetVrFpsCounterLabel();

        if (string.Equals(btnName, "Ping Display", StringComparison.OrdinalIgnoreCase))
            return "Ping Display  :  " + Mods.GetVrPingDisplayLabel();

        if (string.Equals(btnName, "Network Stats", StringComparison.OrdinalIgnoreCase))
            return "Network Stats  :  " + Mods.GetVrNetworkStatsLabel();

        if (string.Equals(btnName, "Average FPS", StringComparison.OrdinalIgnoreCase))
            return "Average FPS  :  " + Mods.GetVrFpsAverageLabel();

        if (string.Equals(btnName, "Slow FPS", StringComparison.OrdinalIgnoreCase))
            return "Slow FPS  :  " + Mods.GetVrFpsSlowLabel();

        if (string.Equals(btnName, "Frametime", StringComparison.OrdinalIgnoreCase))
            return "Frametime  :  " + Mods.GetVrFrametimeLabel();

        if (string.Equals(btnName, "HUD Move Bind", StringComparison.OrdinalIgnoreCase))
            return "HUD Move Bind  :  " + Mods.GetHudMoveBindLabel();

        if (string.Equals(btnName, "Aim Select Bind", StringComparison.OrdinalIgnoreCase))
            return "Aim Select Bind  :  " + Mods.GetAimSelectBindLabel();

        if (string.Equals(btnName, "Reset HUD Pos", StringComparison.OrdinalIgnoreCase))
            return "Reset HUD Pos";

        if (string.Equals(btnName, "Graphics Quality", StringComparison.OrdinalIgnoreCase))
            return "Graphics Quality  :  " + Mods.GetGraphicsQualityLabel();

        if (string.Equals(btnName, "Resolution Scale", StringComparison.OrdinalIgnoreCase))
            return "Resolution Scale  :  " + Mods.GetResolutionScaleLabel();

        if (string.Equals(btnName, "MSAA", StringComparison.OrdinalIgnoreCase))
            return "MSAA  :  " + Mods.GetMsaaLabel();

        if (string.Equals(btnName, "Shadow Quality", StringComparison.OrdinalIgnoreCase))
            return "Shadow Quality  :  " + Mods.GetShadowQualityLabel();

        if (string.Equals(btnName, "Render Distance", StringComparison.OrdinalIgnoreCase))
            return "Render Distance  :  " + Mods.GetRenderDistanceLabel();

        if (string.Equals(btnName, "Controller Haptics", StringComparison.OrdinalIgnoreCase))
            return "Controller Haptics  :  " + Mods.GetControllerHapticsLabel();

        if (string.Equals(btnName, "Comfort Mode", StringComparison.OrdinalIgnoreCase))
            return "Comfort Mode  :  " + Mods.GetComfortModeLabel();

        if (string.Equals(btnName, "Scan Mode", StringComparison.OrdinalIgnoreCase))
            return "Scan Mode  :  " + Mods.GetScanModeLabel();

        if (string.Equals(btnName, "Nametag Size", StringComparison.OrdinalIgnoreCase))
            return "Nametag Size  :  " + Mods.GetNameTagSizeLabel();

        if (string.Equals(btnName, "Fade Distance", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Nametag Fade Distance", StringComparison.OrdinalIgnoreCase))
            return "Nametag Fade Distance  :  " + Mods.GetNameTagFadeDistanceLabel();

        if (string.Equals(btnName, "Menu Settings", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Sound Settings", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "VR Settings", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Shader Settings", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Discord RPC", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Credits", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Exit Menu Settings", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Exit Sound Settings", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Exit VR Settings", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Exit Shader Settings", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "TUPshaders", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "PC Shader GUI", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Cycle Preset", StringComparison.OrdinalIgnoreCase))
            return btnName;

        if (string.Equals(btnName, "Equip Outfit", StringComparison.OrdinalIgnoreCase))
            return "Equip Outfit";

        if (btnName.StartsWith("Outfit #", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(btnName.Substring("Outfit #".Length), out int outfitNum))
            return Mods.GetOutfitDisplayName(outfitNum);

        return btnName;
    }

    private static bool IsTextOnlySetting(string btnName)
    {
        if (string.IsNullOrEmpty(btnName))
            return false;

        return string.Equals(btnName, "Queue", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Mode", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Region", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Lobby Map", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Click Sound", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Smooth Strength", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Menu Scale", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "PC Distance", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Open Bind", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "HUD Move Bind", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Aim Select Bind", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Reset HUD Pos", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Theme", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Scan Mode", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Nametag Size", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Nametag Fade Distance", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Fade Distance", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Graphics Quality", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Resolution Scale", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "MSAA", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Shadow Quality", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Render Distance", StringComparison.OrdinalIgnoreCase)
            || string.Equals(btnName, "Master Volume", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatLabelValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "-";
        value = value.Trim();
        return value.Length <= 1 ? value.ToUpperInvariant() : char.ToUpperInvariant(value[0]) + value.Substring(1).ToLowerInvariant();
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
        ShapeClickBox(target, visualRoot, col);
        HideColliderCoat(target);

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

    private static void AttachRootPress(Transform buttonRoot, ButtonTrigger trigger)
    {
        if (buttonRoot == null || trigger == null)
            return;

        buttonRoot.gameObject.layer = 2;
        Collider col = buttonRoot.GetComponent<Collider>();
        if (col == null)
            col = buttonRoot.gameObject.AddComponent<BoxCollider>();
        col.isTrigger = true;
        ShapeClickBox(buttonRoot, buttonRoot, col);

        Rigidbody rb = buttonRoot.GetComponent<Rigidbody>();
        if (rb == null)
            rb = buttonRoot.gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        ButtonCollider buttonCollider = buttonRoot.GetComponent<ButtonCollider>() ?? buttonRoot.gameObject.AddComponent<ButtonCollider>();
        buttonCollider.trigger = trigger;
    }

    private static void ShapeClickBox(Transform target, Transform visualRoot, Collider collider)
    {
        BoxCollider box = collider as BoxCollider;
        if (target == null || box == null)
            return;

        EnsurePositiveColliderLossyScale(target);

        MeshFilter meshFilter = target.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            Bounds bounds = meshFilter.sharedMesh.bounds;
            if (bounds.size.sqrMagnitude > 0.000001f)
            {
                box.center = bounds.center;
                box.size = AbsSize(bounds.size, 0.002f);
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
        box.size = AbsSize(new Vector3(size.x, size.y, 0.01f), 0.002f);
    }

    private static Vector3 AbsSize(Vector3 size, float min)
    {
        return new Vector3(
            Mathf.Max(min, Mathf.Abs(size.x)),
            Mathf.Max(min, Mathf.Abs(size.y)),
            Mathf.Max(min, Mathf.Abs(size.z)));
    }

    private static void EnsurePositiveColliderLossyScale(Transform target)
    {
        if (target == null)
            return;

        Vector3 lossy = target.lossyScale;
        Vector3 local = target.localScale;
        if (lossy.x < 0f) local.x = -local.x;
        if (lossy.y < 0f) local.y = -local.y;
        if (lossy.z < 0f) local.z = -local.z;
        if (Mathf.Abs(local.x) < 0.0001f) local.x = 0.0001f;
        if (Mathf.Abs(local.y) < 0.0001f) local.y = 0.0001f;
        if (Mathf.Abs(local.z) < 0.0001f) local.z = 0.0001f;
        target.localScale = local;
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

    private static bool ColliderHasCoat(Transform transform)
    {
        if (transform == null)
            return false;
        if (transform.name.IndexOf("Collider", StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        return transform.name.IndexOf("HoverCollider", StringComparison.OrdinalIgnoreCase) < 0;
    }
    private void PutCollidersBackInvis()
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

            HideColliderCoat(child);
        }
    }

    private static void HideColliderCoat(Transform colliderTransform)
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

    private void SpawnLeftKeyboardDot()
    {
        if (leftKeyboardSelectionObj != null)
            return;

        leftKeyboardSelectionObj = CreateSelectorPresser(GTPlayer.Instance.LeftHand.controllerTransform, true);
    }

    private void KillLeftKeyboardDot()
    {
        if (leftKeyboardSelectionObj == null)
            return;

        Destroy(leftKeyboardSelectionObj);
        leftKeyboardSelectionObj = null;
    }
    
    private void SaveToggleBits()
    {
        foreach (GameObject btnObj in buttons)
        {
            if (btnObj == null) continue;
            ButtonTrigger trigger = btnObj.GetComponent<ButtonTrigger>() ?? btnObj.GetComponentInChildren<ButtonTrigger>(true);

            if (trigger != null && trigger.IsToggle && !trigger.SkipSwitchAnimation)
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
        Mods.ClearPadNetworkState();
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

        KillLeftKeyboardDot();

        DestroyButtons();

        foreach (GameObject selectorObj in tabButtonObjs)
            if (selectorObj != null)
                selectorObj.SetActive(false);

        foreach (GameObject navBtn in pageButtonObjs)
            if (navBtn != null)
                navBtn.SetActive(false);

        Mods.ClearSelectionOnMenuClose();
        Mods.ClearPadNetworkState();
        isMenuClosing = false;
    }

    public void RefreshSelectUserButtonStates()
    {
        if (currentCategory != "SelectUser" && currentCategory != "Lobby")
            return;

        foreach (GameObject btnObj in buttons)
        {
            if (btnObj == null)
                continue;

            ButtonTrigger trigger = btnObj.GetComponent<ButtonTrigger>() ?? btnObj.GetComponentInChildren<ButtonTrigger>(true);

            if (trigger == null || !trigger.IsToggle || !trigger.SkipSwitchAnimation || trigger.CustomAction == null)
                continue;

            bool selected = Mods.IsSelectedPlayerButton(trigger.BtnIdentifier);
            trigger.IsOn = selected;
            if (selected)
                MenuEffects.SnapActivated(trigger.Knob, trigger.Slider, trigger.BodyRenderer, trigger.OutlineRenderer);
            else
                MenuEffects.SnapDeactivated(trigger.Knob, trigger.Slider, trigger.BodyRenderer, trigger.OutlineRenderer);
        }
    }
    
    private IEnumerator CreateButtons(string categoryName = "Networking")
    {
        const float gap = 0.094f;


        if (!Mods.Actions.TryGetValue(currentCategory, out var category))
            yield break;

        SetSpotifyPageOnOff();
        if (IsSpotifyHudPageSelected())
        {
            UpdatePageIndicators();
            yield break;
        }

        var pageItems = BuildPageItems(category);
        int pageSize = GetEffectivePageSize(currentCategory);
        int start = currentPage * pageSize;
        int end   = Math.Min(start + pageSize, pageItems.Count);

        int index = 0;
        for (int i = start; i < end; i++)
        {
            PageButtonItem item = pageItems[i];
            float offset = -(index * gap);
            CreateButton(offset, item.Label, item.IsToggle, item.Cooldown, item.CustomAction);
            index++;
            yield return new WaitForSeconds(0.08f);
        }

        InitCycleBtns();
        if (currentCategory == "Cosmetics")
        {
            ApplyOutfitCarouselSideUi();
            RefreshOutfitPreviewDelayed();
        }
        UpdatePageIndicators();
        UpdatePageNavVisibility();
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
        if (string.Equals(currentCategory, "Friends", StringComparison.OrdinalIgnoreCase))
            return BuildFriendsPageItems();

        List<PageButtonItem> items = category.Actions
            .Select(action => new PageButtonItem(action.Key, action.Value.IsToggle, action.Value.Cooldown, null))
            .ToList();

        if (currentCategory != "Lobby" && currentCategory != "SelectUser")
            return items;

        foreach (VRRig rig in Mods.GetSelectableRigs())
        {
            if (rig == null)
                continue;

            VRRig capturedRig = rig;
            items.Add(new PageButtonItem(
                Mods.GetRigDisplayName(rig),
                true,
                0f,
                () =>
                {
                    Mods.SelectRigFromMenu(capturedRig);
                    RefreshSelectUserButtonStates();
                }));
        }

        return items;
    }

    private List<PageButtonItem> BuildFriendsPageItems()
    {
        var items = new List<PageButtonItem>();

        if (Mods.HasSelectedFriend())
        {
            string name = Mods.GetSelectedFriendDisplayName();
            items.Add(new PageButtonItem("Back to Friends", false, 0f, Mods.BackToFriendsList));
            items.Add(new PageButtonItem(name, false, 0f, () => { }));
            items.Add(new PageButtonItem("Join Their Room", false, 1f, Mods.JoinSelectedFriendRoom));
            items.Add(new PageButtonItem("Invite Here", false, 1f, Mods.InviteSelectedFriendHere));
            items.Add(new PageButtonItem("Make Private Code", false, 1f, Mods.MakePrivateCodeForFriend));
            items.Add(new PageButtonItem("Refresh Friends", false, 0f, Mods.RefreshFriendsList));
            return items;
        }

        items.Add(new PageButtonItem("Refresh Friends", false, 0f, Mods.RefreshFriendsList));

        IReadOnlyList<Mods.PadFriendEntry> friends = Mods.GetCachedFriends();
        for (int i = 0; i < friends.Count; i++)
        {
            Mods.PadFriendEntry entry = friends[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.PlayFabId))
                continue;

            string id = entry.PlayFabId;
            items.Add(new PageButtonItem(
                Mods.FormatFriendButtonLabel(entry),
                false,
                0f,
                () => Mods.OpenFriendActions(id)));
        }

        if (friends.Count == 0)
            items.Add(new PageButtonItem("No friends loaded", false, 0f, Mods.RefreshFriendsList));

        return items;
    }

    public void RefreshFriendsPageIfOpen()
    {
        if (!isMenuOpened)
            return;
        if (!string.Equals(currentCategory, "Friends", StringComparison.OrdinalIgnoreCase))
            return;
        RefreshCurrentPage();
    }
    private void SwitchCategory(string categoryName)
    {
        Tools.StopCoroutine(ref buttonRoutine);
        string previousCategory = currentCategory;
        currentCategory = categoryName;
        currentPage = 0;

        if (categoryName == "Cosmetics")
        {
            Mods.InitOutfitSlotCaches();
            EnterOutfitPreviewMode();
        }
        else if (previousCategory == "Cosmetics")
        {
            ExitOutfitPreviewMode();
            SetCheckerPanelTitle(string.IsNullOrEmpty(checkerPanelTitleDefault)
                ? "Player Checker"
                : checkerPanelTitleDefault);
            SetVolumeControlsVisible(true);
            if (categoryName != "SelectUser" && categoryName != "Lobby")
            {
                Mods.ClearSelectedPlayerSilent();
                SetMoreInfoVisible(false);
            }
            else if (!Mods.HasSelectedPlayer())
            {
                ClearCheckerSelectionUiKeepPreview(true);
                SetMoreInfoVisible(false);
            }
        }

        bool wasPlayerTab = previousCategory == "SelectUser" || previousCategory == "Lobby";
        bool isPlayerTab = categoryName == "SelectUser" || categoryName == "Lobby";
        if (wasPlayerTab && !isPlayerTab && categoryName != "Cosmetics")
        {
            Mods.ClearSelectedPlayerSilent();
        }

        if (previousCategory == "Search" && categoryName != "Search")
            Mods.CloseRoomCodeSearchUiPublic();
        if (previousCategory == "Friends" && categoryName != "Friends")
            Mods.ClearSelectedFriend();
        if (categoryName == "Friends")
            Mods.EnsureFriendsWarm();

        DestroyButtons();
        SetSpotifyPageOnOff();
        buttonRoutine = StartCoroutine(CreateButtons());
        UpdatePageIndicators();
        UpdatePageNavVisibility();

        Mods.BroadcastPadNetworkState(force: true);
    }

    public void OpenSettingsCategory(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
            return;
        if (!Mods.Actions.ContainsKey(categoryName))
            return;

        SwitchCategory(categoryName);
    }

    public void OpenNetworkingActionPage(string actionName)
    {
        if (string.IsNullOrWhiteSpace(actionName))
            return;
        if (!Mods.Actions.TryGetValue("Networking", out ModCategory category))
            return;

        int index = 0;
        foreach (var kv in category.Actions)
        {
            if (string.Equals(kv.Key, actionName, StringComparison.OrdinalIgnoreCase))
                break;
            index++;
        }

        int pageSize = GetEffectivePageSize("Networking");
        int targetPage = Mathf.Max(0, index / Mathf.Max(1, pageSize));


        if (!string.Equals(currentCategory, "Networking", StringComparison.OrdinalIgnoreCase)
            || currentPage != targetPage
            || !isMenuOpened)
        {
            currentCategory = "Networking";
            currentPage = targetPage;
            if (isMenuOpened)
            {
                Tools.StopCoroutine(ref buttonRoutine);
                DestroyButtons();
                buttonRoutine = StartCoroutine(CreateButtons());
                UpdatePageIndicators();
}
        }
    }

    private void InitPageButtons(GameObject menuObj)
    {
        tabButtonObjs.Clear();
        List<string> categories = new List<string>
        {
            "Networking",
            "Room",
            "Cosmetics",
            "SelectUser",
            "Lobby",
            "Settings",
            "Friends",
            "Camera",
            "Anticheat",
            "Spotify"
        }.Where(category => Mods.Actions.ContainsKey(category) && Mods.Actions[category].ShowInMenu).ToList();

        var foundButtons = new List<(int Index, Transform Button)>();
        HashSet<int> wiredIndexes = new HashSet<int>();
        foreach (Transform child in menuObj.GetComponentsInChildren<Transform>(true))
        {
            if (child == null || !child.name.StartsWith("SelectorBtn", StringComparison.OrdinalIgnoreCase))
                continue;

            string numberPart = child.name.Substring("SelectorBtn".Length);
            if (!int.TryParse(numberPart, out int btnIndex) || btnIndex < 1)
                continue;
            if (!wiredIndexes.Add(btnIndex))
                continue;

            if (!tabButtonBaseLocalPositions.ContainsKey(btnIndex))
                tabButtonBaseLocalPositions[btnIndex] = child.localPosition;

            foundButtons.Add((btnIndex, child));
        }

        foundButtons.Sort((a, b) => a.Index.CompareTo(b.Index));

        Transform gearSource = foundButtons.FirstOrDefault(b => b.Index == 5).Button;
        if (gearSource != null && settingsTabIconSprite == null)
            settingsTabIconSprite = CaptureTabIconSprite(gearSource);

        for (int slot = 0; slot < foundButtons.Count; slot++)
        {
            Transform child = foundButtons[slot].Button;
            int btnIndex = foundButtons[slot].Index;

            Transform selectorCollider = GetInteractiveCollider(child, "Collider");
            ButtonTrigger trigger = WireInteractive(child, child.name, null, selectorCollider);
            MenuTheme.ApplySelectorButton(child);
            WakeUpTabButton(child);

            if (slot < 0 || slot >= categories.Count)
            {
                child.gameObject.SetActive(false);
                continue;
            }

            string categoryName = categories[slot];
            string capturedCategory = categoryName;
            trigger.SkipSwitchAnimation = true;
            trigger.CustomAction = () => SwitchCategory(capturedCategory);
            ApplyCategoryTabIcon(child, categoryName);

            child.gameObject.SetActive(true);
            tabButtonObjs.Add(child.gameObject);
        }

        LayoutTabButtons(foundButtons, categories.Count);
    }

    private void LayoutTabButtons(List<(int Index, Transform Button)> foundButtons, int activeCount)
    {
        if (foundButtons == null || foundButtons.Count == 0 || activeCount <= 0)
            return;

        var active = foundButtons.Where(b => b.Button != null && b.Button.gameObject.activeSelf).Take(activeCount).ToList();
        if (active.Count == 0)
            return;

        Vector3 first = tabButtonBaseLocalPositions.TryGetValue(1, out Vector3 p1)
            ? p1
            : active[0].Button.localPosition;
        Vector3 last = tabButtonBaseLocalPositions.TryGetValue(Mathf.Min(6, active.Count), out Vector3 pLast)
            ? pLast
            : (tabButtonBaseLocalPositions.TryGetValue(5, out Vector3 p5) ? p5 : active[active.Count - 1].Button.localPosition);

        if (active.Count >= 7 && tabButtonBaseLocalPositions.TryGetValue(7, out Vector3 p7))
            last = p7;
        else if (active.Count >= 7 && tabButtonBaseLocalPositions.TryGetValue(6, out Vector3 p6))
        {
            Vector3 prev = tabButtonBaseLocalPositions.TryGetValue(5, out Vector3 p5b) ? p5b : p6;
            last = p6 + (p6 - prev);
        }

        for (int i = 0; i < active.Count; i++)
        {
            float t = active.Count == 1 ? 0f : i / (float)(active.Count - 1);
            active[i].Button.localPosition = Vector3.Lerp(first, last, t);
        }
    }

    private static Sprite CaptureTabIconSprite(Transform selectorButton)
    {
        if (selectorButton == null)
            return null;

        foreach (Image image in selectorButton.GetComponentsInChildren<Image>(true))
        {
            if (image == null || image.sprite == null || image.gameObject == selectorButton.gameObject)
                continue;
            string name = image.gameObject.name;
            if (name.IndexOf("outline", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (name.IndexOf("bg", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (name.IndexOf("background", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            return image.sprite;
        }

        return null;
    }

    private void ApplyCategoryTabIcon(Transform selectorButton, string categoryName)
    {
        if (selectorButton == null || string.IsNullOrWhiteSpace(categoryName))
            return;

        bool needsOverride =
            string.Equals(categoryName, "Lobby", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(categoryName, "Settings", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(categoryName, "Friends", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(categoryName, "Camera", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(categoryName, "Anticheat", StringComparison.OrdinalIgnoreCase);
        if (!needsOverride)
            return;

        if (string.Equals(categoryName, "Settings", StringComparison.OrdinalIgnoreCase)
            && settingsTabIconSprite != null)
        {
            ApplySpriteToTab(selectorButton, settingsTabIconSprite);
            return;
        }

        if (!Mods.Actions.TryGetValue(categoryName, out ModCategory category) || string.IsNullOrWhiteSpace(category.ImageName))
            return;

        Texture2D texture = Tools.LoadEmbeddedImage(category.ImageName);
        if (texture == null)
            return;

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f);

        ApplySpriteToTab(selectorButton, sprite, texture);
    }

    private static void ApplySpriteToTab(Transform selectorButton, Sprite sprite, Texture2D texture = null)
    {
        if (selectorButton == null || sprite == null)
            return;

        bool applied = false;
        foreach (Image image in selectorButton.GetComponentsInChildren<Image>(true))
        {
            if (image == null)
                continue;
            string name = image.gameObject.name;
            if (name.IndexOf("outline", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (name.IndexOf("bg", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (name.IndexOf("background", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (image.gameObject == selectorButton.gameObject && image.type == Image.Type.Sliced)
                continue;

            image.sprite = sprite;
            image.enabled = true;
            image.preserveAspect = true;
            image.color = Color.white;
            applied = true;
        }

        foreach (RawImage image in selectorButton.GetComponentsInChildren<RawImage>(true))
        {
            if (image == null)
                continue;
            string name = image.gameObject.name;
            if (name.IndexOf("outline", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (name.IndexOf("bg", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            if (texture != null)
                image.texture = texture;
            else if (sprite != null && sprite.texture != null)
                image.texture = sprite.texture;
            image.enabled = true;
            image.color = Color.white;
            applied = true;
        }

        foreach (SpriteRenderer renderer in selectorButton.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (renderer == null)
                continue;
            string name = renderer.gameObject.name;
            if (name.IndexOf("outline", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (name.IndexOf("bg", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            renderer.sprite = sprite;
            renderer.enabled = true;
            renderer.color = Color.white;
            applied = true;
        }

        if (!applied)
        {
            Image fallback = selectorButton.GetComponentInChildren<Image>(true);
            if (fallback != null)
            {
                fallback.sprite = sprite;
                fallback.enabled = true;
                fallback.preserveAspect = true;
                fallback.color = Color.white;
            }
        }
    }



    private static void WakeUpTabButton(Transform selectorButton)
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

    private void InitNavButtons(GameObject menuObj)
    {
        pageButtonObjs.Clear();
        string[] navNames = { "PageBack", "PageNext" };
        foreach (string navName in navNames)
        {
            Transform navTransform = FindMainMenuNavButton(menuObj.transform, navName);
            if (navTransform == null)
            {
                Debug.LogWarning($"[TUP] Nav button '{navName}' not found in menu prefab");
                continue;
            }

            int delta = navName == "PageNext" ? 1 : -1;
            Transform navCollider = GetInteractiveCollider(navTransform, "Collider");
            if (navCollider != null)
            {
                Vector3 ls = navCollider.localScale;
                navCollider.localScale = new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
                EnsurePositiveColliderLossyScale(navCollider);
            }

            ButtonTrigger trigger = WireInteractive(navTransform, navName, () => ChangePage(delta), navCollider);
            MenuTheme.ApplyNavButton(navTransform);
            navTransform.gameObject.SetActive(true);

            pageButtonObjs.Add(navTransform.gameObject);
        }
    }

    private static Transform FindMainMenuNavButton(Transform menuRoot, string navName)
    {
        if (menuRoot == null)
            return null;

        Transform best = null;
        foreach (Transform child in menuRoot.GetComponentsInChildren<Transform>(true))
        {
            if (!child.name.Equals(navName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (child.name.StartsWith("TUP_", StringComparison.OrdinalIgnoreCase))
                continue;

            string path = GetTransformPath(child);
            if (path.IndexOf("SideHolder", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (path.IndexOf("ModsTitle", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (path.IndexOf("CheatsTitle", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (path.IndexOf("TUP_ModPage", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            best = child;
            if (path.IndexOf("MainMenu", StringComparison.OrdinalIgnoreCase) >= 0)
                return child;
        }

        return best;
    }

    public void OnOutfitCarouselChanged()
    {
        if (!string.Equals(currentCategory, "Cosmetics", StringComparison.OrdinalIgnoreCase))
            return;

        ApplyOutfitCarouselSideUi();
        UpdatePageIndicators();
    }

    private void ChangePage(int delta)
    {
        if (string.Equals(currentCategory, "Cosmetics", StringComparison.OrdinalIgnoreCase))
        {
            Mods.BrowseOutfit(delta);
            return;
        }

        if (!Mods.Actions.TryGetValue(currentCategory, out var category))
            return;

        int totalPages = GetCategoryTotalPages(currentCategory, category);
        currentPage = (currentPage + delta + totalPages) % totalPages;

        Tools.StopCoroutine(ref buttonRoutine);
        DestroyButtons();
        SetSpotifyPageOnOff();
        buttonRoutine = StartCoroutine(CreateButtons());
        UpdatePageIndicators();
        Mods.BroadcastPadNetworkState(force: true);
    }

    private int GetEffectivePageSize(string categoryName)
    {
        return PageSize;
    }

    private int GetCategoryTotalPages(string categoryName, ModCategory category)
    {
        if (string.Equals(categoryName, "Cosmetics", StringComparison.OrdinalIgnoreCase))
            return Mathf.Clamp(Mods.GetOutfitCount(), 1, 10);

        int pageSize = GetEffectivePageSize(categoryName);
        int itemCount;
        if (string.Equals(categoryName, "Friends", StringComparison.OrdinalIgnoreCase))
            itemCount = BuildFriendsPageItems().Count;
        else
        {
            itemCount = category.Actions.Count
                + ((categoryName == "Lobby" || categoryName == "SelectUser") ? Mods.GetSelectableRigs().Count : 0);
        }

        int normalPages = Mathf.Max(1, Mathf.CeilToInt((float)Mathf.Max(1, itemCount) / pageSize));
        if (categoryName == "Spotify")
            return Mathf.Max(1, normalPages + 1);

        return normalPages;
    }

    private void CachePageVisDots(GameObject menu)
    {
        pageVisDots.Clear();
        if (menu == null)
            return;

        var found = new List<(int Index, Transform Dot)>();
        foreach (Transform child in menu.GetComponentsInChildren<Transform>(true))
        {
            if (child == null || !child.name.StartsWith("PageVis", StringComparison.OrdinalIgnoreCase))
                continue;

            string numberPart = child.name.Substring("PageVis".Length);
            if (!int.TryParse(numberPart, out int index) || index < 1)
                continue;

            string path = GetTransformPath(child);
            if (path.IndexOf("SideHolder", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            found.Add((index, child));
        }

        foreach ((int _, Transform dot) in found.OrderBy(f => f.Index).GroupBy(f => f.Index).Select(g => g.First()))
            pageVisDots.Add(dot);
    }

    private void EnsurePageVisDotCount(int needed)
    {
        needed = Mathf.Clamp(needed, 1, 10);
        if (pageVisDots.Count == 0 || needed <= pageVisDots.Count)
            return;

        Transform proto = pageVisDots[0];
        Transform parent = proto.parent;
        if (parent == null)
            return;

        Vector3 step = pageVisDots.Count >= 2
            ? pageVisDots[1].localPosition - pageVisDots[0].localPosition
            : new Vector3(0.012f, 0f, 0f);

        while (pageVisDots.Count < needed)
        {
            Transform clone = Instantiate(proto.gameObject, parent).transform;
            clone.name = "PageVis" + (pageVisDots.Count + 1);
            clone.localRotation = proto.localRotation;
            clone.localScale = proto.localScale;
            clone.localPosition = pageVisDots[0].localPosition + step * pageVisDots.Count;
            clone.gameObject.SetActive(true);
            pageVisDots.Add(clone);
        }
    }

    private void UpdatePageIndicators()
    {
        if (menuObj == null)
            return;

        if (pageVisDots.Count == 0)
            CachePageVisDots(menuObj);

        if (!Mods.Actions.TryGetValue(currentCategory, out ModCategory category))
            return;

        int totalPages = GetCategoryTotalPages(currentCategory, category);
        int page = currentPage;

        if (string.Equals(currentCategory, "Cosmetics", StringComparison.OrdinalIgnoreCase))
            page = Mathf.Clamp(Mods.GetCurrentOutfitIndex(), 0, Mathf.Max(0, totalPages - 1));
        else
            page = Mathf.Clamp(page, 0, Mathf.Max(0, totalPages - 1));

        if (!string.Equals(currentCategory, "Cosmetics", StringComparison.OrdinalIgnoreCase))
            currentPage = page;

        EnsurePageVisDotCount(totalPages);

        Color32 active = MenuTheme.Current.Accent;
        Color32 inactive = new Color32(
            (byte)Mathf.Clamp(MenuTheme.Current.DarkAccent.r / 2, 20, 80),
            (byte)Mathf.Clamp(MenuTheme.Current.DarkAccent.g / 2, 20, 80),
            (byte)Mathf.Clamp(MenuTheme.Current.DarkAccent.b / 2, 20, 80),
            255);

        bool hideAllDots = totalPages <= 1;
        for (int i = 0; i < pageVisDots.Count; i++)
        {
            Transform dot = pageVisDots[i];
            if (dot == null)
                continue;

            bool visible = !hideAllDots && i < totalPages;
            if (dot.gameObject.activeSelf != visible)
                dot.gameObject.SetActive(visible);

            if (!visible)
                continue;

            Color32 tint = i == page ? active : inactive;
            MenuTheme.Tint(dot, tint);
            foreach (Renderer renderer in dot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer != null)
                    MenuTheme.Tint(renderer.transform, tint);
            }

            foreach (UnityEngine.UI.Image img in dot.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (img != null)
                    img.color = tint;
            }
        }

        UpdatePageNavVisibility(totalPages);
    }

    private void UpdatePageNavVisibility(int totalPages = -1)
    {
        if (menuObj == null)
            return;

        if (totalPages < 0)
        {
            if (!Mods.Actions.TryGetValue(currentCategory, out ModCategory cat))
                return;
            totalPages = GetCategoryTotalPages(currentCategory, cat);
        }

        bool multi = totalPages > 1;
        string[] navNames = { "PageBack", "PageNext" };
        for (int i = 0; i < navNames.Length; i++)
        {
            Transform nav = FindChildByName(menuObj.transform, navNames[i]);
            if (nav != null && nav.gameObject.activeSelf != multi)
                nav.gameObject.SetActive(multi);
        }
    }

    public void RefreshCurrentPage()
    {
        if (!isMenuOpened) return;
        Tools.StopCoroutine(ref buttonRoutine);
        DestroyButtons();
        buttonRoutine = StartCoroutine(CreateButtons());
        UpdatePageIndicators();
        Mods.BroadcastPadNetworkState(force: true);
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
        InitCycleBtns();
    }

    private void CreateButton(float yOffset, string btnName, bool isToggle = false, float cooldown = 0f, Action customAction = null)
    {
        GameObject btn;
        bool usingButtonSeed = buttonSeed != null && buttonShelf != null;

        if (usingButtonSeed)
        {
            btn = Instantiate(buttonSeed, buttonShelf);
            btn.name = btnName;
            btn.SetActive(true);
            btn.transform.localPosition = buttonSeed.transform.localPosition + new Vector3(0f, yOffset, 0f);
            btn.transform.localRotation = buttonSeed.transform.localRotation;
            btn.transform.localScale = buttonSeed.transform.localScale;
        }
        else
        {
            Transform parent = buttonShelf != null ? buttonShelf : menuObj.transform;
            btn = Instantiate(btnPrefab, parent);
            btn.name = btnName;
            btn.transform.localPosition = buttonBasePosition + new Vector3(0.01f, yOffset, 0f);
            btn.transform.localRotation = buttonBaseRotation;
            btn.transform.localScale = buttonBaseScale;
        }

        Transform legacyClickCollider = FindButtonChild(btn.transform, "ClickCollider");
        if (legacyClickCollider != null && FindButtonChild(btn.transform, "Collider") == null)
            legacyClickCollider.name = "Collider";

        Transform slider = FindButtonChild(btn.transform, "Slider");
        Transform knob = FindButtonChild(btn.transform, "Slider/Knob");
        Transform clickCollider = GetInteractiveCollider(btn.transform, "Collider");
        ButtonTrigger trigger;

        if (usingButtonSeed && clickCollider != null)
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
            buttons.Add(btnCollider);
        }

        if (clickCollider == null)
            AttachRootPress(btn.transform, trigger);

        trigger.SkipSwitchAnimation = false;
        trigger.IsKeyboardKey = false;
        trigger.CustomAction = customAction;

        trigger.Slider = slider;
        trigger.Knob = knob;
        trigger.IsToggle = isToggle;
        trigger.Cooldown = cooldown;

        MenuTheme.ApplyMenuButton(btn.transform, out Renderer renderer, out Renderer outlineRenderer);
        trigger.BodyRenderer = renderer;
        trigger.OutlineRenderer = outlineRenderer;
        TMP_Text text = GetButtonTitle(btn.transform);
        if (text != null)
        {
            text.text = GetButtonDisplayText(btnName);
            if (string.Equals(btnName, "Queue", StringComparison.OrdinalIgnoreCase))
                queueText = text;
            else if (string.Equals(btnName, "Mode", StringComparison.OrdinalIgnoreCase))
                modeText = text;
            else if (string.Equals(btnName, "Region", StringComparison.OrdinalIgnoreCase))
                regionText = text;
            else if (string.Equals(btnName, "Lobby Map", StringComparison.OrdinalIgnoreCase))
                lobbyMapText = text;
            else if (string.Equals(btnName, "Search Room", StringComparison.OrdinalIgnoreCase))
                roomCodeSearchText = text;
            else if (string.Equals(btnName, "Click Sound", StringComparison.OrdinalIgnoreCase))
                clickSoundText = text;
            else if (string.Equals(btnName, "Startup Sound", StringComparison.OrdinalIgnoreCase))
                startupSoundText = text;
            else if (string.Equals(btnName, "Menu Open Sound", StringComparison.OrdinalIgnoreCase))
                menuOpenSoundText = text;
            else if (string.Equals(btnName, "Button Pop Sound", StringComparison.OrdinalIgnoreCase))
                buttonPopSoundText = text;
            else if (string.Equals(btnName, "Notif Sound", StringComparison.OrdinalIgnoreCase))
                notifSoundText = text;
            else if (string.Equals(btnName, "Menu Smoothing", StringComparison.OrdinalIgnoreCase))
                smoothingText = text;
            else if (string.Equals(btnName, "Smooth Strength", StringComparison.OrdinalIgnoreCase))
                smoothingStrengthText = text;
            else if (string.Equals(btnName, "Menu Scale", StringComparison.OrdinalIgnoreCase))
                menuScaleText = text;
            else if (string.Equals(btnName, "PC Distance", StringComparison.OrdinalIgnoreCase))
                headDistanceText = text;
            else if (string.Equals(btnName, "Double Open", StringComparison.OrdinalIgnoreCase))
                doubleOpenText = text;
            else if (string.Equals(btnName, "Open Bind", StringComparison.OrdinalIgnoreCase))
                openBindText = text;
            else if (string.Equals(btnName, "Theme", StringComparison.OrdinalIgnoreCase))
                themeText = text;
            else if (string.Equals(btnName, "Scan Mode", StringComparison.OrdinalIgnoreCase))
                scanModeText = text;
        }
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
            text.alignment = TextAlignmentOptions.Left;
            text.margin = new Vector4(0f, 0f, 0.02f, 0f);
        }

        Transform hoverCollider = GetInteractiveCollider(btn.transform, "HoverCollider");
        if (hoverCollider != null)
        {
            hoverCollider.gameObject.layer = 2;
            Collider hc = hoverCollider.GetComponent<Collider>();
            if (hc == null) hc = hoverCollider.gameObject.AddComponent<BoxCollider>();
            hc.isTrigger = true;
            ShapeClickBox(hoverCollider, btn.transform, hc);

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
            else if (btnName == "Nametag Fade Distance" || btnName == "Fade Distance")
            {
                nameTagFadeDistanceText = text;
                text.text = "Nametag Fade Distance  :  " + Mods.GetNameTagFadeDistanceLabel();
            }
        }

        if (isToggle)
        {
            bool isStickyPlayerSelect = currentCategory == "Lobby" && customAction != null;
            bool savedOn = isStickyPlayerSelect
                ? Mods.IsSelectedPlayerButton(btnName)
                : Mods.GetSavedToggle(btnName, false);
            trigger.IsOn = savedOn;

            if (isStickyPlayerSelect)
            {
                trigger.SkipSwitchAnimation = true;
                Action selectAction = customAction;
                trigger.CustomAction = () =>
                {
                    selectAction?.Invoke();
                    RefreshSelectUserButtonStates();
                };
            }

            if (savedOn)
                MenuEffects.SnapActivated(knob, slider, trigger.BodyRenderer, trigger.OutlineRenderer);
            else
                MenuEffects.SnapDeactivated(knob, slider, trigger.BodyRenderer, trigger.OutlineRenderer);
        }

        if (IsTextOnlySetting(btnName))
            ApplyCycleSideControls(btn.transform);
        else if (!isToggle)
            HideButtonChrome(btn.transform);
        else
            HideCycleArrowOnly(btn.transform);

        if (btnName.StartsWith("Saved Outfit #") && int.TryParse(btnName.Substring("Saved Outfit #".Length), out int outfitN))
        {
            if (text != null)
                text.text = Mods.GetOutfitDisplayName(outfitN);

            Transform renameSprite = FindButtonChild(btn.transform, "Rename");
            if (renameSprite != null)
                renameSprite.gameObject.SetActive(false);

            Transform renameCollider = FindButtonChild(btn.transform, "Rename/RenameCollider");
            if (renameCollider != null)
                renameCollider.gameObject.SetActive(false);
        }

        if (btnName.StartsWith("Outfit #") && int.TryParse(btnName.Substring("Outfit #".Length), out int outfitNum))
        {
            if (text != null)
                text.text = Mods.GetOutfitDisplayName(outfitNum);
        }

        Vector3 targetScale = usingButtonSeed ? btn.transform.localScale : buttonBaseScale;
        btn.transform.localScale = Vector3.zero;
        StartCoroutine(MenuEffects.PopButton(btn, targetScale, Vector3.zero, true));

        if (Mods.IsButtonPopSoundEnabled())
            PlayUiClip(btnEnterSound, 0.1f);

        buttons.Add(btn);
    }
    public void PutMenuInTypingSpot()
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
        smooth.LocalPosition = new Vector3(0f, -0.05f, 0.8f);
        smooth.LocalRotation = Quaternion.Euler(0f, 270f, 0f);
        smooth.Frozen = false;
        smooth.ResetDeadzoneAnchor();
        SpawnLeftKeyboardDot();
    }

    public void RestoreMenuFollowAfterKeyboard()
    {
        if (menuObj == null)
            return;

        KillLeftKeyboardDot();

        Camera activeCamera = GetActiveCamera();
        Transform parent = currentOpenType == MenuOpenType.Head
            ? activeCamera != null ? activeCamera.transform : null
            : GetHandMenuTarget();

        if (parent == null)
            return;

        SmoothFollowMenu smooth = menuObj.GetComponent<SmoothFollowMenu>() ?? menuObj.AddComponent<SmoothFollowMenu>();
        smooth.YawOnly = false;
        smooth.UseYawDeadzone = false;
        smooth.Target = parent;
        smooth.Smoothing = menuSmoothingEnabled ? menuSmoothingStrength : 1000f;
        smooth.Frozen = false;

        if (currentOpenType == MenuOpenType.Head)
        {
            smooth.LocalPosition = new Vector3(HeadMenuX, HeadMenuY, headMenuDistance);
            smooth.LocalRotation = GetHeadMenuLocalRotation();
        }
        else
        {
            smooth.LocalPosition = GetHandMenuLocalPosition();
            smooth.LocalRotation = GetHandMenuLocalRotation();
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

        if (menuObj == null)
            return;

        if (menuObj.activeSelf)
        {
            if (menuScaleRoutine != null)
                StopCoroutine(menuScaleRoutine);

            menuScaleRoutine = StartCoroutine(SmoothMenuScale(menuScale));
            return;
        }

        menuObj.transform.localScale = Vector3.one * menuScale;
    }

    private Vector3 GetHandMenuLocalPosition()
    {
        return menuHandOffset + new Vector3(menuOffsetOut, menuOffsetUp, menuOffsetRight);
    }

    private Quaternion GetHandMenuLocalRotation()
    {

        return Quaternion.Euler(345f - menuGripTilt, 195f, 0f);
    }

    public static Vector3 GetNetworkedPadLocalPosition()
    {
        if (Instance == null)
            return new Vector3(0.024f, -0.13f, 0.03f);
        return Instance.GetHandMenuLocalPosition();
    }

    public static Quaternion GetNetworkedPadLocalRotation()
    {
        if (Instance == null)
            return Quaternion.Euler(300f, 195f, 0f);
        return Instance.GetHandMenuLocalRotation();
    }

    public static float GetNetworkedPadScale()
    {
        return Instance != null ? Instance.menuScale : 0.375f;
    }

    public string GetPadNetworkGuiState()
    {
        if (!isMenuOpened || isMenuClosing || currentOpenType != MenuOpenType.Hand)
            return "0";
        return currentCategory + ":" + currentPage;
    }

    public static List<string> GetNetworkPadPageLabels(string category, int page)
    {
        List<string> labels = new List<string>();
        if (string.IsNullOrWhiteSpace(category) || !Mods.Actions.TryGetValue(category, out ModCategory modCategory))
            return labels;

        const int pageSize = 7;
        List<string> keys = new List<string>(modCategory.Actions.Keys);
        int start = Mathf.Max(0, page) * pageSize;
        int end = Mathf.Min(start + pageSize, keys.Count);
        for (int i = start; i < end; i++)
            labels.Add(keys[i]);
        return labels;
    }

    public bool IsHandPadNetworkVisible(out bool leftHand)
    {
        leftHand = true;
        return isMenuOpened && !isMenuClosing && currentOpenType == MenuOpenType.Hand;
    }

    public GameObject CreateNetworkPadVisual()
    {
        if (!menuMadeAlready)
            InitMenu();

        AssetBundle bundle = menuReduxBundle != null ? menuReduxBundle : menuBundle;
        if (bundle == null)
            return null;

        string prefabPath = bundle == menuReduxBundle
            ? "assets/prefabs/TUP-overv18.prefab"
            : "assets/prefabs/tup-modelsmooth.prefab";

        GameObject prefab = bundle.LoadAsset<GameObject>(prefabPath);
        if (prefab == null)
            return null;

        GameObject visual = Instantiate(prefab);
        visual.transform.localScale = Vector3.one * menuScale;
        StripNetworkPadInteractives(visual);
        MenuTheme.Assign(visual, Theme);
        return visual;
    }

    private static void StripNetworkPadInteractives(GameObject visual)
    {
        if (visual == null)
            return;

        Rigidbody[] bodies = visual.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i] != null)
                Destroy(bodies[i]);
        }

        Collider[] colliders = visual.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                Destroy(colliders[i]);
        }

        ButtonTrigger[] triggers = visual.GetComponentsInChildren<ButtonTrigger>(true);
        for (int i = 0; i < triggers.Length; i++)
        {
            if (triggers[i] != null)
                Destroy(triggers[i]);
        }

        MonoBehaviour[] behaviours = visual.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
                continue;

            string typeName = behaviour.GetType().Name;
            if (typeName.IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Hover", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Slider", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Follow", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Destroy(behaviour);
            }
        }

        Transform side = visual.transform.Find("SideHolder");
        if (side != null)
            side.gameObject.SetActive(false);

        Transform keyboard = visual.transform.Find("Keyboard");
        if (keyboard != null)
            keyboard.gameObject.SetActive(false);
    }

    private Quaternion GetHeadMenuLocalRotation()
    {
        return Quaternion.Euler(0f, 270f, 0f);
    }

    private static Transform GetHandMenuTarget()
    {
        if (GTPlayer.Instance == null)
            return null;

        Transform controller = GTPlayer.Instance.LeftHand.controllerTransform;
        return controller != null ? controller : null;
    }

    private void RefreshHandMenuFollowPose()
    {
        if (!isMenuOpened || currentOpenType == MenuOpenType.Head || menuObj == null)
            return;

        SmoothFollowMenu sf = menuObj.GetComponent<SmoothFollowMenu>();
        if (sf == null)
            return;

        sf.LocalPosition = GetHandMenuLocalPosition();
        sf.LocalRotation = GetHandMenuLocalRotation();
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
            RefreshHandMenuFollowPose();
            yield return null;
        }

        if (menuObj != null && menuObj.activeSelf)
        {
            menuObj.transform.localScale = endScale;
            RefreshHandMenuFollowPose();
        }

        menuScaleRoutine = null;
    }

    public void SetHeadDistance(float distance)
    {
        headMenuDistance = Mathf.Clamp(distance, 0.35f, 1.0f);

        SmoothFollowMenu sf = menuObj?.GetComponent<SmoothFollowMenu>();
        if (sf != null && currentOpenType == MenuOpenType.Head)
            sf.LocalPosition = new Vector3(HeadMenuX, HeadMenuY, headMenuDistance);
    }

    public void PlayBtnCickSound()
    {
        if (!Mods.IsClickSoundEnabled())
            return;
        PlayUiClip(currentClickSound);
    }

    public void RefreshNamedButtonTitle(string buttonName, string label)
    {
        if (menuObj == null || string.IsNullOrEmpty(buttonName) || string.IsNullOrEmpty(label))
            return;

        Transform button = FindChildByName(menuObj.transform, buttonName);
        TMP_Text text = button != null ? GetButtonTitle(button) : null;
        if (text != null)
        {
            text.text = label;
            if (string.Equals(buttonName, "Fade Distance", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(buttonName, "Nametag Fade Distance", StringComparison.OrdinalIgnoreCase))
                nameTagFadeDistanceText = text;
        }
    }

    private void DestroyButtons()
    {
        foreach (GameObject btnObj in buttons)
            Destroy(btnObj);
        buttons.Clear();
    }
}
