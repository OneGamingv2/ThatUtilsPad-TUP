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
                    PlayHelloSound();
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
