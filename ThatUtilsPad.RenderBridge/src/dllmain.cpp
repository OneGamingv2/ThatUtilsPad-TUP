#include "../include/RenderBridge.h"
#include "PresentHook.h"
#include <Windows.h>

static bool g_Inited = false;

RB_API int RB_Init(void)
{
    if (g_Inited)
        return 0;
    if (!PresentHook_Install())
        return -1;
    g_Inited = true;
    return 0;
}

RB_API void RB_Shutdown(void)
{
    if (!g_Inited)
        return;
    PresentHook_Remove();
    g_Inited = false;
}

RB_API int RB_IsActive(void)
{
    return (g_Inited && PresentHook_IsReady()) ? 1 : 0;
}

RB_API void RB_SetLook(
    float master, float ssao, float ssr, float contrast,
    float coolTint, float vignette, float bloom, float fog,
    float godrays, float grain, float sharpen, float saturation,
    float ca, float heightFog, float fogR, float fogG, float fogB,
    float grass, float bump, float detail, float denoise, float quality)
{
    RBLookParams p;
    p.master = master;
    p.ssao = ssao;
    p.ssr = ssr;
    p.contrast = contrast;
    p.coolTint = coolTint;
    p.vignette = vignette;
    p.bloom = bloom;
    p.fog = fog;
    p.godrays = godrays;
    p.grain = grain;
    p.sharpen = sharpen;
    p.saturation = saturation;
    p.ca = ca;
    p.heightFog = heightFog;
    p.fogR = fogR;
    p.fogG = fogG;
    p.fogB = fogB;
    p.grass = grass;
    p.bump = bump;
    p.detail = detail;
    p.denoise = denoise;
    p.quality = quality;
    PresentHook_SetParams(p);
}

BOOL APIENTRY DllMain(HMODULE, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_DETACH)
        RB_Shutdown();
    return TRUE;
}
