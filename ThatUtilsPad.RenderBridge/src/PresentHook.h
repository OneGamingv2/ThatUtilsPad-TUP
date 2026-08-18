#pragma once
#include <d3d11.h>
#include <dxgi.h>

struct RBLookParams
{
    float master = 1.f;
    float ssao = 0.65f;
    float ssr = 0.55f;
    float contrast = 1.15f;
    float coolTint = 0.15f;
    float vignette = 0.25f;
    float bloom = 0.35f;
    float fog = 0.45f;
    float godrays = 0.35f;
    float grain = 0.f;
    float sharpen = 0.35f;
    float saturation = 1.05f;
    float ca = 0.04f;
    float heightFog = 0.35f;
    float fogR = 0.62f;
    float fogG = 0.72f;
    float fogB = 0.82f;
    float grass = 0.55f;   // foliage / grass green revival
    float bump = 0.5f;     // underside / contact bump cues
    float detail = 0.45f;  // micro surface detail
    float denoise = 0.35f; // soften blocky / lost-pixel look
    float quality = 1.f;   // 0 low / 1 med / 2 high (sample budget)
};

bool PresentHook_Install();
void PresentHook_Remove();
void PresentHook_SetParams(const RBLookParams& p);
RBLookParams PresentHook_GetParams();
bool PresentHook_IsReady();
