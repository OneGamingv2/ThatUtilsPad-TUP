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

        string prefabPath = UseSakuraTheme
            ? "assets/prefabs/TUP-overv18.prefab"
            : "assets/prefabs/tup-modelsmooth.prefab";

        AssetBundle bundle = UseSakuraTheme ? menuReduxBundle : menuBundle;
        GameObject  prefab = bundle.LoadAsset<GameObject>(prefabPath);
        
        
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
    
    foreach (GameObject selectorObj in tabButtonObjs)
    {
        selectorObj.SetActive(true);
    }

    foreach (GameObject navBtn in pageButtonObjs)
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

    DestroyButtons();
    InitSelectorObj();
    buttonRoutine = StartCoroutine(CreateButtons());
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
        buttonSeed = buttonShelf != null ? buttonShelf.Find("Button")?.gameObject : null;
        if (buttonSeed == null)
            return;

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

        return btnName;
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

        KillLeftKeyboardDot();

        DestroyButtons();

        foreach (GameObject selectorObj in tabButtonObjs)
            if (selectorObj != null)
                selectorObj.SetActive(false);

        foreach (GameObject navBtn in pageButtonObjs)
            if (navBtn != null)
                navBtn.SetActive(false);

        isMenuClosing = false;
    }
    
    private IEnumerator CreateButtons(string categoryName = "Networking")
    {
        const float gap = 0.094f;

        if (!Mods.Actions.TryGetValue(currentCategory, out var category))
            yield break;

        SetSpotifyPageOnOff();
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
        SetSpotifyPageOnOff();
        buttonRoutine = StartCoroutine(CreateButtons());
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
            WakeUpTabButton(child);

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
            
            tabButtonObjs.Add(child.gameObject);
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

            pageButtonObjs.Add(navTransform.gameObject);
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
        SetSpotifyPageOnOff();
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

        Vector3 targetScale = usingButtonSeed ? btn.transform.localScale : buttonBaseScale;
        btn.transform.localScale = Vector3.zero;
        StartCoroutine(MenuEffects.PopButton(btn, targetScale, Vector3.zero, true));

        AudioSource audioSource = gameObject.GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        audioSource.volume = 0.1f;
        audioSource.PlayOneShot(btnEnterSound);

        buttons.Add(btn);
        InitCycleBtns();
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
        smooth.LocalPosition = new Vector3(0f, -0.05f, 0.6f);
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
        foreach (GameObject btnObj in buttons)
            Destroy(btnObj);
        buttons.Clear();
    }
}
