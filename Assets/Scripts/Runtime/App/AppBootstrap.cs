using MRHomeAssistant.Runtime.Application.Settings;
using MRHomeAssistant.Runtime.Infrastructure.Persistence;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MRHomeAssistant.Runtime.App
{
    /// <summary>
    /// Minimal application bootstrap entry point.
    /// It validates the core scene references and ensures app-settings.json exists.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AppBootstrap : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        private AppContext appContext;

        [Header("Debug")]
        [SerializeField]
        private bool logOnAwake = true;

        [SerializeField]
        private bool logOnStart = true;

        [SerializeField]
        private bool validateSceneReferencesOnStart = true;

        [Header("App Settings")]
        [SerializeField]
        private bool initializeAppSettingsOnStart = true;

        private AppSettingsRepository appSettingsRepository;

        private void Reset()
        {
            appContext = GetComponent<AppContext>();
        }

        private void Awake()
        {
            if (appContext == null)
            {
                appContext = GetComponent<AppContext>();
            }

            appSettingsRepository = new AppSettingsRepository();

            if (logOnAwake)
            {
                Debug.Log(
                    $"[AppBootstrap] Awake | Scene={SceneManager.GetActiveScene().name} | " +
                    $"Product={UnityEngine.Application.productName} | Version={UnityEngine.Application.version} | " +
                    $"PersistentDataPath={UnityEngine.Application.persistentDataPath}",
                    this);
            }
        }

        private void Start()
        {
            if (logOnStart)
            {
                Debug.Log("[AppBootstrap] Start | Minimal bootstrap is alive.", this);
            }

            if (validateSceneReferencesOnStart)
            {
                ValidateSceneReferences();
            }

            if (initializeAppSettingsOnStart)
            {
                EnsureAppSettingsFile();
            }
        }

        private void ValidateSceneReferences()
        {
            if (appContext == null)
            {
                Debug.LogError("[AppBootstrap] Validation failed: AppContext is missing.", this);
                return;
            }

            bool hasAnyMissingReference = false;

            if (appContext.HasCameraRig)
            {
                Debug.Log($"[AppBootstrap] OK | Camera Rig = {appContext.CameraRig.name}", this);
            }
            else
            {
                hasAnyMissingReference = true;
                Debug.LogWarning("[AppBootstrap] Missing reference: Camera Rig", this);
            }

            if (appContext.HasInteractionsRig)
            {
                Debug.Log($"[AppBootstrap] OK | Interactions Rig = {appContext.InteractionsRig.name}", this);
            }
            else
            {
                hasAnyMissingReference = true;
                Debug.LogWarning("[AppBootstrap] Missing reference: Interactions Rig", this);
            }

            if (appContext.HasEffectMeshRoot)
            {
                Debug.Log($"[AppBootstrap] OK | Effect Mesh Root = {appContext.EffectMeshRoot.name}", this);
            }
            else
            {
                hasAnyMissingReference = true;
                Debug.LogWarning("[AppBootstrap] Missing reference: Effect Mesh Root", this);
            }

            if (!hasAnyMissingReference)
            {
                Debug.Log("[AppBootstrap] Validation complete | Core scene references look valid.", this);
            }
        }

        private void EnsureAppSettingsFile()
        {
            if (!appSettingsRepository.Exists())
            {
                AppSettingsData defaultSettings = appSettingsRepository.CreateDefault();
                appSettingsRepository.Save(defaultSettings);

                Debug.Log(
                    $"[AppBootstrap] Created default app settings | Path={appSettingsRepository.FilePath}",
                    this);

                return;
            }

            if (!appSettingsRepository.TryLoad(out AppSettingsData loadedSettings))
            {
                AppSettingsData defaultSettings = appSettingsRepository.CreateDefault();
                appSettingsRepository.Save(defaultSettings);

                Debug.LogWarning(
                    $"[AppBootstrap] app-settings.json invalid, recreated default file | Path={appSettingsRepository.FilePath}",
                    this);

                return;
            }

            Debug.Log(
                $"[AppBootstrap] App settings loaded | Path={appSettingsRepository.FilePath} | " +
                $"UrlSet={!string.IsNullOrWhiteSpace(loadedSettings.homeAssistantUrl)} | " +
                $"TokenSet={!string.IsNullOrWhiteSpace(loadedSettings.homeAssistantToken)} | " +
                $"OnboardingCompleted={loadedSettings.onboardingCompleted}",
                this);
        }

    }
}
