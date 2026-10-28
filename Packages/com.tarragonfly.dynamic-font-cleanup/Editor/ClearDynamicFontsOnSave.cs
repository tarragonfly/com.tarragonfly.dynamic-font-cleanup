using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using StackTrace = System.Diagnostics.StackTrace;
using TextCoreAtlasPopulationMode = UnityEngine.TextCore.Text.AtlasPopulationMode;
using TextCoreFontAsset = UnityEngine.TextCore.Text.FontAsset;

namespace TarragonFly.DynamicFontCleanup
{
    // Dynamic fonts keep their generated glyphs and atlas in the same .asset file as their settings,
    // so every play session leaves a big diff. This wipes that data right before Unity writes the file,
    // for TMP fonts (uGUI and world text) and TextCore fonts (UI Toolkit), going by the same
    // Clear Dynamic Data On Build checkbox Unity's own build and quit cleanup use.
    //
    // Unlike Unity's cleanup this also empties the kerning tables, since play sessions add kerning pairs
    // to them too. Hand-tuned kerning needs that checkbox off, or a Static font.
    //
    // TMP text open in a scene adds its glyphs back right after the save, so the font can show as modified
    // again. UI Toolkit text only rebuilds when Live Reload is on for its panel, otherwise it can show
    // blank until it redraws. The file on disk stays clean either way.
    internal sealed class ClearDynamicFontsOnSave : AssetModificationProcessor
    {
        // Only for the tests, so they can tell Unity's atlas page save really happened and got skipped.
        internal static int SkippedAtlasPageSaveCount { get; private set; }

        static string[] OnWillSaveAssets(string[] paths)
        {
            List<string> pathsLeftUnsaved = null;

            foreach (string path in paths)
            {
                // Most saves are scenes, prefabs and .meta files, skip those before asking the AssetDatabase.
                if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!IsHandledFontType(AssetDatabase.GetMainAssetTypeAtPath(path)))
                    continue;

                Object fontAsset = AssetDatabase.LoadMainAssetAtPath(path);
                if (!ShouldClear(fontAsset, path))
                    continue;

                if (IsTextCoreAtlasPageSave(fontAsset))
                {
                    pathsLeftUnsaved ??= new List<string>();
                    pathsLeftUnsaved.Add(path);
                    SkippedAtlasPageSaveCount++;
                    continue;
                }

                // One broken font shouldn't stop the rest of the project from saving.
                try
                {
                    Clear(fontAsset);
                }
                catch (Exception e)
                {
                    Debug.LogException(e, fontAsset);
                    Debug.LogError($"Couldn't fully clear the dynamic font data in '{path}', it might be saved half cleared this time.", fontAsset);
                }
            }

            return pathsLeftUnsaved == null ? paths : paths.Except(pathsLeftUnsaved).ToArray();
        }

        static bool IsHandledFontType(Type assetType)
        {
            if (typeof(TMP_FontAsset).IsAssignableFrom(assetType))
                return true;

#if TEXTCORE_DYNAMIC_DATA_PERSISTENCE
            // From 6000.7.0b6 and 7000.0.0a7 Unity clears TextCore fonts on save by itself, going by each font's Dynamic
            // Data Persistence setting. Earlier 6.7 betas and 7000.0 alphas don't, so they're still handled here.
            return false;
#else
            return typeof(TextCoreFontAsset).IsAssignableFrom(assetType);
#endif
        }

        static bool ShouldClear(Object fontAsset, string path)
        {
            if (!IsDynamic(fontAsset))
                return false;

            // The checkbox is internal on both font types, but they serialize it under the same name.
            if (!new SerializedObject(fontAsset).FindProperty("m_ClearDynamicDataOnBuild").boolValue)
                return false;

            // A dynamic font can't make its glyphs again without its source font, so clearing it would lose them for good.
            if (IsMissingSourceFont(fontAsset))
            {
                Debug.LogWarning($"Kept the dynamic font data in '{path}', its source font is missing so the glyphs couldn't be made again.", fontAsset);
                return false;
            }

            return true;
        }

        static bool IsDynamic(Object fontAsset) => fontAsset switch
        {
            TMP_FontAsset tmpFont => tmpFont.atlasPopulationMode is AtlasPopulationMode.Dynamic or AtlasPopulationMode.DynamicOS,
            TextCoreFontAsset textCoreFont => textCoreFont.atlasPopulationMode is TextCoreAtlasPopulationMode.Dynamic or TextCoreAtlasPopulationMode.DynamicOS,
            _ => false,
        };

        // Dynamic OS fonts read their glyphs from the fonts installed on the machine and never have a source font file,
        // so they always get cleared, same as in Unity's own cleanup.
        static bool IsMissingSourceFont(Object fontAsset)
        {
            bool needsSourceFontLookup = fontAsset switch
            {
                TMP_FontAsset tmpFont => tmpFont.atlasPopulationMode == AtlasPopulationMode.Dynamic && tmpFont.sourceFontFile == null,
                TextCoreFontAsset textCoreFont => textCoreFont.atlasPopulationMode == TextCoreAtlasPopulationMode.Dynamic && textCoreFont.sourceFontFile == null,
                _ => false,
            };
            if (!needsSourceFontLookup)
                return false;

            // With the reference empty, both font types load the source font again from this GUID the next time they
            // need a glyph, so the font is only really missing when the GUID doesn't lead to a font anymore.
            using SerializedObject serializedFont = new SerializedObject(fontAsset);
            string sourceFontGuid = serializedFont.FindProperty("m_SourceFontFileGUID")?.stringValue;
            if (string.IsNullOrEmpty(sourceFontGuid))
                return true;

            return AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(sourceFontGuid)) == null;
        }

        // Unity 6.5 and 6.6 save a TextCore font by themselves whenever it gets a new atlas page. Clearing it in that save
        // makes the open UI fill it again, which adds the page again and saves again, every few frames. Those saves are
        // skipped instead, so the glyphs stay in memory until the next real save clears them. Unity stopped saving there in
        // 6000.7.0b6 and 7000.0.0a7, so this can go once the package drops 6.5, 6.6, the early 6.7 betas and the early
        // 7000.0 alphas.
        static bool IsTextCoreAtlasPageSave(Object fontAsset)
        {
#if UNITY_6000_5_OR_NEWER && !TEXTCORE_DYNAMIC_DATA_PERSISTENCE
            return fontAsset is TextCoreFontAsset && new StackTrace().ToString().Contains("TextEditorResourceManager.DoPostRenderUpdates");
#else
            return false;
#endif
        }

        static void Clear(Object fontAsset)
        {
            switch (fontAsset)
            {
                case TMP_FontAsset tmpFont:
                    tmpFont.ClearFontAssetData(setAtlasSizeToZero: true);
                    break;

                case TextCoreFontAsset textCoreFont:
                    textCoreFont.ClearFontAssetData(setAtlasSizeToZero: true);
                    break;
            }
        }
    }
}
