using UnityEngine;

namespace ThatUtilsPad;

public static class ShaderCache
{
    public static Shader UberShader { get; private set; }
    public static Shader TextShader { get; private set; }

    public static void Init()
    {
        UberShader = Shader.Find("GorillaTag/UberShader");
        TextShader = Shader.Find("GUI/Text Shader");

        if (UberShader == null)
            Debug.LogError("[TUP] Failed to find UberShader!");

        if (TextShader == null)
            Debug.LogError("[TUP] Failed to find TextShader!");
    }
}
