using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace MRHomeAssistant.Runtime.Presentation.UI
{
    internal static class DashboardFonts
    {
        private static TMP_FontAsset fallback;

        public static void EnsureFallback()
        {
            if (fallback != null) return;
            Font source = Resources.Load<Font>("Fonts/NotoSansCJKsc-Regular");
            if (source == null) return;
            fallback = TMP_FontAsset.CreateFontAsset(source, 48, 5, GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, true);
            fallback.name = "Dashboard CJK fallback";
            fallback.hideFlags = HideFlags.DontSave;
            TMP_Settings.fallbackFontAssets ??= new List<TMP_FontAsset>();
            TMP_Settings.fallbackFontAssets.Add(fallback);
        }
    }
}
