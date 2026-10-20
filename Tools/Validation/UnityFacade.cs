// Test adapter only. Production sources are compiled unchanged by the offline protocol harness.
using System;
using System.IO;
using System.Text.Json;

namespace UnityEngine
{
    public sealed class DisallowMultipleComponent : Attribute { }
    public sealed class SerializeField : Attribute { }
    public sealed class ContextMenu : Attribute { public ContextMenu(string value) { } }
    public class MonoBehaviour { }
    public static class Application
    {
        public static string persistentDataPath = Path.Combine(Path.GetTempPath(), "mrha-check-" + Guid.NewGuid().ToString("N"));
    }
    public static class JsonUtility
    {
        private static JsonSerializerOptions Options(bool pretty = false) => new() { IncludeFields = true, WriteIndented = pretty };
        public static string ToJson(object value, bool pretty = false) => JsonSerializer.Serialize(value, value.GetType(), Options(pretty));
        public static T FromJson<T>(string json) => JsonSerializer.Deserialize<T>(json, Options());
    }
}
