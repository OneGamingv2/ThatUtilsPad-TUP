using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ThatUtilsPad;

internal static class GameTypeCloak
{
    private const byte Key = 0x57;

    private static readonly BindingFlags AllStatic =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    private static readonly BindingFlags AllInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static Type _cosmeticsType;
    private static Type _pollerType;
    private static MemberInfo _cosmeticsInstance;
    private static MethodInfo _loadSavedOutfit;
    private static MemberInfo _pollerInstance;
    private static MemberInfo _leftSecondary;
    private static MemberInfo _leftPrimary;
    private static MemberInfo _leftTriggerBtn;
    private static MemberInfo _leftIndex;
    private static MemberInfo _leftGrip;
    private static MemberInfo _rightSecondary;
    private static MemberInfo _rightPrimary;
    private static MemberInfo _rightTriggerBtn;
    private static MemberInfo _rightIndex;
    private static MemberInfo _rightGrip;

    private static string X(params byte[] data)
    {
        char[] chars = new char[data.Length];
        for (int i = 0; i < data.Length; i++)
            chars[i] = (char)(data[i] ^ Key);
        return new string(chars);
    }

    private static Type FindType(string fullName)
    {
        if (string.IsNullOrEmpty(fullName))
            return null;

        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type t = asm.GetType(fullName, false);
                if (t != null)
                    return t;
            }
            catch
            {
            }
        }

        return Type.GetType(fullName + ", Assembly-CSharp", false);
    }

    private static Type ResolveCosmeticsType()
    {
        if (_cosmeticsType != null)
            return _cosmeticsType;
        _cosmeticsType = FindType(X(
            0x10, 0x38, 0x25, 0x3E, 0x3B, 0x3B, 0x36, 0x19, 0x32, 0x23, 0x20, 0x38, 0x25, 0x3C, 0x3E, 0x39, 0x30, 0x79,
            0x14, 0x38, 0x24, 0x3A, 0x32, 0x23, 0x3E, 0x34, 0x24, 0x14, 0x38, 0x39, 0x23, 0x25, 0x38, 0x3B, 0x3B, 0x32, 0x25));
        return _cosmeticsType;
    }

    private static Type ResolvePollerType()
    {
        if (_pollerType != null)
            return _pollerType;
        _pollerType = FindType(X(
            0x14, 0x38, 0x39, 0x23, 0x25, 0x38, 0x3B, 0x3B, 0x32, 0x25, 0x1E, 0x39, 0x27, 0x22, 0x23, 0x07, 0x38, 0x3B, 0x3B, 0x32, 0x25));
        return _pollerType;
    }

    private static object ReadMember(object target, MemberInfo member)
    {
        if (member is FieldInfo fi)
            return fi.GetValue(target);
        if (member is PropertyInfo pi)
            return pi.GetValue(target, null);
        return null;
    }

    private static MemberInfo FindMember(Type type, string name, BindingFlags flags)
    {
        if (type == null)
            return null;
        return (MemberInfo)type.GetField(name, flags) ?? type.GetProperty(name, flags);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static object CosmeticsInstance()
    {
        Type t = ResolveCosmeticsType();
        if (t == null)
            return null;
        if (_cosmeticsInstance == null)
            _cosmeticsInstance = FindMember(t, X(0x3E, 0x39, 0x24, 0x23, 0x36, 0x39, 0x34, 0x32), AllStatic);
        return ReadMember(null, _cosmeticsInstance);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Array CosmeticsSavedOutfits(object controller)
    {
        if (controller == null)
            return null;
        FieldInfo savedField = controller.GetType().GetField(
            X(0x24, 0x36, 0x21, 0x32, 0x33, 0x18, 0x22, 0x23, 0x31, 0x3E, 0x23, 0x24),
            AllInstance);
        return savedField?.GetValue(controller) as Array;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void CosmeticsLoadSavedOutfit(object controller, int index)
    {
        if (controller == null)
            return;
        Type t = ResolveCosmeticsType();
        if (t == null)
            return;
        if (_loadSavedOutfit == null)
            _loadSavedOutfit = t.GetMethod(
                X(0x1B, 0x38, 0x36, 0x33, 0x04, 0x36, 0x21, 0x32, 0x33, 0x18, 0x22, 0x23, 0x31, 0x3E, 0x23),
                AllInstance);
        _loadSavedOutfit?.Invoke(controller, new object[] { index });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static object PollerInstance()
    {
        Type t = ResolvePollerType();
        if (t == null)
            return null;
        if (_pollerInstance == null)
            _pollerInstance = FindMember(t, X(0x3E, 0x39, 0x24, 0x23, 0x36, 0x39, 0x34, 0x32), AllStatic);
        return ReadMember(null, _pollerInstance);
    }

    private static bool ReadBool(object poller, ref MemberInfo cache, string name)
    {
        if (poller == null)
            return false;
        if (cache == null)
            cache = FindMember(poller.GetType(), name, AllInstance);
        object v = ReadMember(poller, cache);
        return v is bool b && b;
    }

    private static float ReadFloat(object poller, ref MemberInfo cache, string name)
    {
        if (poller == null)
            return 0f;
        if (cache == null)
            cache = FindMember(poller.GetType(), name, AllInstance);
        object v = ReadMember(poller, cache);
        return v is float f ? f : 0f;
    }

    internal static bool LeftSecondary(object p) =>
        ReadBool(p, ref _leftSecondary, X(0x3B, 0x32, 0x31, 0x23, 0x14, 0x38, 0x39, 0x23, 0x25, 0x38, 0x3B, 0x3B, 0x32, 0x25, 0x04, 0x32, 0x34, 0x38, 0x39, 0x33, 0x36, 0x25, 0x2E, 0x15, 0x22, 0x23, 0x23, 0x38, 0x39));

    internal static bool LeftPrimary(object p) =>
        ReadBool(p, ref _leftPrimary, X(0x3B, 0x32, 0x31, 0x23, 0x14, 0x38, 0x39, 0x23, 0x25, 0x38, 0x3B, 0x3B, 0x32, 0x25, 0x07, 0x25, 0x3E, 0x3A, 0x36, 0x25, 0x2E, 0x15, 0x22, 0x23, 0x23, 0x38, 0x39));

    internal static bool LeftTriggerButton(object p) =>
        ReadBool(p, ref _leftTriggerBtn, X(0x3B, 0x32, 0x31, 0x23, 0x14, 0x38, 0x39, 0x23, 0x25, 0x38, 0x3B, 0x3B, 0x32, 0x25, 0x03, 0x25, 0x3E, 0x30, 0x30, 0x32, 0x25, 0x15, 0x22, 0x23, 0x23, 0x38, 0x39));

    internal static float LeftIndex(object p) =>
        ReadFloat(p, ref _leftIndex, X(0x3B, 0x32, 0x31, 0x23, 0x14, 0x38, 0x39, 0x23, 0x25, 0x38, 0x3B, 0x3B, 0x32, 0x25, 0x1E, 0x39, 0x33, 0x32, 0x2F, 0x11, 0x3B, 0x38, 0x36, 0x23));

    internal static float LeftGrip(object p) =>
        ReadFloat(p, ref _leftGrip, X(0x3B, 0x32, 0x31, 0x23, 0x14, 0x38, 0x39, 0x23, 0x25, 0x38, 0x3B, 0x3B, 0x32, 0x25, 0x10, 0x25, 0x3E, 0x27, 0x11, 0x3B, 0x38, 0x36, 0x23));

    internal static bool RightSecondary(object p) =>
        ReadBool(p, ref _rightSecondary, X(0x25, 0x3E, 0x30, 0x3F, 0x23, 0x14, 0x38, 0x39, 0x23, 0x25, 0x38, 0x3B, 0x3B, 0x32, 0x25, 0x04, 0x32, 0x34, 0x38, 0x39, 0x33, 0x36, 0x25, 0x2E, 0x15, 0x22, 0x23, 0x23, 0x38, 0x39));

    internal static bool RightPrimary(object p) =>
        ReadBool(p, ref _rightPrimary, X(0x25, 0x3E, 0x30, 0x3F, 0x23, 0x14, 0x38, 0x39, 0x23, 0x25, 0x38, 0x3B, 0x3B, 0x32, 0x25, 0x07, 0x25, 0x3E, 0x3A, 0x36, 0x25, 0x2E, 0x15, 0x22, 0x23, 0x23, 0x38, 0x39));

    internal static bool RightTriggerButton(object p) =>
        ReadBool(p, ref _rightTriggerBtn, X(0x25, 0x3E, 0x30, 0x3F, 0x23, 0x14, 0x38, 0x39, 0x23, 0x25, 0x38, 0x3B, 0x3B, 0x32, 0x25, 0x03, 0x25, 0x3E, 0x30, 0x30, 0x32, 0x25, 0x15, 0x22, 0x23, 0x23, 0x38, 0x39));

    internal static float RightIndex(object p) =>
        ReadFloat(p, ref _rightIndex, X(0x25, 0x3E, 0x30, 0x3F, 0x23, 0x14, 0x38, 0x39, 0x23, 0x25, 0x38, 0x3B, 0x3B, 0x32, 0x25, 0x1E, 0x39, 0x33, 0x32, 0x2F, 0x11, 0x3B, 0x38, 0x36, 0x23));

    internal static float RightGrip(object p) =>
        ReadFloat(p, ref _rightGrip, X(0x25, 0x3E, 0x30, 0x3F, 0x23, 0x14, 0x38, 0x39, 0x23, 0x25, 0x38, 0x3B, 0x3B, 0x32, 0x25, 0x10, 0x25, 0x3E, 0x27, 0x11, 0x3B, 0x38, 0x36, 0x23));
}
