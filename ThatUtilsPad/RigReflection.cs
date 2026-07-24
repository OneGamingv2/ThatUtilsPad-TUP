using System;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ThatUtilsPad;

public static class CosmeticBits
{
    private static readonly MethodInfo IsTempCosmeticAllowed;

    static CosmeticBits()
    {
        Assembly assembly = typeof(VRRig).Assembly;
        Type cosmeticsType = assembly.GetType("PlayerCosmeticsSystem");
        IsTempCosmeticAllowed = cosmeticsType.GetMethod(
            "IsTemporaryCosmeticAllowed",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    }

    public static bool IsTemporaryCosmeticAllowed(VRRig rig, string cosmetic)
    {
        if (IsTempCosmeticAllowed == null)
            return false;

        object result = IsTempCosmeticAllowed.Invoke(null, new object[] { rig, cosmetic });
        return (bool)result;
    }
}

public static class VRRigCosmeticsExtensions
{
    private static readonly FieldInfo CosmeticsField = typeof(VRRig).GetField(
        "_playerOwnedCosmetics",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private static readonly FieldInfo TemporaryCosmeticsField = typeof(VRRig).GetField(
        "_temporaryCosmetics",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    public static string Cosmetics(this VRRig rig)
    {
        if (rig == null)
            return "";

        StringBuilder builder = new StringBuilder(256);
        AppendCosmeticCollection(builder, CosmeticsField != null ? CosmeticsField.GetValue(rig) : null);
        AppendCosmeticCollection(builder, TemporaryCosmeticsField != null ? TemporaryCosmeticsField.GetValue(rig) : null);

        try
        {
            if (rig.cosmeticSet != null && rig.cosmeticSet.items != null)
            {
                for (int i = 0; i < rig.cosmeticSet.items.Length; i++)
                {
                    object itemObj = rig.cosmeticSet.items[i];
                    if (itemObj == null)
                        continue;

                    Type itemType = itemObj.GetType();
                    FieldInfo nullField = itemType.GetField("isNullItem");
                    if (nullField != null && nullField.GetValue(itemObj) is bool isNull && isNull)
                        continue;

                    FieldInfo nameField = itemType.GetField("itemName") ?? itemType.GetField("displayName");
                    object nameValue = nameField != null ? nameField.GetValue(itemObj) : null;
                    if (nameValue != null)
                        builder.Append(nameValue).Append(' ');
                    else
                        builder.Append(itemObj).Append(' ');
                }
            }
        }
        catch { }

        try
        {
            if (rig.activeCosmetics != null)
            {
                for (int i = 0; i < rig.activeCosmetics.Count; i++)
                {
                    object cosmetic = rig.activeCosmetics[i];
                    if (cosmetic != null)
                        builder.Append(cosmetic).Append(' ');
                }
            }
        }
        catch { }

        return builder.ToString();
    }

    private static void AppendCosmeticCollection(StringBuilder builder, object value)
    {
        if (value == null || builder == null)
            return;

        if (value is System.Collections.IEnumerable enumerable)
        {
            foreach (object entry in enumerable)
            {
                if (entry != null)
                    builder.Append(entry).Append(' ');
            }
        }
        else
        {
            builder.Append(value).Append(' ');
        }
    }
}

public static class RigBits
{
    private static readonly FieldInfo FpsField = typeof(VRRig).GetField(
        "fps",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private static readonly FieldInfo PcTierField = typeof(VRRig).GetField(
        "currentRankedSubTierPC",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private static readonly FieldInfo QuestTierField = typeof(VRRig).GetField(
        "currentRankedSubTierQuest",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    public static int GetFPS(VRRig rig) =>
        FpsField != null ? (int)FpsField.GetValue(rig) : -1;

    public static int GetPCTier(VRRig rig) =>
        PcTierField != null ? (int)PcTierField.GetValue(rig) : 0;

    public static int GetQuestTier(VRRig rig) =>
        QuestTierField != null ? (int)QuestTierField.GetValue(rig) : 0;
}
