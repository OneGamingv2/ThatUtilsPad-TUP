using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using BepInEx;
using GorillaLocomotion;
using PlayFab;
using ThatUtilsPad.MenuComponents;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

namespace ThatUtilsPad;

[BepInPlugin("that.utils.pad", "ThatUtilsPad", "1.0.0")]
public class Main : BaseUnityPlugin
{
    private const string AdminsUrl = "https://playfabswapping.hu/data/tup/admins";

    // State
    private const bool UseSakuraTheme = true;
    private const bool AlwaysShowMenu = false;

    public static    Main             Instance;
    public           bool             IsAdmin;
    private readonly Color32          accentColor = new(203, 166, 247, 255);
    private readonly Color32          borderColor = new(12, 12, 22, 255);
    private readonly List<GameObject> btnObjs     = [];

    private readonly Vector3
            buttonBasePosition = new(-0.044f, 0f, 0f); // X = left/right, Y will be stacked, Z = always 0

    private readonly Quaternion buttonBaseRotation = Quaternion.Euler(0f, 0f, 180f); // Buttons face player
    private readonly Vector3    buttonBaseScale    = Vector3.one * 1.2f;

    private readonly Color32 buttonColor        = new(30, 30, 46, 255);
    private readonly Color32 buttonOutlineColor = new(18, 18, 36, 255);

    // Theme colors
    private readonly Color32 mainColor = new(17, 17, 27, 255);

    // Menu positioning
    private readonly Vector3 menuGripPosition = new(0f, -0.17f, 0f);
    private readonly Vector3 menuHandOffset   = new(0f, 0f, 0f);

    private GameObject  btnPrefab;
    private AssetBundle buttonBundle;

    private MenuOpenType currentOpenType;
    private AudioClip    helloSound;
    private bool         isMenuOpened;

    // Asset Bundles
    private AssetBundle menuBundle;
    private float       menuGripRotaton = -30f;

    private GameObject  menuObj;
    private AssetBundle sakuraBundle;

    private AudioClip  startSound;
    private GameObject stumpInfo;

    public static Camera FirstPersonCamera { get; private set; }
    public static Camera ThirdPersonCamera { get; private set; }

    private void Awake() => Instance = this;

    private void Start()
    {
        Debug.Log("\n \n"                                                              +
                  "   ================:  ┌○○○─TUP────────────────────────────── x ┐\n" +
                  "   .::=*=-+*--++-:.   ┣────────────────────────────────────────┫\n" +
                  "   .+*************-   │ TUP: ThatUtilsPad                      │\n" +
                  "      :*.     ++.     │ Discord: https://discord.gg/fuJcTWsn   │\n" +
                  "      :*.     ++.     │ Made by Jelly and Kwyf <3              │\n" +
                  "                      └────────────────────────────────────────┘\n");

        GorillaTagger.OnPlayerSpawned(OnPlayerSpawned);
    }

    private void Update()
    {
        bool controllerPressed = ControllerInputPoller.instance.leftControllerSecondaryButton;
        bool keyboardPressed   = Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame;

        if (!isMenuOpened)
        {
            if (controllerPressed || keyboardPressed || AlwaysShowMenu)
            {
                currentOpenType = controllerPressed ? MenuOpenType.Hand : MenuOpenType.Head;
                OpenMenu();
                isMenuOpened = true;
                CreateButtons();
            }
        }
        else
        {
            bool releaseCondition = !controllerPressed && !Keyboard.current.tKey.isPressed && !AlwaysShowMenu;

            if (releaseCondition)
                CloseMenu();
        }

        if (!isMenuOpened)
            return;

        if (!Mouse.current.leftButton.wasPressedThisFrame)
            return;

        Camera raycastCamera = GetActiveCamera();

        if (!Physics.Raycast(raycastCamera.ScreenPointToRay(Mouse.current.position.ReadValue()),
                    out RaycastHit hit,
                    0.6f, 1 << 2, QueryTriggerInteraction.Collide))
            return;

        if (hit.collider.TryGetComponent(out ButtonTrigger buttonTrigger))
            ButtonTrigger.PcPress(buttonTrigger);
    }

