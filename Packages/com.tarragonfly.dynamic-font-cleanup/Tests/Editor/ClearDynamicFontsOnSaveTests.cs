using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TextCore.LowLevel;
using Object = UnityEngine.Object;
using TextCoreAtlasPopulationMode = UnityEngine.TextCore.Text.AtlasPopulationMode;
using TextCoreFontAsset = UnityEngine.TextCore.Text.FontAsset;

namespace TarragonFly.DynamicFontCleanup.Editor.Tests
{
    public class ClearDynamicFontsOnSaveTests
    {
        const string TestFolder = "Assets/DynamicFontCleanupTests";
        const string TmpFontPath = TestFolder + "/Test TMP Font.asset";
        const string TextCoreFontPath = TestFolder + "/Test TextCore Font.asset";
        const string TestCharacters = "abcd";
        const string EmptyCharacterTable = "m_CharacterTable: []";
        const string EmptyGlyphTable = "m_GlyphTable: []";

        // A cleared atlas can end up 0x0 or 1x1 depending on the font type, both count as empty.
        const int ClearedAtlasMaxWidth = 1;

        // Shipped with the tests so they also run inside a game with no fonts of its own. Unity's built-in
        // LegacyRuntime font doesn't work here, TMP can't read its font data.
        static Font TestFont => AssetDatabase.LoadAssetAtPath<Font>("Packages/com.tarragonfly.dynamic-font-cleanup/Tests/Editor/Fonts/LiberationSans.ttf");

        [SetUp]
        public void CreateTestFolder()
        {
            if (EditorSettings.serializationMode != SerializationMode.ForceText)
                Assert.Ignore("Set Asset Serialization to Force Text to run these tests, they read the saved font files as text.");

            if (!AssetDatabase.IsValidFolder(TestFolder))
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(TestFolder));
        }

        [TearDown]
        public void DeleteTestFolder()
        {
            AssetDatabase.DeleteAsset(TestFolder);
        }

        [Test]
        public void TmpDynamicFont_SavedFileHasNoGlyphs()
        {
            TMP_FontAsset font = CreateTmpFont();

            SaveFont(font);

            Assert.That(font.characterTable, Is.Empty);
            Assert.That(font.atlasTexture.width, Is.LessThanOrEqualTo(ClearedAtlasMaxWidth));
            AssertSavedFileHasNoGlyphs(TmpFontPath);
        }

        [Test]
        public void TmpStaticFont_KeepsGlyphsWhenSaved()
        {
            TMP_FontAsset font = CreateTmpFont();
            SetPopulationMode(font, (int)AtlasPopulationMode.Static);

            SaveFont(font);

            Assert.That(font.characterTable, Is.Not.Empty);
        }

        [Test]
        public void TmpDynamicFont_KeepsGlyphsWhenClearOnBuildIsOff()
        {
            TMP_FontAsset font = CreateTmpFont();
            SetClearDynamicDataOnBuild(font, false);

            SaveFont(font);

            Assert.That(font.characterTable, Is.Not.Empty);
        }

        [Test]
        public void TmpDynamicFont_KeepsGlyphsWhenSourceFontIsMissing()
        {
            TMP_FontAsset font = CreateTmpFont();
            var serializedFont = new SerializedObject(font);
            serializedFont.FindProperty("m_SourceFontFile").objectReferenceValue = null;
            serializedFont.ApplyModifiedPropertiesWithoutUndo();

            LogAssert.Expect(LogType.Warning, new Regex("source font is missing"));
            SaveFont(font);

            Assert.That(font.characterTable, Is.Not.Empty);
        }

#if !UNITY_6000_7_OR_NEWER
        [Test]
        public void TextCoreDynamicFont_SavedFileHasNoGlyphs()
        {
            TextCoreFontAsset font = CreateTextCoreFont();

            SaveFont(font);

            Assert.That(font.characterTable, Is.Empty);
            Assert.That(font.atlasTexture.width, Is.LessThanOrEqualTo(ClearedAtlasMaxWidth));
            AssertSavedFileHasNoGlyphs(TextCoreFontPath);
        }

        [Test]
        public void TextCoreStaticFont_KeepsGlyphsWhenSaved()
        {
            TextCoreFontAsset font = CreateTextCoreFont();
            SetPopulationMode(font, (int)TextCoreAtlasPopulationMode.Static);

            SaveFont(font);

            Assert.That(font.characterTable, Is.Not.Empty);
        }

