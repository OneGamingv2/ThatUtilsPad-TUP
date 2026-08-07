using UnityEngine;
using UnityEngine.Rendering;

namespace CameraMod.Camera
{
    public abstract class FromCameraHider : MonoBehaviour
    {
        public UnityEngine.Camera cam;

        public void Start()
        {
            cam = GetComponent<UnityEngine.Camera>();
        }

        public void OnEnable()
        {
            RenderPipelineManager.endCameraRendering += EndCameraRendering;
            RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
        }

        public void OnDisable()
        {
            RenderPipelineManager.endCameraRendering -= EndCameraRendering;
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
        }

        private void EndCameraRendering(ScriptableRenderContext context, UnityEngine.Camera camera)
        {
            if (cam == null)
                cam = GetComponent<UnityEngine.Camera>();
            if (cam == null || cam != camera)
                return;
            try { Show(); }
            catch (System.Exception) { }
        }

        private void BeginCameraRendering(ScriptableRenderContext context, UnityEngine.Camera camera)
        {
            if (cam == null)
                cam = GetComponent<UnityEngine.Camera>();
            if (cam == null || cam != camera)
                return;
            try { Hide(); }
            catch (System.Exception) { }
        }

        public abstract void Hide();
        public abstract void Show();
    }
}
