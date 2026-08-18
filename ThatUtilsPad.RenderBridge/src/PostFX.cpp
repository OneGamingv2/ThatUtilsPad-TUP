#include "PostFX.h"
#include <d3dcompiler.h>
#include <Windows.h>
#include <cstring>

#pragma comment(lib, "d3dcompiler.lib")

namespace
{
    // Clean camera-grade Present post. Soft AO/SSR/fog/bloom — no pixel sparkle.
    // Grass/detail/bump are gentle image edits. NOT DXR / not new meshes.
    static const char* kHLSL = R"(
cbuffer LookCB : register(b0)
{
    float4 Params0; // master, ssao, ssr, contrast
    float4 Params1; // tint (pos=cool / neg=magenta), vignette, bloom, time
    float4 Params2; // invW, invH, fog, godrays
    float4 Params3; // grain, sharpen, saturation, ca
    float4 Params4; // heightFog, fogR, fogG, fogB
    float4 Params5; // grass, bump, detail, denoise
    float4 Params6; // quality, pad, pad, pad
};

Texture2D SceneTex : register(t0);
SamplerState LinearSamp : register(s0);

struct VSOut
{
    float4 pos : SV_Position;
    float2 uv  : TEXCOORD0;
};

VSOut VSMain(uint id : SV_VertexID)
{
    VSOut o;
    // Fullscreen triangle: corner UVs must reach 2.0 (do not saturate).
    float2 uv = float2((id << 1) & 2, id & 2);
    o.uv = uv;
    o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return o;
}

float Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

float Hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float3 SampleC(float2 uv)
{
    return SceneTex.Sample(LinearSamp, saturate(uv)).rgb;
}

float3 SoftSample(float2 uv, float2 texel)
{
    // 5-tap soft read — kills hard pixel edges from cheap AO/CA samples
    float3 c = SampleC(uv) * 0.4;
    c += SampleC(uv + float2( texel.x, 0)) * 0.15;
    c += SampleC(uv + float2(-texel.x, 0)) * 0.15;
    c += SampleC(uv + float2(0,  texel.y)) * 0.15;
    c += SampleC(uv + float2(0, -texel.y)) * 0.15;
    return c;
}

float DepthProxy(float3 col, float2 uv, float2 texel)
{
    float sky = saturate((col.b - col.r) * 2.0 + Luma(col) * 0.3 - 0.12);
    float open = saturate(Luma(col) - 0.52) * 0.45;
    float edge = 0;
    [unroll] for (int i = 0; i < 4; i++)
    {
        float2 o = float2((i % 2) * 2 - 1, (i / 2) * 2 - 1) * texel * 3.0;
        edge += saturate(Luma(col) - Luma(SampleC(uv + o)));
    }
    return saturate(sky + open - saturate(edge * 1.1) * 0.45);
}

float GrassMask(float3 c, float2 uv)
{
    float green = saturate(c.g - max(c.r, c.b) * 0.9);
    green *= saturate(1.1 - abs(c.g - 0.4) * 2.0);
    float ground = saturate(1.0 - uv.y * 0.8);
    float notSky = 1.0 - saturate((c.b - c.r) * 2.2 + Luma(c) * 0.18 - 0.3);
    return saturate(green * 2.0) * lerp(0.4, 1.0, ground) * notSky;
}

float4 PSMain(VSOut i) : SV_Target
{
    float master = saturate(Params0.x);
    float ssao = Params0.y;
    float ssr = Params0.z;
    float contrast = max(Params0.w, 0.01);
    float tint = Params1.x; // +cool / -magenta
    float vignette = Params1.y;
    float bloom = Params1.z;
    float time = Params1.w;
    float2 texel = Params2.xy;
    float fog = Params2.z;
    float godrays = Params2.w;
    float grain = Params3.x;
    float sharpen = Params3.y;
    float saturation = Params3.z;
    float ca = Params3.w;
    float heightFog = Params4.x;
    float3 fogCol = Params4.yzw;
    float grass = Params5.x;
    float bump = Params5.y;
    float detail = Params5.z;
    float denoise = Params5.w;
    float quality = Params6.x;

    int stepsSSR = quality >= 1.5 ? 14 : (quality >= 0.5 ? 10 : 6);
    int stepsRay = quality >= 1.5 ? 20 : (quality >= 0.5 ? 14 : 8);

    float2 uv = saturate(i.uv);
    float2 vc0 = uv * 2.0 - 1.0;

    // Soft CA — tiny radial split, never crunchy fringe soup
    float caAmt = saturate(ca) * 0.0035 * master;
    float3 col;
    if (caAmt > 0.00005)
    {
        float2 cad = vc0 * caAmt;
        col.r = SampleC(uv + cad).r;
        col.g = SampleC(uv).g;
        col.b = SampleC(uv - cad).b;
    }
    else
        col = SampleC(uv);

    if (master < 0.01)
        return float4(col, 1);

    // Gentle denoise first so later passes don't amplify dirt
    if (denoise > 0.01)
    {
        float3 soft = SoftSample(uv, texel);
        col = lerp(col, soft, saturate(denoise) * 0.55 * master);
    }

    float depth = DepthProxy(col, uv, texel);
    float gMask = GrassMask(col, uv);

    // Soft foliage revive (no sparkle noise)
    if (grass > 0.01 && gMask > 0.03)
    {
        float3 lush = col * float3(0.92, 1.10, 0.88);
        lush = lerp(lush, float3(0.26, 0.46, 0.22), 0.08 * gMask);
        col = lerp(col, lush, saturate(grass) * gMask * 0.85 * master);
    }

    // Soft cavity darkening (contact / underside)
    if (bump > 0.01)
    {
        float above = Luma(SampleC(uv + float2(0, -texel.y * 3.0)));
        float below = Luma(SampleC(uv + float2(0,  texel.y * 3.0)));
        float left  = Luma(SampleC(uv + float2(-texel.x * 3.0, 0)));
        float right = Luma(SampleC(uv + float2( texel.x * 3.0, 0)));
        float center = Luma(col);
        float cavity = saturate((above + below + left + right) * 0.25 - center);
        float under = saturate(above - center);
        float amt = saturate(cavity * 0.9 + under * 1.1) * (1.0 - depth * 0.5);
        col *= 1.0 - amt * bump * 0.32 * master;
    }

    // Very soft local contrast (NOT hash sparkle)
    if (detail > 0.01)
    {
        float3 blur = SoftSample(uv, texel * 2.0);
        float3 hi = col - blur;
        col += hi * detail * 0.22 * master * (1.0 - depth * 0.4);
    }

    // Soft contact AO
    if (ssao > 0.01)
    {
        float center = Luma(col);
        float ao = 0;
        float wsum = 0;
        int rad = quality >= 1.5 ? 2 : 1;
        [unroll] for (int y = -2; y <= 2; y++)
        [unroll] for (int x = -2; x <= 2; x++)
        {
            if (abs(x) > rad || abs(y) > rad) continue;
            if (x == 0 && y == 0) continue;
            float dist = length(float2(x, y));
            float n = Luma(SampleC(uv + float2(x, y) * texel * 2.4));
            float w = 1.0 / (0.6 + dist);
            ao += saturate(center - n) * w;
            wsum += w;
        }
        ao = saturate((ao / max(wsum, 0.001)) * 1.35);
        // Smooth AO so wood grain doesn't pixelate
        col *= 1.0 - ao * ssao * 0.42 * master * (1.0 - depth * 0.4);
    }

    // Soft floor/wet SSR cue
    if (ssr > 0.01)
    {
        float3 refl = 0;
        float wsum = 0;
        float2 p = uv;
        for (int s = 1; s <= 14; s++)
        {
            if (s > stepsSSR) break;
            p = saturate(p + float2(0.0, -1.0) * texel * (1.8 + s * 1.1));
            float3 sampleC = SoftSample(p, texel);
            float w = 1.0 / (1.0 + s * 0.32);
            refl += sampleC * w;
            wsum += w;
        }
        if (wsum > 0.001)
        {
            refl /= wsum;
            float floorGate = saturate(1.15 - uv.y * 1.25);
            float wet = floorGate * (0.35 + depth * 0.3);
            col = lerp(col, refl, saturate(ssr) * wet * 0.35 * master);
        }
    }

    // Soft bloom
    if (bloom > 0.01)
    {
        float3 bright = 0;
        int bk = quality >= 0.5 ? 8 : 4;
        [unroll] for (int k = 0; k < 8; k++)
        {
            if (k >= bk) break;
            float2 o = float2((k % 4) - 1.5, (k / 4) - 0.5) * texel * 7.0;
            bright += max(SoftSample(uv + o, texel) - 0.62, 0);
        }
        col += bright * (0.09 * bloom * master);
    }

    // Soft golden volumetric sun shafts — stable anchors (no per-frame hunt flicker)
    if (godrays > 0.01)
    {
        // Only a few fixed sky anchors. Searching a moving grid every frame made
        // shafts jump when walking through light / dark patches.
        float2 a0 = float2(0.74, 0.08);
        float2 a1 = float2(0.55, 0.05);
        float2 a2 = float2(0.88, 0.12);
        float2 a3 = float2(0.38, 0.07);
        float2 sunUV = a0;
        float best = -1.0;
        float2 anchors[4] = { a0, a1, a2, a3 };
        [unroll] for (int si = 0; si < 4; si++)
        {
            float3 s = SoftSample(anchors[si], texel * 2.0);
            float l = Luma(s);
            float warm = saturate(s.r * 1.1 + s.g * 0.4 - s.b * 0.8);
            float sky = saturate((s.b - s.r) * 0.8 + l * 0.3);
            float score = l * 0.4 + warm * 1.0 + sky * 0.25;
            if (score > best) { best = score; sunUV = anchors[si]; }
        }

        float2 dir = sunUV - uv;
        float dist = length(dir);
        float2 stepDir = dir / max(dist, 0.001);
        float3 rays = 0;
        float weight = 0;
        float2 rp = uv;
        float decay = 1.0;
        for (int r = 0; r < 20; r++)
        {
            if (r >= stepsRay) break;
            rp = saturate(rp + stepDir * 0.024);
            float3 s = SoftSample(rp, texel);
            float l = Luma(s);
            // Gentle openness — sudden dark→bright underfoot no longer spikes shafts
            float open = smoothstep(0.12, 0.55, l);
            float warm = saturate(s.r * 0.7 + s.g * 0.28 - s.b * 0.22);
            float3 gold = lerp(s, float3(1.02, 0.82, 0.5), 0.35 + warm * 0.3);
            float w = open * decay;
            rays += gold * w;
            weight += w;
            decay *= 0.91;
        }
        rays /= max(weight, 0.25);
        float shaft = pow(saturate(1.06 - dist * 0.48), 1.3);
        float amt = saturate(godrays) * shaft * 0.42 * master * (0.28 + depth * 0.5);
        col += rays * amt;
        col += float3(1.0, 0.86, 0.58) * amt * 0.12 * saturate(best);
    }

    // Aerial / height fog
    float fogAmt = saturate(depth * fog * 1.05);
    fogAmt = saturate(fogAmt + saturate(1.0 - uv.y) * heightFog * (0.45 + fog * 0.35));
    fogAmt *= master * 0.72;
    col = lerp(col, fogCol, fogAmt);

    // Warm air lift when haze itself is golden (sunset / realistic)
    float warmAir = saturate((fogCol.r - fogCol.b) * 1.8) * saturate(fog + godrays * 0.35);
    col = lerp(col, col * float3(1.07, 1.02, 0.93), warmAir * 0.28 * master);

    // Mild sharpen (CAS-like, capped)
    if (sharpen > 0.01)
    {
        float3 blur = SoftSample(uv, texel);
        float3 sharpenCol = col + (col - blur) * 0.85;
        col = lerp(col, sharpenCol, saturate(sharpen) * 0.55 * master);
    }

    // Soft contrast around midtones (less crushing than hard multiply)
    float cAmt = lerp(1.0, contrast, master * 0.85);
    col = saturate((col - 0.5) * cAmt + 0.5);

    float gray = Luma(col);
    col = lerp(gray.xxx, col, saturation);

    // Tint: +cool blue / -magenta neon
    if (tint > 0.001)
        col = lerp(col, col * float3(0.93, 0.97, 1.07), saturate(tint) * 0.4 * master);
    else if (tint < -0.001)
        col = lerp(col, col * float3(1.12, 0.82, 1.18), saturate(-tint) * 0.55 * master);

    // Soft vignette
    float2 vc = uv * 2.0 - 1.0;
    float vig = saturate(1.0 - dot(vc, vc) * 0.42);
    col *= lerp(1.0, vig, saturate(vignette) * 0.85 * master);

    // Film grain — very soft, luma-linked, never blocky hash cubes
    if (grain > 0.001)
    {
        float n = Hash21(uv * float2(900, 500) + float2(time * 0.7, time * 0.3));
        float g = (n - 0.5) * grain * 0.025 * master;
        col += g * (0.55 + Luma(col) * 0.45);
    }

    col = saturate(col);
    return float4(col, 1);
}
)";

