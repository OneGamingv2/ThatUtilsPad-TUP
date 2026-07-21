using System;
using System.Linq;
using System.Reflection;

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
        BindingFlags.Instance | BindingFlags.NonPublic);

    public static string Cosmetics(this VRRig rig)
    {
        if (CosmeticsField == null)
            return "";

        object value = CosmeticsField.GetValue(rig);
        if (value == null)
            return "";

        if (value is System.Collections.IEnumerable enumerable)
            return string.Join(",", enumerable.Cast<object>());

        return value.ToString();
    }
}

public static class RigBits
{
    private static readonly FieldInfo FpsField = typeof(VRRig).GetField(
        "fps",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo PcTierField = typeof(VRRig).GetField(
        "currentRankedSubTierPC",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo QuestTierField = typeof(VRRig).GetField(
        "currentRankedSubTierQuest",
        BindingFlags.Instance | BindingFlags.NonPublic);

    public static int GetFPS(VRRig rig) =>
        FpsField != null ? (int)FpsField.GetValue(rig) : -1;

    public static int GetPCTier(VRRig rig) =>
        PcTierField != null ? (int)PcTierField.GetValue(rig) : 0;

    public static int GetQuestTier(VRRig rig) =>
        QuestTierField != null ? (int)QuestTierField.GetValue(rig) : 0;
}
