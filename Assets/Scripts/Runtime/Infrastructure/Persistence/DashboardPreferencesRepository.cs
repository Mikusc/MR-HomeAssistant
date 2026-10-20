using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MRHomeAssistant.Runtime.Infrastructure.Persistence
{
    [Serializable]
    public sealed class DashboardPreferences
    {
        public string server;
        public List<string> favorites = new();
        public List<string> recent = new();
        public bool IsFavorite(string id) => favorites.Contains(id);
        public void ToggleFavorite(string id) { if (!favorites.Remove(id)) favorites.Add(id); }
        public void RecordUse(string id) { recent.Remove(id); recent.Insert(0, id); if (recent.Count > 20) recent.RemoveAt(20); }
    }

    public sealed class DashboardPreferencesRepository
    {
        [Serializable] private sealed class Store { public List<DashboardPreferences> profiles = new(); }
        private readonly string path;
        public DashboardPreferencesRepository(string directory = null) =>
            path = Path.Combine(directory ?? UnityEngine.Application.persistentDataPath, "dashboard-preferences.json");

        public DashboardPreferences Load(string server)
        {
            var profile = Read().profiles.FirstOrDefault(p => p != null && p.server == server);
            if (profile == null) return new DashboardPreferences { server = server };
            profile.favorites = (profile.favorites ?? new()).Where(id => !string.IsNullOrEmpty(id)).Distinct().Take(500).ToList();
            profile.recent = (profile.recent ?? new()).Where(id => !string.IsNullOrEmpty(id)).Distinct().Take(20).ToList();
            return profile;
        }

        public void Save(DashboardPreferences profile)
        {
            if (profile == null || string.IsNullOrEmpty(profile.server) || profile.server == "demo") return;
            var store = Read();
            store.profiles.RemoveAll(p => p == null || p.server == profile.server);
            store.profiles.Add(profile);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(store, true));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        private Store Read()
        {
            try
            {
                var store = File.Exists(path) ? JsonUtility.FromJson<Store>(File.ReadAllText(path)) : new Store();
                return store?.profiles != null ? store : new Store();
            }
            catch { return new Store(); }
        }
    }
}