#pragma pack(push, 1)
    struct LookCB
    {
        float master, ssao, ssr, contrast;
        float cool, vignette, bloom, time;
        float invW, invH, fog, godrays;
        float grain, sharpen, saturation, ca;
        float heightFog, fogR, fogG, fogB;
        float grass, bump, detail, denoise;
        float quality, pad0, pad1, pad2;
    };
#pragma pack(pop)

    struct SavedState
    {
        ID3D11RenderTargetView* rtv = nullptr;
        ID3D11DepthStencilView* dsv = nullptr;
        ID3D11VertexShader* vs = nullptr;
        ID3D11PixelShader* ps = nullptr;
        ID3D11InputLayout* layout = nullptr;
        ID3D11Buffer* vb = nullptr;
        UINT vbStride = 0, vbOffset = 0;
        ID3D11Buffer* ib = nullptr;
        DXGI_FORMAT ibFormat = DXGI_FORMAT_UNKNOWN;
        UINT ibOffset = 0;
        D3D11_PRIMITIVE_TOPOLOGY topo = D3D11_PRIMITIVE_TOPOLOGY_UNDEFINED;
        ID3D11ShaderResourceView* psSrv = nullptr;
        ID3D11SamplerState* psSamp = nullptr;
        ID3D11Buffer* psCb = nullptr;
        ID3D11RasterizerState* rs = nullptr;
        ID3D11DepthStencilState* dss = nullptr;
        UINT stencilRef = 0;
        ID3D11BlendState* blend = nullptr;
        float blendFactor[4]{};
        UINT sampleMask = 0;
        D3D11_VIEWPORT vp{};
        UINT numVp = 0;
    };
}

