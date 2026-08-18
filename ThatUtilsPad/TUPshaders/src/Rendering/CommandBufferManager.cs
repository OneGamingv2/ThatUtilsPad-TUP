using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace TUPshaders.Rendering
{
    /// <summary>
    /// Thin wrapper around CommandBuffer usage for effects that need multi-pass GPU work
    /// without allocating new buffers each frame.
    /// </summary>
    public sealed class CommandBufferManager : IDisposable
    {
        private CommandBuffer? _buffer;
        private Camera? _camera;
        private CameraEvent _evt = CameraEvent.AfterImageEffects;

        public CommandBuffer Buffer => _buffer ??= new CommandBuffer { name = "TUPshaders" };

        public void Attach(Camera camera, CameraEvent evt = CameraEvent.AfterImageEffects)
        {
            if (_camera == camera && _evt == evt && _buffer != null) return;
            Detach();
            _camera = camera;
            _evt = evt;
            camera.AddCommandBuffer(evt, Buffer);
        }

        public void Detach()
        {
            if (_camera != null && _buffer != null)
            {
                try { _camera.RemoveCommandBuffer(_evt, _buffer); }
                catch { /* camera destroyed */ }
            }
            _camera = null;
        }

        public void Clear() => _buffer?.Clear();

        public void Dispose()
        {
            Detach();
            _buffer?.Release();
            _buffer = null;
        }
    }
}
