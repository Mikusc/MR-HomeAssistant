using UnityEngine;

namespace MRHomeAssistant.Runtime.App
{
    /// <summary>
    /// Holds scene-level references for the runtime root.
    /// This keeps AppBootstrap lightweight and prevents hard-coded scene lookups everywhere.
    /// </summary>
    public sealed class AppContext : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField]
        private GameObject cameraRig;

        [SerializeField]
        private GameObject interactionsRig;

        [SerializeField]
        private GameObject effectMeshRoot;

        public GameObject CameraRig => cameraRig;
        public GameObject InteractionsRig => interactionsRig;
        public GameObject EffectMeshRoot => effectMeshRoot;

        public bool HasCameraRig => cameraRig != null;
        public bool HasInteractionsRig => interactionsRig != null;
        public bool HasEffectMeshRoot => effectMeshRoot != null;
    }
}