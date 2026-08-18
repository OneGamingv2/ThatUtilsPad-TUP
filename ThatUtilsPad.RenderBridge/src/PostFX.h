#pragma once
#include <d3d11.h>
#include "PresentHook.h"

struct PostFXContext
{
    ID3D11Device* device = nullptr;
    ID3D11DeviceContext* context = nullptr;
    ID3D11VertexShader* vs = nullptr;
    ID3D11PixelShader* ps = nullptr;
    ID3D11SamplerState* samp = nullptr;
    ID3D11Buffer* cb = nullptr;
    ID3D11BlendState* blend = nullptr;
    ID3D11DepthStencilState* depthOff = nullptr;
    ID3D11RasterizerState* raster = nullptr;
    ID3D11Texture2D* stagingCopy = nullptr;
    ID3D11ShaderResourceView* stagingSrv = nullptr;
    ID3D11RenderTargetView* bbRtv = nullptr;
    ID3D11Texture2D* bbTex = nullptr;
    UINT width = 0;
    UINT height = 0;
    bool ready = false;
};

bool PostFX_Ensure(ID3D11Device* device, IDXGISwapChain* swap, PostFXContext& fx);
void PostFX_Release(PostFXContext& fx);
void PostFX_Apply(IDXGISwapChain* swap, PostFXContext& fx, const RBLookParams& look);