bool PostFX_Ensure(ID3D11Device* device, IDXGISwapChain* swap, PostFXContext& fx)
{
    if (!device || !swap)
        return false;

    // Prefer real backbuffer size — DXGI GetDesc Width/Height can be 0 on flip model
    ID3D11Texture2D* sizeProbe = nullptr;
    UINT w = 0, h = 0;
    if (SUCCEEDED(swap->GetBuffer(0, __uuidof(ID3D11Texture2D), (void**)&sizeProbe)) && sizeProbe)
    {
        D3D11_TEXTURE2D_DESC td{};
        sizeProbe->GetDesc(&td);
        w = td.Width;
        h = td.Height;
        sizeProbe->Release();
    }
    if (w < 8 || h < 8)
    {
        DXGI_SWAP_CHAIN_DESC desc{};
        if (FAILED(swap->GetDesc(&desc)))
            return false;
        w = desc.BufferDesc.Width;
        h = desc.BufferDesc.Height;
    }
    if (w < 8 || h < 8)
        return false;

    if (fx.ready && fx.device == device && fx.width == w && fx.height == h && fx.bbRtv)
        return true;

    PostFX_Release(fx);
    fx.device = device;
    device->GetImmediateContext(&fx.context);
    fx.width = w;
    fx.height = h;

    ID3DBlob* vsBlob = nullptr;
    ID3DBlob* psBlob = nullptr;
    ID3DBlob* err = nullptr;
    HRESULT hr = D3DCompile(kHLSL, strlen(kHLSL), "RBPost", nullptr, nullptr, "VSMain", "vs_5_0",
                            D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, &vsBlob, &err);
    if (FAILED(hr))
    {
        if (err) err->Release();
        return false;
    }
    hr = D3DCompile(kHLSL, strlen(kHLSL), "RBPost", nullptr, nullptr, "PSMain", "ps_5_0",
                    D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, &psBlob, &err);
    if (FAILED(hr))
    {
        if (err) err->Release();
        vsBlob->Release();
        return false;
    }

    if (FAILED(device->CreateVertexShader(vsBlob->GetBufferPointer(), vsBlob->GetBufferSize(), nullptr, &fx.vs)) ||
        FAILED(device->CreatePixelShader(psBlob->GetBufferPointer(), psBlob->GetBufferSize(), nullptr, &fx.ps)))
    {
        vsBlob->Release();
        psBlob->Release();
        PostFX_Release(fx);
        return false;
    }
    vsBlob->Release();
    psBlob->Release();

    D3D11_SAMPLER_DESC sd{};
    sd.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
    sd.AddressU = sd.AddressV = sd.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
    sd.ComparisonFunc = D3D11_COMPARISON_NEVER;
    sd.MaxLOD = D3D11_FLOAT32_MAX;
    device->CreateSamplerState(&sd, &fx.samp);

    D3D11_BUFFER_DESC cbd{};
    cbd.ByteWidth = (sizeof(LookCB) + 15u) & ~15u;
    cbd.Usage = D3D11_USAGE_DYNAMIC;
    cbd.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    cbd.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    device->CreateBuffer(&cbd, nullptr, &fx.cb);

    // Opaque replace blend — no partial alpha holes
    D3D11_BLEND_DESC bd{};
    bd.RenderTarget[0].RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALL;
    device->CreateBlendState(&bd, &fx.blend);

    D3D11_DEPTH_STENCIL_DESC dd{};
    dd.DepthEnable = FALSE;
    dd.DepthWriteMask = D3D11_DEPTH_WRITE_MASK_ZERO;
    device->CreateDepthStencilState(&dd, &fx.depthOff);

    D3D11_RASTERIZER_DESC rd{};
    rd.FillMode = D3D11_FILL_SOLID;
    rd.CullMode = D3D11_CULL_NONE;
    rd.DepthClipEnable = TRUE;
    device->CreateRasterizerState(&rd, &fx.raster);

    ID3D11Texture2D* back = nullptr;
    if (FAILED(swap->GetBuffer(0, __uuidof(ID3D11Texture2D), (void**)&back)) || !back)
    {
        PostFX_Release(fx);
        return false;
    }
    fx.bbTex = back;
    if (FAILED(device->CreateRenderTargetView(back, nullptr, &fx.bbRtv)))
    {
        PostFX_Release(fx);
        return false;
    }

    D3D11_TEXTURE2D_DESC td{};
    back->GetDesc(&td);
    td.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    td.MiscFlags = 0;
    td.CPUAccessFlags = 0;
    td.Usage = D3D11_USAGE_DEFAULT;
    if (FAILED(device->CreateTexture2D(&td, nullptr, &fx.stagingCopy)))
    {
        PostFX_Release(fx);
        return false;
    }
    device->CreateShaderResourceView(fx.stagingCopy, nullptr, &fx.stagingSrv);

    fx.ready = true;
    return true;
}

