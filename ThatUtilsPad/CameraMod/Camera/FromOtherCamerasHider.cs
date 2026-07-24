using UnityEngine;
using UnityEngine.Rendering;

namespace CameraMod.Camera {
    public abstract class FromOtherCamerasHider : MonoBehaviour {
        public UnityEngine.Camera cam;

        public void Start() {
            cam = GetComponent<UnityEngine.Camera>();
        }

        public void OnEnable() {
            RenderPipelineManager.beginFrameRendering += onBeginFrameRendering;
            RenderPipelineManager.beginCameraRendering += onBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += onEndCameraRendering;
            RenderPipelineManager.endFrameRendering += onEndFrameRendering;
        }

        public void OnDisable() {
            RenderPipelineManager.beginFrameRendering -= onBeginFrameRendering;
            RenderPipelineManager.beginCameraRendering -= onBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= onEndCameraRendering;
            RenderPipelineManager.endFrameRendering -= onEndFrameRendering;
        }

        private void onBeginFrameRendering(ScriptableRenderContext context, UnityEngine.Camera[] cameras) {
            Hide();
        }

        private void onBeginCameraRendering(ScriptableRenderContext context, UnityEngine.Camera camera) {
            if (cam == camera) Show();
        }

        private void onEndCameraRendering(ScriptableRenderContext context, UnityEngine.Camera camera) {
            if (cam == camera) Hide();
        }

        private void onEndFrameRendering(ScriptableRenderContext context, UnityEngine.Camera[] cameras) {
            Show();
        }

        public abstract void Hide();
        public abstract void Show();
    }
}