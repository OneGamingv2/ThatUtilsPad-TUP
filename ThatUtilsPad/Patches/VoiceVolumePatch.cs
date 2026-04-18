using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ThatUtilsPad.Patches;

[HarmonyPatch(typeof(VRRig))]
[HarmonyPatch(nameof(VRRig.PostTick))]
internal static class VoiceVolumePatch
{
    private static void Postfix(VRRig __instance)
    {
        if (__instance.OwningNetPlayer == null) return;

        AudioSource voice = Traverse.Create(__instance).Field("voiceAudio").GetValue<AudioSource>();

        if (voice == null)
            return;

        string id = __instance.OwningNetPlayer.UserId;

        if (!Main.VolumeByPlayerID.ContainsKey(id))
            Main.VolumeByPlayerID[id] = 1f;

        float vol = Main.VolumeByPlayerID.GetValueOrDefault(id, 1f);

        voice.volume = Mathf.Clamp(vol, 0f, 2f);
    }
}