    private void OnPlayerSpawned()
    {
        FirstPersonCamera = GTPlayer.Instance.mainCamera;
        ThirdPersonCamera = GorillaTagger.Instance.thirdPersonCamera.transform.GetChild(0).GetComponent<Camera>();

        LoadAudio();
        PlayStartSound();
        CheckAdminStatus();
        Mods.Init();
        LoadBundles();

        //Init Shaders
        ShaderCache.Init();

        new GameObject("TUP_CoroutineHandler").AddComponent<CoroutineHandler>();
        btnPrefab = buttonBundle.LoadAsset<GameObject>("assets/prefabs/tup-buttonmodel.prefab");
    }

    private void CreateButtons()
    {
        const float Gap = 0.15f;
        
        CreateButton(0.38f,  "Disconnect");
        CreateButton(0.23f,  "Join Random");
        CreateButton(0.08f,  "Lobby Hop");
        CreateButton(-0.07f, "Copy Room");
        CreateButton(-0.22f, "Select User");
    }

    private void LoadAudio()
    {
        try
        {
            Assembly assembly = Assembly.GetExecutingAssembly();

            string[] resourceNames = assembly.GetManifestResourceNames();
            Debug.Log("[TUP] Embedded resources:");
            foreach (string name in resourceNames)
                Debug.Log("[TUP] - " + name);

            string startPath = null;
            string helloPath = null;

            foreach (string name in resourceNames)
            {
                if (name.Contains("tup_startup") || name.Contains("startup"))
                    startPath = name;

                if (name.Contains("hello"))
                    helloPath = name;
            }

            if (startPath != null)
                startSound = LoadAudioClip(assembly, startPath);
            else
                Debug.LogWarning("[TUP] Could not find startup sound");

            if (helloPath != null)
                helloSound = LoadAudioClip(assembly, helloPath);
            else
                Debug.LogWarning("[TUP] Could not find hello sound");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TUP] Failed to load audio: " + e.Message);
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
            return;

        AudioSource audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.PlayOneShot(startSound);
    }

