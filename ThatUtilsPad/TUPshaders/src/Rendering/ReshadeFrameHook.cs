using System;
using TUPshaders.Settings;
using UnityEngine;

namespace TUPshaders.Rendering
{
    /// <summary>
    /// Legacy shell — real work moved to <see cref="RenderAgent"/>.
    /// Kept so older references compile without double-hooking cameras.
    /// </summary>
    public sealed class ReshadeFrameHook : IDisposable
    {
        public ReshadeFrameHook(SettingsManager settings) { }

        public void EnsureSubscribed()
        {
            // No-op. Use RenderAgent.
        }

        public void Dispose() { }
    }
}
