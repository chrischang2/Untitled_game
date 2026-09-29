using UnityEngine;

namespace UntitledGame.Environment
{
    /// <summary>Keeps a quad facing the camera (lantern glows).</summary>
    public class Billboard : MonoBehaviour
    {
        private Camera _cam;

        private void LateUpdate()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;
            transform.rotation = _cam.transform.rotation;
        }
    }
}
