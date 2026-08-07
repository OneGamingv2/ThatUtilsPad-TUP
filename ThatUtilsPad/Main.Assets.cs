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
    private AudioSource notificationAudioSource;

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
        Mods.RebuildClickSounds();

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
        if (!Mods.IsNotifSoundEnabled() || Instance == null || Instance.notifSound == null) return;

        if (Instance.notificationAudioSource == null)
        {
            Instance.notificationAudioSource = Instance.gameObject.AddComponent<AudioSource>();
            Instance.notificationAudioSource.playOnAwake = false;
            Instance.notificationAudioSource.spatialBlend = 0f;
            Instance.notificationAudioSource.volume = 0.35f;
        }

        Instance.notificationAudioSource.PlayOneShot(Instance.notifSound);
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
        if (!Mods.IsStartupSoundEnabled() || startSound == null)
            return;

        PlayUiClip(startSound);
    }

    public void PlayUiClip(AudioClip clip, float volume = 1f)
    {
        if (clip == null)
            return;

        AudioSource audioSource = gameObject.GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    private void LoadBundles()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
buttonBundle             = LoadBundle(assembly, "ThatUtilsPad.Assets.Models.buttonmodelui2");
        menuReduxBundle          = LoadBundle(assembly, "ThatUtilsPad.Assets.Models.tup-overv18");
        notifBundle              = LoadBundle(assembly, "ThatUtilsPad.Assets.UI.notifdarkmode");
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
}
