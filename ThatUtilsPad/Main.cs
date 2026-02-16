using BepInEx;
using GorillaLocomotion;
using PlayFab;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using ThatUtilsPad;
using System.Reflection;
using TMPro;
using UnityEngine;
using static ThrowableBug;
using Debug = UnityEngine.Debug;

namespace ThatUtilsPad
{
    [BepInPlugin("that.utils.pad", "ThatUtilsPad", "1.0.0")]
    public class Main : BaseUnityPlugin
    {
        // Asset Bundles
        AssetBundle menuBundle;
        AssetBundle sakuraBundle;
        AssetBundle buttonBundle;

        GameObject btnPrefab;

        AudioClip startSound;
        AudioClip helloSound;

        // Menu positioning
        Vector3 menuGripPosition = new Vector3(0f, -0.17f, 0f);
        float menuGripRotaton = -30f;

        GameObject menuObj;
        GameObject stumpInfo = new GameObject("ButtonLabel");
        List<GameObject> btnObjs = new List<GameObject>();
        Vector3 menuHandOffset = new Vector3(0f, 0f, 0f);

        // State
        bool useSakuraTheme = true;
        bool isMenuOpened = false;
        bool alwaysShowMenu = true;

        // Theme colors
        Color32 mainColor = new Color32(17, 17, 27, 255);
        Color32 borderColor = new Color32(12, 12, 22, 255);
        Color32 accentColor = new Color32(203, 166, 247, 255);
        Color32 buttonColor = new Color32(30, 30, 46, 255);
        Color32 buttonOutlineColor = new Color32(18, 18, 36, 255);

        public static Main Instance;
        public static bool IsAdmin = false;

        const string AdminsUrl = "https://playfabswapping.hu/data/tup/admins";



        void Awake() { Instance = this; }



        void Start()
        {
            Debug.Log("\n \n" +
                "   ================:  ┌○○○─TUP────────────────────────────── x ┐\n" +
                "   .::=*=-+*--++-:.   ┣────────────────────────────────────────┫\n" +
                "   .+*************-   │ TUP: ThatUtilsPad                      │\n" +
                "      :*.     ++.     │ Discord: https://discord.gg/fuJcTWsn   │\n" +
                "      :*.     ++.     │ Made by Jelly and Kwyf <3              │\n" +
                "                      └────────────────────────────────────────┘\n");

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



        void Update()
        {
            bool shouldShow = ControllerInputPoller.instance.leftControllerSecondaryButton || alwaysShowMenu;
            StumpInfo();

            if (!isMenuOpened && shouldShow)
            {
                isMenuOpened = true;
                OpenMenu();
                CreateButton(0.24f, "Disconnect");
                CreateButton(0.17f, "Join Random");
                CreateButton(0.10f, "Lobby Hop");
                CreateButton(0.03f, "Copy Room");
                CreateButton(-0.04f, "Select User");
            }
            else if (isMenuOpened && !shouldShow)
            {
                CloseMenu();
            }
        }



        void StumpInfo()
        {
            if (stumpInfo != null)
            {
                stumpInfo.transform.LookAt(Camera.main.transform.position);
                return;
            }

            stumpInfo.transform.localPosition = new Vector3(-66.689f, 11.896f, -82.602f); // Middle of stump pos (took too long)
            stumpInfo.transform.localRotation = Quaternion.identity;
            stumpInfo.transform.LookAt(Camera.main.transform.position);
            stumpInfo.transform.Rotate(0f, 180f, 0f);

            var text = stumpInfo.AddComponent<TextMeshPro>();
            text.text = "ThatUtilsPad\n<size=15><color=white>Version 1.0.0</color></size>";
            text.color = accentColor;
            text.fontSize = 25;
            text.alignment = TextAlignmentOptions.Center;
            text.font = VRRig.LocalRig.playerText1.font;
            text.enableAutoSizing = false;
            text.richText = true;
            text.transform.localScale = Vector3.one * 0.12f;
            var rect = text.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(500f, 100f);
        }

        void LoadAudio()
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();

                string[] resourceNames = assembly.GetManifestResourceNames();
                Debug.Log("[TUP] Embedded resources:");
                foreach (string name in resourceNames)
                {
                    Debug.Log("[TUP] - " + name);
                }

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
            catch (System.Exception e)
            {
                Debug.LogWarning("[TUP] Failed to load audio: " + e.Message);
            }
        }

