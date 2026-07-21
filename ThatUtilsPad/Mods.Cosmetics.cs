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
        var controller = CosmeticsController.instance;
        if (controller == null)
        {
            return;
        }

        FieldInfo savedField = controller.GetType().GetField(
            "savedOutfits",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
        );

        if (savedField == null)
        {
            Debug.LogError("[TUP] 'savedOutfits' field not found");
            return;
        }

        var outfits = savedField.GetValue(controller) as Array;
        if (outfits == null || outfits.Length == 0)
            return;

        currentOutfitIndex = (currentOutfitIndex + 1) % outfits.Length;
        controller.LoadSavedOutfit(currentOutfitIndex);
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
        int slotIdx = currentOutfitIndex;

        savedOutfitSlots[n] = slotIdx;
        Actions["Cosmetics"].Actions["Saved Outfit #" + n] = new ModAction(() => LoadOutfitSlot(slotIdx));

        SaveButtonStates();
        Main.Instance?.AppendOutfitButton(n);

    }

    private static void LoadOutfitSlot(int index)
    {
        var controller = CosmeticsController.instance;
        if (controller == null) return;

        controller.LoadSavedOutfit(index);
        currentOutfitIndex = index;
    }

    public static void InitOutfitSlotActions()
    {
        int count = 5;

        var controller = CosmeticsController.instance;
        if (controller != null)
        {
            FieldInfo savedField = controller.GetType().GetField(
                "savedOutfits",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
            );
            if (savedField != null)
            {
                var outfits = savedField.GetValue(controller) as Array;
                if (outfits != null && outfits.Length > 0)
                    count = outfits.Length;
            }
        }

        Actions["Cosmetics"].Actions.Clear();
        Actions["Cosmetics"].Actions["Rotate Outfit"] = new ModAction(RotateOutfits, false);
        for (int i = 0; i < count; i++)
        {
            int slot = i;
            int n    = i + 1;
            Actions["Cosmetics"].Actions["Saved Outfit #" + n] = new ModAction(() => LoadOutfitSlot(slot));
        }
    }

    public static string GetOutfitDisplayName(int n)
        => outfitSlotNames.TryGetValue(n, out string name) ? name : "Outfit Slot #" + n;

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
        Actions["Cosmetics"].Actions.Remove("Saved Outfit #" + n);
        SaveButtonStates();
        Main.Instance?.RefreshCurrentPage();
    }
}
