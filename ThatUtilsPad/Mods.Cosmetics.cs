using System;
using System.Collections;
using GorillaLocomotion;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GorillaGameModes;
using GorillaNetworking;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.XR;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;
using PlayFab;
using PlayFab.ClientModels;
using Photon.Voice.Unity;
using TMPro;
using ThatUtilsPad.MenuComponents;
using UnityEngine.UI;
using Newtonsoft.Json.Linq;
using Debug = UnityEngine.Debug;

namespace ThatUtilsPad;


public static partial class Mods
{
    private static void OutfitSpinner()
    {
        var root = GameObject.Find("Environment Objects");
        var spinner =
            root.transform.Find("LocalObjects_Prefab/TreeRoom/TreeRoomInteractables/UI/SatelliteWardrobe/WornDisplay");
        
        GameObject spinnerObj = GameObject.Instantiate(spinner.gameObject);
        spinnerObj.transform.SetParent(GTPlayer.Instance.transform);
        spinnerObj.transform.position =
            Camera.main.transform.position + Camera.main.transform.forward * 0.4f - Camera.main.transform.up * 0.4f;
        spinnerObj.transform.localRotation = Quaternion.identity;
        spinnerObj.transform.LookAt(GTPlayer.Instance.bodyCollider.transform);
        spinnerObj.transform.localScale = Vector3.one;
        
        
        var buttons =
            root.transform.Find("LocalObjects_Prefab/TreeRoom/TreeRoomInteractables/UI/SatelliteWardrobe/UI/OutfitButtons/");
        
        GameObject btnsOj = GameObject.Instantiate(buttons.gameObject);
        
        btnsOj.transform.position =
            Camera.main.transform.position + Camera.main.transform.forward * 0.4f - Camera.main.transform.up * 0.4f;
        btnsOj.transform.localRotation = Quaternion.identity;
        btnsOj.transform.LookAt(GTPlayer.Instance.bodyCollider.transform);
        btnsOj.transform.localScale = Vector3.one;
    }

    private static void RotateOutfits()
    {
        BrowseOutfit(1);
    }

    private static int NextOutfitNumber()
    {
        for (int i = 1; i <= 10; i++)
            if (!savedOutfitSlots.ContainsKey(i)) return i;
        return -1;
    }

    private static void SaveCurrentOutfit()
    {
        if (savedOutfitSlots.Count >= 10) return;

        int n = NextOutfitNumber();
        if (n < 0) return;
        int slotIdx = browsingOutfitIndex;

        savedOutfitSlots[n] = slotIdx;
        SaveButtonStates();
        Main.Instance?.RefreshOutfitPreviewDelayed();
    }

    private static void LoadOutfitSlot(int index)
    {
        object controller = GameTypeCloak.CosmeticsInstance();
        if (controller == null) return;

        int count = GetOutfitCount();
        if (count <= 0) return;
        index = ((index % count) + count) % count;

        GameTypeCloak.CosmeticsLoadSavedOutfit(controller, index);
        browsingOutfitIndex = index;
        currentOutfitIndex = index;
        Main.Instance?.RefreshOutfitPreviewDelayed();
    }

    private static int browsingOutfitIndex;
    private static int equippedOutfitIndex = -1;

    public static int GetCurrentOutfitIndex() => browsingOutfitIndex;

    public static int GetOutfitCount()
    {
        int count = 10;
        object controller = GameTypeCloak.CosmeticsInstance();
        if (controller != null)
        {
            Array outfits = GameTypeCloak.CosmeticsSavedOutfits(controller);
            if (outfits != null && outfits.Length > 0)
                count = Mathf.Max(outfits.Length, 10);
        }
        else if (savedOutfitSlots.Count > 0)
        {
            count = Mathf.Max(savedOutfitSlots.Count, 10);
        }

        return count;
    }

    public static string GetBrowsingOutfitLabel()
        => "Outfit #" + (browsingOutfitIndex + 1);

    public static string GetOutfitDotsLabel()
    {
        int count = Mathf.Clamp(GetOutfitCount(), 1, 10);
        int index = Mathf.Clamp(browsingOutfitIndex, 0, count - 1);
        var chars = new char[count * 2 - 1];
        for (int i = 0; i < count; i++)
        {
            chars[i * 2] = i == index ? '\u25CF' : '\u25CB'; // ● / ○
            if (i * 2 + 1 < chars.Length)
                chars[i * 2 + 1] = ' ';
        }
        return new string(chars);
    }

    public static bool IsBrowsingOutfitEquipped()
        => equippedOutfitIndex >= 0 && equippedOutfitIndex == browsingOutfitIndex;

    public static void BrowseOutfit(int delta)
    {
        int count = GetOutfitCount();
        if (count <= 0)
            return;

        browsingOutfitIndex = (browsingOutfitIndex + delta) % count;
        if (browsingOutfitIndex < 0)
            browsingOutfitIndex += count;

        PreviewBrowsingOutfit();
    }

    public static void BrowseToOutfit(int index)
    {
        int count = GetOutfitCount();
        if (count <= 0)
            return;
        browsingOutfitIndex = ((index % count) + count) % count;
        PreviewBrowsingOutfit();
    }

