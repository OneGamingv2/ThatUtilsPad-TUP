using UnityEngine;

namespace TUPshaders.Utilities
{
    /// <summary>
    /// RT pooling for bloom downsample chains — no per-frame allocations.
    /// </summary>
    public sealed class RenderTexturePool
    {
        private readonly RenderTexture[] _pool = new RenderTexture[16];
        private readonly int[] _w = new int[16];
        private readonly int[] _h = new int[16];
        private readonly RenderTextureFormat[] _fmt = new RenderTextureFormat[16];

        public RenderTexture Get(int index, int width, int height, RenderTextureFormat format = RenderTextureFormat.DefaultHDR)
        {
            index = Mathf.Clamp(index, 0, _pool.Length - 1);
            var rt = _pool[index];
            if (rt != null && _w[index] == width && _h[index] == height && _fmt[index] == format && rt.IsCreated())
                return rt;

            if (rt != null)
            {
                rt.Release();
                Object.Destroy(rt);
            }

            rt = new RenderTexture(width, height, 0, format)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = $"TUP_RT_{index}_{width}x{height}",
                hideFlags = HideFlags.HideAndDontSave,
                useMipMap = false,
                autoGenerateMips = false
            };
            rt.Create();
            _pool[index] = rt;
            _w[index] = width;
            _h[index] = height;
            _fmt[index] = format;
            return rt;
        }

        public void ReleaseAll()
        {
            for (int i = 0; i < _pool.Length; i++)
            {
                if (_pool[i] == null) continue;
                _pool[i].Release();
                Object.Destroy(_pool[i]);
                _pool[i] = null!;
            }
        }
    }
}
