#pragma once

#ifdef RENDERBRIDGE_EXPORTS
#define RB_API __declspec(dllexport)
#else
#define RB_API __declspec(dllimport)
#endif

#ifdef __cplusplus
extern "C" {
#endif

RB_API int RB_Init(void);
RB_API void RB_Shutdown(void);
RB_API int RB_IsActive(void);

// Full ReShade-class look. densoise/quality help avoid lost / blocky pixels.
RB_API void RB_SetLook(
    float master,
    float ssao,
    float ssr,
    float contrast,
    float coolTint,
    float vignette,
    float bloom,
    float fog,
    float godrays,
    float grain,
    float sharpen,
    float saturation,
    float ca,
    float heightFog,
    float fogR,
    float fogG,
    float fogB,
    float grass,
    float bump,
    float detail,
    float denoise,
    float quality);

#ifdef __cplusplus
}
#endif
