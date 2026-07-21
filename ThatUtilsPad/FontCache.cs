using System.IO;
using System.Reflection;
using UnityEngine;

namespace ThatUtilsPad;
public static class FontCache
{
    public static AssetBundle minecraftiaBundle { get; private set; }
    public static AssetBundle figtreeBundle { get; private set; }
    
    public static void LoadFonts()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        minecraftiaBundle        = LoadBundle(assembly, "ThatUtilsPad.Assets.Fonts.minecraftia");
        figtreeBundle            = LoadBundle(assembly, "ThatUtilsPad.Assets.Fonts.figtree");
    }
    
    private static AssetBundle LoadBundle(Assembly assembly, string resourceName)
    {
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        byte[] buffer = new byte[stream.Length];
        stream.Read(buffer, 0, buffer.Length);
        return AssetBundle.LoadFromMemory(buffer);
    }
}