void PostFX_Release(PostFXContext& fx)
{
    if (fx.stagingSrv) { fx.stagingSrv->Release(); fx.stagingSrv = nullptr; }
    if (fx.stagingCopy) { fx.stagingCopy->Release(); fx.stagingCopy = nullptr; }
    if (fx.bbRtv) { fx.bbRtv->Release(); fx.bbRtv = nullptr; }
    if (fx.bbTex) { fx.bbTex->Release(); fx.bbTex = nullptr; }
    if (fx.cb) { fx.cb->Release(); fx.cb = nullptr; }
    if (fx.samp) { fx.samp->Release(); fx.samp = nullptr; }
    if (fx.ps) { fx.ps->Release(); fx.ps = nullptr; }
    if (fx.vs) { fx.vs->Release(); fx.vs = nullptr; }
    if (fx.blend) { fx.blend->Release(); fx.blend = nullptr; }
    if (fx.depthOff) { fx.depthOff->Release(); fx.depthOff = nullptr; }
    if (fx.raster) { fx.raster->Release(); fx.raster = nullptr; }
    if (fx.context) { fx.context->Release(); fx.context = nullptr; }
    fx.device = nullptr;
    fx.width = fx.height = 0;
    fx.ready = false;
}

void PostFX_Apply(IDXGISwapChain* swap, PostFXContext& fx, const RBLookParams& look)
{
    if (!swap || !fx.ready || !fx.context || look.master < 0.01f)
        return;

    ID3D11Texture2D* back = nullptr;
    if (FAILED(swap->GetBuffer(0, __uuidof(ID3D11Texture2D), (void**)&back)) || !back)
        return;

    D3D11_TEXTURE2D_DESC bd{};
    back->GetDesc(&bd);
    if (bd.Width != fx.width || bd.Height != fx.height)
    {
        back->Release();
        ID3D11Device* dev = nullptr;
        swap->GetDevice(__uuidof(ID3D11Device), (void**)&dev);
        if (dev)
        {
            PostFX_Ensure(dev, swap, fx);
            dev->Release();
        }
        return;
    }

    fx.context->CopyResource(fx.stagingCopy, back);

    D3D11_MAPPED_SUBRESOURCE map{};
    if (SUCCEEDED(fx.context->Map(fx.cb, 0, D3D11_MAP_WRITE_DISCARD, 0, &map)))
    {
        LookCB* cb = (LookCB*)map.pData;
        memset(cb, 0, sizeof(LookCB));
        cb->master = look.master;
        cb->ssao = look.ssao;
        cb->ssr = look.ssr;
        cb->contrast = look.contrast;
        cb->cool = look.coolTint;
        cb->vignette = look.vignette;
        cb->bloom = look.bloom;
        cb->time = (float)(GetTickCount64() % 100000) * 0.001f;
        cb->invW = 1.f / (float)fx.width;
        cb->invH = 1.f / (float)fx.height;
        cb->fog = look.fog;
        cb->godrays = look.godrays;
        cb->grain = look.grain;
        cb->sharpen = look.sharpen;
        cb->saturation = look.saturation;
        cb->ca = look.ca;
        cb->heightFog = look.heightFog;
        cb->fogR = look.fogR;
        cb->fogG = look.fogG;
        cb->fogB = look.fogB;
        cb->grass = look.grass;
        cb->bump = look.bump;
        cb->detail = look.detail;
        cb->denoise = look.denoise;
        cb->quality = look.quality;
        fx.context->Unmap(fx.cb, 0);
    }

    // Full pipeline save — mirrors ReShade-style injector hygiene (stops lost/pink pixels)
    SavedState s{};
    fx.context->OMGetRenderTargets(1, &s.rtv, &s.dsv);
    fx.context->VSGetShader(&s.vs, nullptr, nullptr);
    fx.context->PSGetShader(&s.ps, nullptr, nullptr);
    fx.context->IAGetInputLayout(&s.layout);
    fx.context->IAGetVertexBuffers(0, 1, &s.vb, &s.vbStride, &s.vbOffset);
    fx.context->IAGetIndexBuffer(&s.ib, &s.ibFormat, &s.ibOffset);
    fx.context->IAGetPrimitiveTopology(&s.topo);
    fx.context->PSGetShaderResources(0, 1, &s.psSrv);
    fx.context->PSGetSamplers(0, 1, &s.psSamp);
    fx.context->PSGetConstantBuffers(0, 1, &s.psCb);
    fx.context->RSGetState(&s.rs);
    fx.context->OMGetDepthStencilState(&s.dss, &s.stencilRef);
    fx.context->OMGetBlendState(&s.blend, s.blendFactor, &s.sampleMask);
    s.numVp = 1;
    fx.context->RSGetViewports(&s.numVp, &s.vp);

    D3D11_VIEWPORT vp{};
    vp.Width = (float)fx.width;
    vp.Height = (float)fx.height;
    vp.MaxDepth = 1.f;
    float bf[4] = { 0, 0, 0, 0 };
    fx.context->RSSetViewports(1, &vp);
    fx.context->OMSetRenderTargets(1, &fx.bbRtv, nullptr);
    fx.context->OMSetBlendState(fx.blend, bf, 0xffffffff);
    fx.context->OMSetDepthStencilState(fx.depthOff, 0);
    fx.context->RSSetState(fx.raster);
    fx.context->IASetInputLayout(nullptr);
    fx.context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    fx.context->VSSetShader(fx.vs, nullptr, 0);
    fx.context->PSSetShader(fx.ps, nullptr, 0);
    fx.context->PSSetShaderResources(0, 1, &fx.stagingSrv);
    fx.context->PSSetSamplers(0, 1, &fx.samp);
    fx.context->PSSetConstantBuffers(0, 1, &fx.cb);
    fx.context->Draw(3, 0);

    ID3D11ShaderResourceView* nullSrv = nullptr;
    fx.context->PSSetShaderResources(0, 1, &nullSrv);

    // Restore
    fx.context->OMSetRenderTargets(1, &s.rtv, s.dsv);
    fx.context->VSSetShader(s.vs, nullptr, 0);
    fx.context->PSSetShader(s.ps, nullptr, 0);
    fx.context->IASetInputLayout(s.layout);
    fx.context->IASetVertexBuffers(0, 1, &s.vb, &s.vbStride, &s.vbOffset);
    fx.context->IASetIndexBuffer(s.ib, s.ibFormat, s.ibOffset);
    fx.context->IASetPrimitiveTopology(s.topo);
    fx.context->PSSetShaderResources(0, 1, &s.psSrv);
    fx.context->PSSetSamplers(0, 1, &s.psSamp);
    fx.context->PSSetConstantBuffers(0, 1, &s.psCb);
    fx.context->RSSetState(s.rs);
    fx.context->OMSetDepthStencilState(s.dss, s.stencilRef);
    fx.context->OMSetBlendState(s.blend, s.blendFactor, s.sampleMask);
    if (s.numVp > 0)
        fx.context->RSSetViewports(s.numVp, &s.vp);

    auto rel = [](IUnknown* p) { if (p) p->Release(); };
    rel(s.rtv); rel(s.dsv); rel(s.vs); rel(s.ps); rel(s.layout);
    rel(s.vb); rel(s.ib); rel(s.psSrv); rel(s.psSamp); rel(s.psCb);
    rel(s.rs); rel(s.dss); rel(s.blend);
    back->Release();
}