        AudioClip LoadAudioClip(Assembly assembly, string resourceName)
        {
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    Debug.LogWarning($"[TUP] {resourceName} not found in resources");
                    return null;
                }

                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);

                return WavUtility.ToAudioClip(buffer, resourceName);
            }
        }

        void PlayStartSound()
        {
            if (startSound != null)
            {
                AudioSource audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.PlayOneShot(startSound);
            }
        }

        void CheckAdminStatus()
        {
            try
            {
                string localPlayfabId = PlayFabSettings.staticPlayer.PlayFabId;

                using (WebClient client = new WebClient())
                {
                    client.DownloadStringCompleted += (sender, e) =>
                    {
                        if (e.Error != null)
                        {
                            Debug.LogWarning("[TUP] Failed to fetch admin list: " + e.Error.Message);
                            return;
                        }

                        string[] ids = e.Result.Split(',');
                        foreach (string entry in ids)
                        {
                            string id = entry.Trim();
                            if (string.IsNullOrEmpty(id)) continue;

                            if (id == localPlayfabId)
                            {
                                IsAdmin = true;
                                break;
                            }
                        }

                        if (IsAdmin)
                        {
                            Debug.Log("[TUP] Logged in as Admin");
                            PlayHelloSound();
                        }
                        else
                            Debug.Log("[TUP] Logged in as normal user");
                    };

                    client.DownloadStringAsync(new System.Uri(AdminsUrl));
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[TUP] Admin check failed: " + e.Message);
            }
        }

        void PlayHelloSound()
        {
            if (helloSound != null)
            {
                AudioSource audioSource = gameObject.GetComponent<AudioSource>();
                if (audioSource == null)
                    audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.PlayOneShot(helloSound);
            }
        }

        void CloseMenu()
        {
            isMenuOpened = false;
            Destroy(menuObj);
            DestroyButtons();
            menuObj = null;
        }

        void OpenMenu()
        {
            string prefabPath = useSakuraTheme
                ? "assets/prefabs/tup-modelsmooth-sakura.prefab"
                : "assets/prefabs/tup-modelsmooth.prefab";

            AssetBundle bundle = useSakuraTheme ? sakuraBundle : menuBundle;
            GameObject prefab = bundle.LoadAsset<GameObject>(prefabPath);
            menuObj = Instantiate(prefab);

            menuObj.transform.parent = GTPlayer.Instance.LeftHand.controllerTransform;
            menuObj.transform.localScale = Vector3.one * 0.625f;
            menuObj.transform.localPosition = Vector3.zero + menuHandOffset + menuGripPosition;
            menuObj.transform.localRotation = Quaternion.identity * Quaternion.Euler(270f, 180f, 0f);

            var rb = menuObj.GetComponent<Rigidbody>();
            if (rb != null) Destroy(rb);

            var collider = menuObj.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            var renderer = menuObj.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                renderer.material.shader = ShaderCache.UberShader;
                renderer.material.color = new Color32(171, 0, 63, 255);
            }

            if (useSakuraTheme)
                MenuTheme.AssignSakura(menuObj, mainColor, borderColor, accentColor);
            else
                MenuTheme.Assign(menuObj, mainColor, borderColor, accentColor);
        }

        void CreateButton(float zOffset, string btnName)
        {
            GameObject btn = Instantiate(btnPrefab);
            GameObject btnOutline = Instantiate(btnPrefab);
            GameObject btnCollider = GameObject.CreatePrimitive(PrimitiveType.Cube);

            btn.transform.localScale = Vector3.one * 0.78f;
            btn.transform.localRotation = Quaternion.identity;

            btnCollider.transform.localScale = new Vector3(0.015f, 0.265f, 0.04f);
            btnCollider.transform.localRotation = Quaternion.identity;
            btnCollider.layer = 18;

            btnOutline.transform.localScale = Vector3.one * 0.79f;
            btnOutline.transform.localRotation = Quaternion.identity;

            var follow = btn.AddComponent<FollowMenu>();
            follow.target = GTPlayer.Instance.LeftHand.controllerTransform;
            follow.position = new Vector3(0.026f, 0f, zOffset) + menuHandOffset + menuGripPosition;
            follow.rotation = Quaternion.identity * Quaternion.Euler(270f, 0f, 0f);

            var followCollider = btnCollider.AddComponent<FollowMenu>();
            followCollider.target = GTPlayer.Instance.LeftHand.controllerTransform;
            followCollider.position = new Vector3(0.026f, 0f, zOffset) + menuHandOffset + menuGripPosition;
            followCollider.rotation = Quaternion.identity;

            var followOutline = btnOutline.AddComponent<FollowMenu>();
            followOutline.target = GTPlayer.Instance.LeftHand.controllerTransform;
            followOutline.position = new Vector3(0.025f, 0f, zOffset) + menuHandOffset + menuGripPosition;
            followOutline.rotation = Quaternion.identity * Quaternion.Euler(270f, 0f, 0f);

            var trigger = btnCollider.AddComponent<ButtonTrigger>();
            trigger.btnIdentifier = btnName;
            trigger.pressButtonSoundIndex = 28;

            btnCollider.GetComponent<Collider>().isTrigger = true;
            btnCollider.GetComponent<Rigidbody>().Destroy();

            var renderer = btn.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                renderer.material.shader = ShaderCache.UberShader;
                renderer.material.color = buttonColor;
            }

            var rendererCollider = btnCollider.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                rendererCollider.material.shader = ShaderCache.TextShader;
                rendererCollider.material.color = new Color32(255, 0, 0, 100);
            }

            var rendererOutline = btnOutline.GetComponentInChildren<Renderer>();
            if (rendererOutline != null)
            {
                rendererOutline.material.shader = ShaderCache.UberShader;
                rendererOutline.material.color = buttonOutlineColor;
            }

            GameObject textObj = new GameObject("ButtonLabel");
            textObj.transform.SetParent(btn.transform);
            textObj.transform.localPosition = new Vector3(0.023f, 0f, 0f);
            textObj.transform.localRotation = Quaternion.Euler(0f, -90f, 180f);

            var text = textObj.AddComponent<TextMeshPro>();
            text.text = btnName.ToUpper();
            text.fontSize = 20;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.font = VRRig.LocalRig.playerText1.font;
            text.enableAutoSizing = false;
            text.transform.localScale = new Vector3(0.02f, 0.018f, 2f);

            btnObjs.Add(btn);
            btnObjs.Add(btnOutline);
            btnObjs.Add(btnCollider);
        }

        void DestroyButtons()
        {
            foreach (GameObject btnObj in btnObjs)
                Destroy(btnObj);

            btnObjs.Clear();
        }

        void LoadBundles()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();

            menuBundle = LoadBundle(assembly, "ThatUtilsPad.Assets.tup-prefab");
            sakuraBundle = LoadBundle(assembly, "ThatUtilsPad.Assets.tupsakura-prefab");
            buttonBundle = LoadBundle(assembly, "ThatUtilsPad.Assets.tupbutton-prefab");

            //foreach (string name in buttonBundle.GetAllAssetNames())
            //    Debug.Log("BUNDLE ASSET: " + name);
        }

        AssetBundle LoadBundle(Assembly assembly, string resourceName)
        {
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);
                return AssetBundle.LoadFromMemory(buffer);
            }
        }
    }

    public static class WavUtility
    {
        public static AudioClip ToAudioClip(byte[] fileBytes, string name = "wav")
        {
            int headerSize = 44;
            int subchunk1 = BitConverter.ToInt32(fileBytes, 16);
            ushort audioFormat = BitConverter.ToUInt16(fileBytes, 20);

            ushort numChannels = BitConverter.ToUInt16(fileBytes, 22);
            int sampleRate = BitConverter.ToInt32(fileBytes, 24);
            ushort bitDepth = BitConverter.ToUInt16(fileBytes, 34);

            int dataSize = BitConverter.ToInt32(fileBytes, 40);
            float[] data = new float[dataSize / (bitDepth / 8)];

            for (int i = 0; i < data.Length; i++)
            {
                int offset = headerSize + (i * 2);
                data[i] = BitConverter.ToInt16(fileBytes, offset) / 32768.0f;
            }

            AudioClip audioClip = AudioClip.Create(name, data.Length / numChannels, numChannels, sampleRate, false);
            audioClip.SetData(data, 0);

            return audioClip;
        }
    }
}