    private static void PreviewBrowsingOutfit()
    {
        object controller = GameTypeCloak.CosmeticsInstance();
        if (controller == null)
        {
            Main.Instance?.OnOutfitCarouselChanged();
            return;
        }

        GameTypeCloak.CosmeticsLoadSavedOutfit(controller, browsingOutfitIndex);
        currentOutfitIndex = browsingOutfitIndex;
        Main.Instance?.RefreshOutfitPreviewDelayed();
        Main.Instance?.OnOutfitCarouselChanged();
    }

    private static void EquipBrowsingOutfit()
    {
        object controller = GameTypeCloak.CosmeticsInstance();
        if (controller == null)
        {
            ShowNotification("Cosmetics unavailable", NotificationDefaultDuration);
            return;
        }

        GameTypeCloak.CosmeticsLoadSavedOutfit(controller, browsingOutfitIndex);
        currentOutfitIndex = browsingOutfitIndex;
        equippedOutfitIndex = browsingOutfitIndex;
        ShowNotification("Equipped " + GetBrowsingOutfitLabel(), NotificationDefaultDuration);
        Main.Instance?.RefreshOutfitPreviewDelayed();
        Main.Instance?.OnOutfitCarouselChanged();
    }

    private static void PreviousOutfit() => BrowseOutfit(-1);
    private static void NextOutfit() => BrowseOutfit(1);

    public static VRRig GetLocalVRRig()
    {
        if (GorillaTagger.Instance != null && GorillaTagger.Instance.offlineVRRig != null)
            return GorillaTagger.Instance.offlineVRRig;

        foreach (VRRig rig in VRRigCache.ActiveRigs)
        {
            if (rig != null && rig.isLocal && rig.gameObject.activeInHierarchy)
                return rig;
        }

        return null;
    }

    public static void InitOutfitSlotCaches()
    {
        int count = GetOutfitCount();
        browsingOutfitIndex = Mathf.Clamp(browsingOutfitIndex, 0, Mathf.Max(0, count - 1));
        if (equippedOutfitIndex < 0)
            equippedOutfitIndex = browsingOutfitIndex;

        Actions["Cosmetics"].Actions.Clear();
        Actions["Cosmetics"].Actions["Previous"] = new ModAction(PreviousOutfit, false);
        Actions["Cosmetics"].Actions["Next"] = new ModAction(NextOutfit, false);
        Actions["Cosmetics"].Actions["Equip Outfit"] = new ModAction(EquipBrowsingOutfit, false);
    }

    public static string GetOutfitDisplayName(int n)
    {
        if (outfitSlotNames.TryGetValue(n, out string name))
            return name;
        bool equipped = equippedOutfitIndex + 1 == n;
        return equipped ? "Outfit #" + n + "  (on)" : "Outfit #" + n;
    }

    public static void StartRename(int outfitN, TMP_Text label, GameObject keyboard)
    {
        if (isRenaming) return;
        isRenaming        = true;
        renamingOutfitN   = outfitN;
        renamingLabel     = label;
        renameKeyboard    = keyboard;
        originalLabel     = label.text;
        currentRenameText = outfitSlotNames.TryGetValue(outfitN, out string saved) ? saved : "";
        if (renamingLabel != null) renamingLabel.text = currentRenameText;

        Main.Instance?.PutMenuInTypingSpot();

        Main.ShowKeyboard(keyboard);
        Main.Instance?.SetKeyboardPreview(keyboard, currentRenameText, false);
    }

    public static void TypeChar(char c)
    {
        if (!isRenaming || currentRenameText.Length >= 10) return;
        currentRenameText += c;
        if (renamingLabel != null) renamingLabel.text = currentRenameText;
        Main.Instance?.SetKeyboardPreview(renameKeyboard, currentRenameText, true);
    }

    public static void DeleteChar()
    {
        if (!isRenaming || currentRenameText.Length == 0) return;
        currentRenameText = currentRenameText[..^1];
        if (renamingLabel != null) renamingLabel.text = currentRenameText;
        Main.Instance?.SetKeyboardPreview(renameKeyboard, currentRenameText, false);
    }

    public static void ConfirmRename()
    {
        if (!isRenaming) return;
        if (isRoomCodeSearch)
        {
            ConfirmRoomCodeJoin();
            return;
        }

        if (currentRenameText.Length > 0)
        {
            outfitSlotNames[renamingOutfitN] = currentRenameText;
            if (renamingLabel != null) renamingLabel.text = currentRenameText;
            SaveButtonStates();
        }
        else
        {
            if (renamingLabel != null) renamingLabel.text = GetOutfitDisplayName(renamingOutfitN);
        }
        CloseRename();
    }

    public static void CancelRename()
    {
        if (!isRenaming) return;
        if (isRoomCodeSearch)
        {
            CloseRoomCodeSearchUi();
            return;
        }

        if (renamingLabel != null) renamingLabel.text = originalLabel;
        CloseRename();
    }

    private static void CloseRename()
    {
        Main.Instance?.RestoreMenuFollowAfterKeyboard();

        Main.HideKeyboard(renameKeyboard);

        isRenaming      = false;
        renamingLabel   = null;
        renameKeyboard  = null;
        renamingOutfitN = -1;
    }

    public static void DeleteSavedOutfit(int n)
    {
        savedOutfitSlots.Remove(n);
        outfitSlotNames.Remove(n);
        SaveButtonStates();
        Main.Instance?.RefreshCurrentPage();
        Main.Instance?.ApplyOutfitCarouselSideUi();
    }
}
