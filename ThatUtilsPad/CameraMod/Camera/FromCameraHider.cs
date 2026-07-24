using UnityEngine;
using UnityEngine.Rendering;

namespace CameraMod.Camera {
    public abstract class FromCameraHider : MonoBehaviour {
        public UnityEngine.Camera cam;

        public void Start() {
            cam = GetComponent<UnityEngine.Camera>();
        }

        public void OnEnable() {
            RenderPipelineManager.endCameraRendering += endCameraRendering;
            RenderPipelineManager.beginCameraRendering += beginCameraRendering;
        }

        public void OnDisable() {
            RenderPipelineManager.endCameraRendering -= endCameraRendering;
            RenderPipelineManager.beginCameraRendering -= beginCameraRendering;
        }

        private void endCameraRendering(ScriptableRenderContext context, UnityEngine.Camera camera) {
            if (cam == camera) Show();
        }

        private void beginCameraRendering(ScriptableRenderContext context, UnityEngine.Camera camera) {
            if (cam == camera) Hide();
        }

        public abstract void Hide();
        public abstract void Show();
    }
}