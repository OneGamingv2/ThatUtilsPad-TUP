ThatUtilsPad.RenderBridge
========================

Native D3D11 Present-hook post processor (ReShade-class).

WHAT THIS IS
- Hooks IDXGISwapChain::Present (same injection class as ReShade overlays).
- Runs a fullscreen HLSL pass: contact-shadow AO (color), screen-space
  reflection cues (ray-march in UV), bloom lift, contrast, cool grade, vignette.
- Driven by TUPshaders settings (SSAO / SSR / Realistic / etc.).

WHAT THIS IS NOT
- NOT hardware DXR / RTX ray tracing.
- Gorilla Tag logs show D3D11. DXR requires D3D12. Impossible here.
- Public "realtime ray tracing" packs for URP (JoshuaLim SSR, Unity SSR preview,
  Lettier SSR tutorials) are screen-space ray marching — same family as this.

References (algorithms / public material):
- https://lettier.github.io/3d-game-shaders-for-beginners/screen-space-reflection.html
- https://github.com/JoshuaLim007/Unity-ScreenSpaceReflections-URP
- Unity Discussions: Screen Space Reflections for URP preview
- ReShade-style Present injection (conceptually; we do not ship ReShade code)

BUILD (Visual Studio 2022 + C++ workload)
-----------------------------------------
  msbuild ThatUtilsPad.RenderBridge\ThatUtilsPad.RenderBridge.vcxproj /p:Configuration=Release /p:Platform=x64

Output:
  ThatUtilsPad.RenderBridge\bin\Release\ThatUtilsPad.RenderBridge.dll

Copy next to ThatUtilsPad.dll:
  ...\BepInEx\plugins\ThatUtilsPad\ThatUtilsPad.RenderBridge.dll

Or run repo build.bat (builds C# + native and copies when possible).

LOG LINES TO LOOK FOR
- "RenderBridge active — D3D11 Present hook..."
- If missing DLL: "RenderBridge DLL not found..."

TOGGLE
- F GUI → Ray Look → Native D3D Present Bridge
