using System;
using GorillaNetworking;
using UnityEngine;
using static GorillaNetworking.CosmeticsController;

namespace CameraMod.Camera
{
    public class HeadCosmeticsHider : FromCameraHider
    {
        public static void setHeadCosmeticsEnabled(bool enable)
        {
            try
            {
                if (instance == null || instance.currentWornSet == null || instance.currentWornSet.items == null)
                    return;

                GorillaTagger tagger = GorillaTagger.Instance;
                if (tagger == null || tagger.offlineVRRig == null)
                    return;

                var registry = tagger.offlineVRRig.cosmeticsObjectRegistry;
                if (registry == null)
                    return;

                VRRig localRig = VRRig.LocalRig;
                CosmeticItem[] items = instance.currentWornSet.items;

                for (int i = 0; i < items.Length; i++)
                {
                    CosmeticItem item = items[i];
                    if (string.IsNullOrEmpty(item.displayName))
                        continue;

                    if (item.itemCategory != CosmeticCategory.Face &&
                        item.itemCategory != CosmeticCategory.Hat)
                        continue;

                    CosmeticSlots slotType = item.itemCategory == CosmeticCategory.Face
                        ? CosmeticSlots.Face
                        : CosmeticSlots.Hat;

                    var cosmeticInstance = registry.Cosmetic(item.displayName);
                    if (cosmeticInstance == null)
                        continue;

                    if (enable)
                    {
                        if (localRig != null)
                            cosmeticInstance.EnableItem(slotType, localRig);
                    }
                    else
                    {
                        cosmeticInstance.DisableItem(slotType);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TUP Camera] Head cosmetics toggle skipped: " + ex.Message);
            }
        }

        public override void Hide()
        {
            CameraController ctrl = CameraController.Instance;
            if (ctrl == null || ctrl.cameraMode != CameraMode.FirstPersonView)
                return;

            setHeadCosmeticsEnabled(false);
        }

        public override void Show()
        {
            setHeadCosmeticsEnabled(true);
        }
    }
}
