#include "PresentHook.h"
#include "PostFX.h"
#include <d3d11.h>
#include <dxgi.h>
#include <Windows.h>
#include <atomic>
#include <mutex>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")

namespace
{
    using PresentFn = HRESULT(__stdcall*)(IDXGISwapChain*, UINT, UINT);

    PresentFn g_OriginalPresent = nullptr;
    void** g_PresentVtableSlot = nullptr;
    PresentFn g_HookedPresentAddr = nullptr;

    PostFXContext g_Fx{};
    RBLookParams g_Look{};
    std::mutex g_LookMutex;
    std::atomic<bool> g_Installed{ false };
    std::atomic<bool> g_Ready{ false };

    HRESULT __stdcall HookedPresent(IDXGISwapChain* swap, UINT sync, UINT flags)
    {
        RBLookParams look;
        {
            std::lock_guard<std::mutex> lock(g_LookMutex);
            look = g_Look;
        }

        // Optimization: skip all Device work when FX master is off
        if (swap && look.master > 0.01f)
        {
            ID3D11Device* device = nullptr;
            if (SUCCEEDED(swap->GetDevice(__uuidof(ID3D11Device), (void**)&device)) && device)
            {
                if (PostFX_Ensure(device, swap, g_Fx))
                {
                    PostFX_Apply(swap, g_Fx, look);
                    g_Ready = true;
                }
                device->Release();
            }
        }

        return g_OriginalPresent(swap, sync, flags);
    }

    bool PatchVtable(void** slot, void* detour, void** original)
    {
        if (!slot || !detour || !original)
            return false;
        DWORD oldProt = 0;
        if (!VirtualProtect(slot, sizeof(void*), PAGE_EXECUTE_READWRITE, &oldProt))
            return false;
        *original = *slot;
        *slot = detour;
        VirtualProtect(slot, sizeof(void*), oldProt, &oldProt);
        return true;
    }
}

bool PresentHook_Install()
{
    if (g_Installed)
        return true;

    // Kiero-style: create a temporary D3D11 device+swapchain to resolve Present vtable,
    // then patch the shared vtable so Unity's swapchain Present is hooked too.
    DXGI_SWAP_CHAIN_DESC sd{};
    sd.BufferCount = 1;
    sd.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    sd.BufferDesc.Width = 2;
    sd.BufferDesc.Height = 2;
    sd.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    sd.OutputWindow = GetForegroundWindow();
    if (!sd.OutputWindow)
        sd.OutputWindow = GetDesktopWindow();
    sd.SampleDesc.Count = 1;
    sd.Windowed = TRUE;
    sd.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;

    ID3D11Device* device = nullptr;
    ID3D11DeviceContext* ctx = nullptr;
    IDXGISwapChain* swap = nullptr;
    D3D_FEATURE_LEVEL fl;
    HRESULT hr = D3D11CreateDeviceAndSwapChain(
        nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0,
        nullptr, 0, D3D11_SDK_VERSION, &sd,
        &swap, &device, &fl, &ctx);

    if (FAILED(hr) || !swap)
    {
        // Fallback: try null driver WARP
        hr = D3D11CreateDeviceAndSwapChain(
            nullptr, D3D_DRIVER_TYPE_WARP, nullptr, 0,
            nullptr, 0, D3D11_SDK_VERSION, &sd,
            &swap, &device, &fl, &ctx);
        if (FAILED(hr) || !swap)
            return false;
    }

    void** vtable = *reinterpret_cast<void***>(swap);
    // IDXGISwapChain::Present is vtable index 8
    g_PresentVtableSlot = &vtable[8];
    if (!PatchVtable(g_PresentVtableSlot, (void*)&HookedPresent, (void**)&g_OriginalPresent))
    {
        if (ctx) ctx->Release();
        if (device) device->Release();
        if (swap) swap->Release();
        return false;
    }

    g_HookedPresentAddr = &HookedPresent;
    g_Installed = true;

    if (ctx) ctx->Release();
    if (device) device->Release();
    if (swap) swap->Release();
    return true;
}

void PresentHook_Remove()
{
    if (!g_Installed)
        return;

    if (g_PresentVtableSlot && g_OriginalPresent)
    {
        void* ignored = nullptr;
        PatchVtable(g_PresentVtableSlot, (void*)g_OriginalPresent, &ignored);
    }

    PostFX_Release(g_Fx);
    g_Installed = false;
    g_Ready = false;
    g_OriginalPresent = nullptr;
    g_PresentVtableSlot = nullptr;
}

void PresentHook_SetParams(const RBLookParams& p)
{
    std::lock_guard<std::mutex> lock(g_LookMutex);
    g_Look = p;
}

RBLookParams PresentHook_GetParams()
{
    std::lock_guard<std::mutex> lock(g_LookMutex);
    return g_Look;
}

bool PresentHook_IsReady()
{
    return g_Ready.load();
}