    private void CheckAdminStatus()
    {
        try
        {
            string localPlayfabId = PlayFabSettings.staticPlayer.PlayFabId;

            using WebClient client = new();
            client.DownloadStringCompleted += (sender, e) =>
                                              {
                                                  if (e.Error != null)
                                                  {
                                                      Debug.LogWarning(
                                                              "[TUP] Failed to fetch admin list: " +
                                                              e.Error.Message);

                                                      return;
                                                  }

                                                  string[] ids = e.Result.Split(',');
                                                  foreach (string entry in ids)
                                                  {
                                                      string id = entry.Trim();

                                                      if (string.IsNullOrEmpty(id)) continue;

                                                      if (id != localPlayfabId)
                                                          continue;

                                                      IsAdmin = true;

                                                      break;
                                                  }

                                                  if (IsAdmin)
                                                  {
                                                      Debug.Log("[TUP] Logged in as Admin");
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
            Debug.LogWarning("[TUP] Admin check failed: " + e.Message);
        }
    }

    private void PlayHelloSound()
    {
        if (helloSound == null)
            return;

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
    }

    private void OpenMenu()
    {
        string prefabPath = UseSakuraTheme
                                    ? "assets/prefabs/tup-modelsmooth-sakura.prefab"
                                    : "assets/prefabs/tup-modelsmooth.prefab";

        AssetBundle bundle = UseSakuraTheme ? sakuraBundle : menuBundle;
        GameObject  prefab = bundle.LoadAsset<GameObject>(prefabPath);

        Transform parent;

        parent = currentOpenType == MenuOpenType.Head
                         ? GetActiveCamera().transform
                         : GTPlayer.Instance.LeftHand.controllerTransform;

        menuObj = Instantiate(prefab, parent, true);

        menuObj.transform.localScale = Vector3.one * 0.625f;

        if (currentOpenType == MenuOpenType.Head)
        {
            menuObj.transform.localPosition = new Vector3(0f, -0.1f, 0.7f);
            menuObj.transform.localRotation = Quaternion.Euler(0f, 270f, 0f);
        }
        else
        {
            menuObj.transform.localPosition = Vector3.zero + menuHandOffset + menuGripPosition;
            menuObj.transform.localRotation = Quaternion.Euler(270f, 180f, 0f);
        }

        Rigidbody? rb = menuObj.GetComponent<Rigidbody>();
        if (rb != null) Destroy(rb);

        Collider? collider = menuObj.GetComponent<Collider>();
        if (collider != null) Destroy(collider);

        Renderer? renderer = menuObj.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            renderer.material.shader = ShaderCache.UberShader;
            renderer.material.color  = new Color32(171, 0, 63, 255);
        }

        if (UseSakuraTheme)
            MenuTheme.AssignSakura(menuObj, mainColor, borderColor, accentColor);
        else
            MenuTheme.Assign(menuObj, mainColor, borderColor, accentColor);
    }

    private void CreateButton(float zOffset, string btnName)
    {
        GameObject btn         = Instantiate(btnPrefab, menuObj.transform);
        GameObject btnOutline  = Instantiate(btnPrefab, menuObj.transform);
        GameObject btnCollider = GameObject.CreatePrimitive(PrimitiveType.Cube);
        btnCollider.transform.SetParent(menuObj.transform, false);

        Vector3 stackedPos = buttonBasePosition + new Vector3(0f, zOffset, 0f);

        btn.transform.localPosition = stackedPos;
        btn.transform.localRotation = buttonBaseRotation;
        btn.transform.localScale    = buttonBaseScale;

        btnOutline.transform.localPosition = stackedPos;
        btnOutline.transform.localRotation = buttonBaseRotation;
        btnOutline.transform.localScale    = buttonBaseScale * 1.05f;

        btnCollider.transform.localPosition = stackedPos;
        btnCollider.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
        btnCollider.transform.localScale    = new Vector3(0.05f, 0.05f, 0.45f);
        btnCollider.layer                   = 18;

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
        rendererCollider.enabled = false;
        /*if (rendererCollider != null)
        {
            rendererCollider.material.shader = ShaderCache.TextShader;
            rendererCollider.material.color  = new Color32(255, 0, 0, 100);
        }*/

        GameObject textObj = new("ButtonLabel");
        textObj.transform.SetParent(btn.transform, false);
        textObj.transform.localPosition = new Vector3(0.02f, 0.003f, 0f);
        textObj.transform.localRotation = Quaternion.Euler(0f, 270f, 180f);

        TextMeshPro text = textObj.AddComponent<TextMeshPro>();
        text.text                 = btnName.ToUpper();
        text.fontSize             = 20;
        text.alignment            = TextAlignmentOptions.Center;
        text.color                = Color.white;
        text.font                 = VRRig.LocalRig.playerText1.font;
        text.enableAutoSizing     = false;
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

        menuBundle   = LoadBundle(assembly, "ThatUtilsPad.Assets.tup-prefab");
        sakuraBundle = LoadBundle(assembly, "ThatUtilsPad.Assets.tupsakura-prefab");
        buttonBundle = LoadBundle(assembly, "ThatUtilsPad.Assets.tupbutton-prefab");

        //foreach (string name in buttonBundle.GetAllAssetNames())
        //    Debug.Log("BUNDLE ASSET: " + name);
    }

    private AssetBundle LoadBundle(Assembly assembly, string resourceName)
    {
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        byte[]        buffer = new byte[stream.Length];
        // ReSharper disable once MustUseReturnValue
        stream.Read(buffer, 0, buffer.Length);

        return AssetBundle.LoadFromMemory(buffer);
    }

    public static Camera GetActiveCamera() => ThirdPersonCamera.gameObject.activeInHierarchy
                                                      ? ThirdPersonCamera
                                                      : FirstPersonCamera;

    private enum MenuOpenType
    {
        Hand,
        Head,
    }
}

public static class WavUtility
{
    public static AudioClip ToAudioClip(byte[] fileBytes, string name = "wav")
    {
        const int HeaderSize  = 44;
        int       subchunk1   = BitConverter.ToInt32(fileBytes, 16);
        ushort    audioFormat = BitConverter.ToUInt16(fileBytes, 20);

        ushort numChannels = BitConverter.ToUInt16(fileBytes, 22);
        int    sampleRate  = BitConverter.ToInt32(fileBytes, 24);
        ushort bitDepth    = BitConverter.ToUInt16(fileBytes, 34);

        int     dataSize = BitConverter.ToInt32(fileBytes, 40);
        float[] data     = new float[dataSize / (bitDepth / 8)];

        for (int i = 0; i < data.Length; i++)
        {
            int offset = HeaderSize + i * 2;
            data[i] = BitConverter.ToInt16(fileBytes, offset) / 32768.0f;
        }

        AudioClip audioClip = AudioClip.Create(name, data.Length / numChannels, numChannels, sampleRate, false);
        audioClip.SetData(data, 0);

        return audioClip;
    }
}