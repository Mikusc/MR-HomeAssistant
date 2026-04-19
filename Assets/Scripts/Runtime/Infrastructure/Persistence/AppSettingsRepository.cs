using System.IO;
using MRHomeAssistant.Runtime.Application.Settings;
using UnityEngine;

namespace MRHomeAssistant.Runtime.Infrastructure.Persistence
{
    public sealed class AppSettingsRepository
    {
        private const string FileName = "app-settings.json";
        private readonly string filePath;

        public string FilePath => filePath;

        public AppSettingsRepository()
        {
            filePath = Path.Combine(UnityEngine.Application.persistentDataPath, FileName);
        }

        public bool Exists()
        {
            return File.Exists(filePath);
        }

        public AppSettingsData CreateDefault()
        {
            return new AppSettingsData();
        }

        public void Save(AppSettingsData data)
        {
            Directory.CreateDirectory(UnityEngine.Application.persistentDataPath);
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(filePath, json);
        }

        public bool TryLoad(out AppSettingsData data)
        {
            data = null;

            try
            {
                if (!File.Exists(filePath))
                {
                    return false;
                }

                string json = File.ReadAllText(filePath);

                if (string.IsNullOrWhiteSpace(json))
                {
                    return false;
                }

                AppSettingsData loaded = JsonUtility.FromJson<AppSettingsData>(json);

                if (loaded == null)
                {
                    return false;
                }

                data = loaded;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}