        [Test]
        public void TextCoreDynamicFont_KeepsGlyphsWhenClearOnBuildIsOff()
        {
            TextCoreFontAsset font = CreateTextCoreFont();
            SetClearDynamicDataOnBuild(font, false);

            SaveFont(font);

            Assert.That(font.characterTable, Is.Not.Empty);
        }
#endif

#if UNITY_6000_5_OR_NEWER && !UNITY_6000_7_OR_NEWER
        [UnityTest]
        public IEnumerator TextCoreDynamicFont_UnitysAtlasPageSaveIsSkipped()
        {
            // Tiny atlas so a handful of characters spill onto a second page, which makes Unity save the font on its next update.
            TextCoreFontAsset font = TextCoreFontAsset.CreateFontAsset(TestFont, 16, 2, GlyphRenderMode.SDFAA, 64, 64);
            SetClearDynamicDataOnBuild(font, true);
            CreateFontFile(font, font.atlasTexture, font.material, TextCoreFontPath);
            SaveFont(font);

            Assert.That(font.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZ", includeFontFeatures: false), Is.True);
            Assert.That(font.atlasTextures.Length, Is.GreaterThan(1));

            yield return null;
            yield return null;

            Assert.That(font.characterTable, Is.Not.Empty);
            AssertSavedFileHasNoGlyphs(TextCoreFontPath);
        }
#endif

        // Same steps as Create > TextMeshPro > Font Asset, through the public API instead of TMP's internal menu code.
        // The glyphs go in before the file is written, so the saved file starts out with them.
        static TMP_FontAsset CreateTmpFont()
        {
            // TMP can't make a font asset without its settings file, which comes with TMP Essential Resources.
            if (TMP_Settings.instance == null)
                Assert.Ignore("Import TMP Essential Resources (Window > TextMeshPro) to run the TMP font tests.");

            TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(TestFont);
            Assert.That(font.TryAddCharacters(TestCharacters, includeFontFeatures: false), Is.True);
            SetClearDynamicDataOnBuild(font, true);
            CreateFontFile(font, font.atlasTexture, font.material, TmpFontPath);
            AssertSavedFileHasGlyphs(TmpFontPath);
            return font;
        }

        static TextCoreFontAsset CreateTextCoreFont()
        {
            TextCoreFontAsset font = TextCoreFontAsset.CreateFontAsset(TestFont);
            Assert.That(font.TryAddCharacters(TestCharacters, includeFontFeatures: false), Is.True);
            SetClearDynamicDataOnBuild(font, true);
            CreateFontFile(font, font.atlasTexture, font.material, TextCoreFontPath);
            AssertSavedFileHasGlyphs(TextCoreFontPath);
            return font;
        }

        static void CreateFontFile(Object font, Texture2D atlas, Material material, string path)
        {
            // TextCore marks fonts made at runtime as don't-save, and CreateAsset refuses those.
            font.hideFlags = HideFlags.None;
            atlas.hideFlags = HideFlags.None;
            material.hideFlags = HideFlags.None;

            AssetDatabase.CreateAsset(font, path);
            AssetDatabase.AddObjectToAsset(atlas, font);
            AssetDatabase.AddObjectToAsset(material, font);
        }

        // Both font types serialize the mode under the same name, and this skips any editor-only logic in the setters.
        static void SetPopulationMode(Object font, int mode)
        {
            var serializedFont = new SerializedObject(font);
            serializedFont.FindProperty("m_AtlasPopulationMode").intValue = mode;
            serializedFont.ApplyModifiedPropertiesWithoutUndo();
        }

        // Internal on both font types. New fonts copy it from the project's TMP or TextCore settings, so it's set here.
        static void SetClearDynamicDataOnBuild(Object font, bool clear)
        {
            var serializedFont = new SerializedObject(font);
            serializedFont.FindProperty("m_ClearDynamicDataOnBuild").boolValue = clear;
            serializedFont.ApplyModifiedPropertiesWithoutUndo();
        }

        // Saves only the test font, so running these inside a game doesn't also save whatever else is dirty there.
        static void SaveFont(Object font)
        {
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssetIfDirty(font);
        }

        static void AssertSavedFileHasGlyphs(string path)
        {
            Assert.That(File.ReadAllText(path), Does.Not.Contain(EmptyCharacterTable));
        }

        static void AssertSavedFileHasNoGlyphs(string path)
        {
            string savedFile = File.ReadAllText(path);
            Assert.That(savedFile, Does.Contain(EmptyCharacterTable));
            Assert.That(savedFile, Does.Contain(EmptyGlyphTable));
        }
    }
}
