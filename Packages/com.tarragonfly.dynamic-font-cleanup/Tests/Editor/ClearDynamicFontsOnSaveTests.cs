using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TextCore.LowLevel;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using TextCoreAtlasPopulationMode = UnityEngine.TextCore.Text.AtlasPopulationMode;
using TextCoreFontAsset = UnityEngine.TextCore.Text.FontAsset;
#if TEXTCORE_DYNAMIC_DATA_PERSISTENCE
using TextCoreDynamicDataPersistence = UnityEngine.TextCore.Text.DynamicDataPersistence;
#endif

namespace TarragonFly.DynamicFontCleanup.Editor.Tests
{
    public class ClearDynamicFontsOnSaveTests
    {
        const string TestFolderName = "DynamicFontCleanupTests";
        const string TmpFontFileName = "Test TMP Font.asset";
        const string TextCoreFontFileName = "Test TextCore Font.asset";
        string testFolderPath;
        string TmpFontPath => testFolderPath + "/" + TmpFontFileName;
        string TextCoreFontPath => testFolderPath + "/" + TextCoreFontFileName;
        const string TestCharacters = "abcd";
        const string EmptyCharacterTable = "m_CharacterTable: []";
        const string EmptyGlyphTable = "m_GlyphTable: []";
        const string EmptyKerningTable = "m_GlyphPairAdjustmentRecords: []";

        // Small so each set in CharactersAddedBetweenSaves spills onto extra atlas pages. Both sets also have
        // kerning pairs in LiberationSans, like AV and To.
        const int TestAtlasSize = 64;
        static readonly string[] CharactersAddedBetweenSaves = { "ABCDEFGHIJKLMNOPQRSTUVWXYZ", "AVToabcdefghijklmnopqrstuvwxyz0123456789" };

        // A cleared atlas can end up 0x0 or 1x1 depending on the font type, both count as empty.
        const int ClearedAtlasMaxDimension = 1;

        // Shipped with the tests so they also run inside a game with no fonts of its own. Unity's built-in
        // LegacyRuntime font doesn't work here, TMP can't read its font data.
        static Font TestFont => AssetDatabase.LoadAssetAtPath<Font>("Packages/com.tarragonfly.dynamic-font-cleanup/Tests/Editor/Fonts/LiberationSans.ttf");

        [SetUp]
        public void CreateTestFolder()
        {
            if (EditorSettings.serializationMode != SerializationMode.ForceText)
                Assert.Ignore("Set Asset Serialization to Force Text to run these tests, they read the saved font files as text.");

            // A run killed before its TearDown leaves its folder behind, so the next run cleans it up.
            string leftoverFolderPath = "Assets/" + TestFolderName;
            if (HoldsOnlyTestFonts(leftoverFolderPath))
                AssetDatabase.DeleteAsset(leftoverFolderPath);

            // CreateFolder picks a free name when the project already has a folder by this name.
            string folderGuid = AssetDatabase.CreateFolder("Assets", TestFolderName);
            testFolderPath = AssetDatabase.GUIDToAssetPath(folderGuid);
            Assert.That(testFolderPath, Is.Not.Empty);
        }

        [TearDown]
        public void DeleteTestFolder()
        {
            if (string.IsNullOrEmpty(testFolderPath))
                return;

            string folderPath = testFolderPath;
            testFolderPath = null;
            Assert.That(AssetDatabase.DeleteAsset(folderPath), Is.True, $"Couldn't delete the test folder '{folderPath}'.");
        }

        // Anything besides the test fonts means the folder isn't a leftover from these tests, so it's left alone.
        static bool HoldsOnlyTestFonts(string folderPath)
        {
            if (!AssetDatabase.IsValidFolder(folderPath))
                return false;

            return Directory.EnumerateFileSystemEntries(folderPath, "*", SearchOption.AllDirectories)
                .Where(entry => !entry.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .All(entry => Path.GetFileName(entry) is TmpFontFileName or TextCoreFontFileName);
        }

        [Test]
        public void TestFolders_KeepExistingContent()
        {
            string existingFolderPath = testFolderPath;
            AssetDatabase.CreateFolder(existingFolderPath, "Keep");
            ClearDynamicFontsOnSaveTests otherTest = new ClearDynamicFontsOnSaveTests();

            try
            {
                otherTest.CreateTestFolder();
                string otherFolderPath = otherTest.testFolderPath;
                Assert.That(otherFolderPath, Is.Not.EqualTo(existingFolderPath));
                Assert.That(AssetDatabase.IsValidFolder(otherFolderPath), Is.True);

                otherTest.DeleteTestFolder();

                Assert.That(AssetDatabase.IsValidFolder(otherFolderPath), Is.False);
                Assert.That(AssetDatabase.IsValidFolder(existingFolderPath + "/Keep"), Is.True);
            }
            finally
            {
                otherTest.DeleteTestFolder();
            }
        }

        [Test]
        public void TestFolders_OnlyFoldersWithJustTestFontsCountAsLeftovers()
        {
            CreateTextCoreFont();
            Assert.That(HoldsOnlyTestFonts(testFolderPath), Is.True);

            AssetDatabase.CreateFolder(testFolderPath, "Keep");
            Assert.That(HoldsOnlyTestFonts(testFolderPath), Is.False);
        }

        [Test]
        public void TmpDynamicFont_SavedFileHasNoGlyphs()
        {
            TMP_FontAsset font = CreateTmpFont();

            SaveFont(font);

            Assert.That(font.characterTable, Is.Empty);
            Assert.That(font.atlasTexture.width, Is.LessThanOrEqualTo(ClearedAtlasMaxDimension));
            AssertSavedFileHasNoGlyphs(TmpFontPath);
        }

        [Test]
        public void TmpDynamicFont_RepeatedSavesKeepFilesUnchanged()
        {
            TMP_FontAsset font = CreateTmpFont();
            Material material = font.material;
            SaveFont(font);
            Assert.That(font.characterTable, Is.Empty);
            AssertSavedFontIsCleared(TmpFontPath);
            byte[] savedFontBytes = File.ReadAllBytes(TmpFontPath);
            byte[] savedMetaBytes = File.ReadAllBytes(TmpFontPath + ".meta");

            foreach (string characters in CharactersAddedBetweenSaves)
            {
                Assert.That(font.TryAddCharacters(characters, includeFontFeatures: true), Is.True);
                Assert.That(font.atlasTextures.Length, Is.GreaterThan(1));
                Assert.That(font.fontFeatureTable.glyphPairAdjustmentRecords, Is.Not.Empty);

                SaveFont(font);

                // Checked before anything reimports the font, so this is what the save itself left in memory.
                Assert.That(font.characterTable, Is.Empty);
                Assert.That(font.fontFeatureTable.glyphPairAdjustmentRecords, Is.Empty);
                Assert.That(File.ReadAllBytes(TmpFontPath), Is.EqualTo(savedFontBytes));
                Assert.That(File.ReadAllBytes(TmpFontPath + ".meta"), Is.EqualTo(savedMetaBytes));
            }

            Assert.That(font.sourceFontFile, Is.EqualTo(TestFont));
            Assert.That(font.material, Is.EqualTo(material));
            Assert.That(font.atlasWidth, Is.EqualTo(TestAtlasSize));
            Assert.That(font.atlasHeight, Is.EqualTo(TestAtlasSize));
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
            ClearSourceFont(font);

            LogAssert.Expect(LogType.Warning, new Regex("source font is missing"));
            SaveFont(font);

            Assert.That(font.characterTable, Is.Not.Empty);
        }

        [Test]
        public void TmpDynamicFont_ClearsWhenSourceFontCanStillBeFound()
        {
            TMP_FontAsset font = CreateTmpFont();
            ClearSourceFontReference(font);
            Assert.That(font.sourceFontFile, Is.Null);

            List<string> warnings = SaveFontCollectingWarnings(font);

            Assert.That(warnings, Is.Empty);
            Assert.That(font.characterTable, Is.Empty);
            AssertSavedFileHasNoGlyphs(TmpFontPath);
        }

        [Test]
        public void TmpDynamicFont_SaveAssetsLeavesNoGlyphs()
        {
            IgnoreUnlessPackageIsEmbedded();
            TMP_FontAsset font = CreateTmpFont();

            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();

            Assert.That(font.characterTable, Is.Empty);
            AssertSavedFileHasNoGlyphs(TmpFontPath);
        }

#if !TEXTCORE_DYNAMIC_DATA_PERSISTENCE
        [Test]
        public void TextCoreDynamicFont_SavedFileHasNoGlyphs()
        {
            TextCoreFontAsset font = CreateTextCoreFont();

            SaveFont(font);

            Assert.That(font.characterTable, Is.Empty);
            Assert.That(font.atlasTexture.width, Is.LessThanOrEqualTo(ClearedAtlasMaxDimension));
            AssertSavedFileHasNoGlyphs(TextCoreFontPath);
        }

        [Test]
        public void TextCoreDynamicFont_RepeatedSavesKeepFilesUnchanged()
        {
            TextCoreFontAsset font = CreateTextCoreFont();
            Material material = font.material;
            SaveFont(font);
            Assert.That(font.characterTable, Is.Empty);
            AssertSavedFontIsCleared(TextCoreFontPath);
            byte[] savedFontBytes = File.ReadAllBytes(TextCoreFontPath);
            byte[] savedMetaBytes = File.ReadAllBytes(TextCoreFontPath + ".meta");

            foreach (string characters in CharactersAddedBetweenSaves)
            {
                Assert.That(font.TryAddCharacters(characters, includeFontFeatures: true), Is.True);
                Assert.That(font.atlasTextures.Length, Is.GreaterThan(1));
                Assert.That(GetKerningPairCount(font), Is.GreaterThan(0));

                SaveFont(font);

                // Checked before anything reimports the font, so this is what the save itself left in memory.
                Assert.That(font.characterTable, Is.Empty);
                Assert.That(GetKerningPairCount(font), Is.Zero);
                Assert.That(File.ReadAllBytes(TextCoreFontPath), Is.EqualTo(savedFontBytes));
                Assert.That(File.ReadAllBytes(TextCoreFontPath + ".meta"), Is.EqualTo(savedMetaBytes));
            }

            Assert.That(font.sourceFontFile, Is.EqualTo(TestFont));
            Assert.That(font.material, Is.EqualTo(material));
            Assert.That(font.atlasWidth, Is.EqualTo(TestAtlasSize));
            Assert.That(font.atlasHeight, Is.EqualTo(TestAtlasSize));
        }

        static int GetKerningPairCount(TextCoreFontAsset font)
        {
            using SerializedObject serializedFont = new SerializedObject(font);
            return serializedFont.FindProperty("m_FontFeatureTable.m_GlyphPairAdjustmentRecords").arraySize;
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

        [Test]
        public void TextCoreDynamicFont_KeepsGlyphsWhenSourceFontIsMissing()
        {
            TextCoreFontAsset font = CreateTextCoreFont();
            ClearSourceFont(font);

            LogAssert.Expect(LogType.Warning, new Regex("source font is missing"));
            SaveFont(font);

            Assert.That(font.characterTable, Is.Not.Empty);
            AssertSavedFileHasGlyphs(TextCoreFontPath);
        }

        [Test]
        public void TextCoreDynamicFont_ClearsWhenSourceFontCanStillBeFound()
        {
            TextCoreFontAsset font = CreateTextCoreFont();
            ClearSourceFontReference(font);
            Assert.That(font.sourceFontFile, Is.Null);

            List<string> warnings = SaveFontCollectingWarnings(font);

            Assert.That(warnings, Is.Empty);
            Assert.That(font.characterTable, Is.Empty);
            AssertSavedFileHasNoGlyphs(TextCoreFontPath);
        }

        [Test]
        public void TextCoreDynamicFont_SaveAssetsLeavesNoGlyphs()
        {
            IgnoreUnlessPackageIsEmbedded();
            TextCoreFontAsset font = CreateTextCoreFont();

            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();

            Assert.That(font.characterTable, Is.Empty);
            AssertSavedFileHasNoGlyphs(TextCoreFontPath);
        }
#else
        [TestCase(TextCoreDynamicDataPersistence.SessionOnly)]
        [TestCase(TextCoreDynamicDataPersistence.Persistent)]
        public void TextCoreDynamicFont_UsesUnityPersistenceSetting(TextCoreDynamicDataPersistence persistence)
        {
            // The file starts out with glyphs, so the SessionOnly case only passes if the save really cleared them.
            TextCoreFontAsset font = CreateTextCoreFont();
            font.dynamicDataPersistence = persistence;

            SaveFont(font);

            if (persistence == TextCoreDynamicDataPersistence.SessionOnly)
                AssertSavedFileHasNoGlyphs(TextCoreFontPath);
            else
                AssertSavedFileHasGlyphs(TextCoreFontPath);
        }
#endif

#if UNITY_6000_5_OR_NEWER && !TEXTCORE_DYNAMIC_DATA_PERSISTENCE
        [UnityTest]
        public IEnumerator TextCoreDynamicFont_UnitysAtlasPageSaveIsSkipped()
        {
            TextCoreFontAsset font = CreateTextCoreFont();
            SaveFont(font);
            int skippedSaveCountBefore = ClearDynamicFontsOnSave.SkippedAtlasPageSaveCount;

            Assert.That(font.TryAddCharacters(CharactersAddedBetweenSaves[0], includeFontFeatures: false), Is.True);
            Assert.That(font.atlasTextures.Length, Is.GreaterThan(1));

            // A new atlas page makes Unity save the font on its next update, or after a camera renders when a render
            // pipeline is active, which might never happen in batchmode.
            for (int frame = 0; frame < 10 && ClearDynamicFontsOnSave.SkippedAtlasPageSaveCount == skippedSaveCountBefore; frame++)
                yield return null;

            if (ClearDynamicFontsOnSave.SkippedAtlasPageSaveCount == skippedSaveCountBefore)
                Assert.Inconclusive("Unity didn't save the font by itself after the new atlas page (for example with a render pipeline active in batchmode), so the skip wasn't checked.");

            Assert.That(font.characterTable, Is.Not.Empty);
            AssertSavedFileHasNoGlyphs(TextCoreFontPath);

            SaveFont(font);
            Assert.That(font.characterTable, Is.Empty);
            AssertSavedFontIsCleared(TextCoreFontPath);
        }
#endif

        // Same steps as Create > TextMeshPro > Font Asset, through the public API instead of TMP's internal menu code,
        // except for the small atlas (see TestAtlasSize). The glyphs go in before the file is written, so the saved
        // file starts out with them.
        TMP_FontAsset CreateTmpFont()
        {
            // TMP can't make a font asset without its settings file, which comes with TMP Essential Resources.
            if (TMP_Settings.instance == null)
                Assert.Ignore("Import TMP Essential Resources (Window > TextMeshPro) to run the TMP font tests.");

            TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(TestFont, 16, 2, GlyphRenderMode.SDFAA, TestAtlasSize, TestAtlasSize);
            Assert.That(font.TryAddCharacters(TestCharacters, includeFontFeatures: false), Is.True);
            SetClearDynamicDataOnBuild(font, true);
            CreateFontFile(font, font.atlasTexture, font.material, TmpFontPath);
            AssertSavedFileHasGlyphs(TmpFontPath);
            return font;
        }

        TextCoreFontAsset CreateTextCoreFont()
        {
            TextCoreFontAsset font = TextCoreFontAsset.CreateFontAsset(TestFont, 16, 2, GlyphRenderMode.SDFAA, TestAtlasSize, TestAtlasSize);
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

        // Internal on both font types. New TMP fonts copy it from the project's TMP settings, and new TextCore fonts
        // start with it off on Unity 6.0, so it's set here.
        static void SetClearDynamicDataOnBuild(Object font, bool clear)
        {
            var serializedFont = new SerializedObject(font);
            serializedFont.FindProperty("m_ClearDynamicDataOnBuild").boolValue = clear;
            serializedFont.ApplyModifiedPropertiesWithoutUndo();
        }

        // Clears the saved reference and the GUID, so the font can't find its source font again, the same as when the
        // .ttf gets deleted. Both font types serialize them under the same names.
        static void ClearSourceFont(Object font)
        {
            using SerializedObject serializedFont = new SerializedObject(font);
            serializedFont.FindProperty("m_SourceFontFile").objectReferenceValue = null;
            serializedFont.FindProperty("m_SourceFontFileGUID").stringValue = string.Empty;
            serializedFont.ApplyModifiedPropertiesWithoutUndo();
        }

        // Clears only the saved reference and keeps the GUID, so the font can still load the .ttf again from it.
        static void ClearSourceFontReference(Object font)
        {
            using SerializedObject serializedFont = new SerializedObject(font);
            serializedFont.FindProperty("m_SourceFontFile").objectReferenceValue = null;
            serializedFont.ApplyModifiedPropertiesWithoutUndo();
        }

        // Saves only the test font, so running these inside a game doesn't also save whatever else is dirty there.
        static void SaveFont(Object font)
        {
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssetIfDirty(font);
        }

        static List<string> SaveFontCollectingWarnings(Object font)
        {
            List<string> warnings = new List<string>();
            void CollectWarning(string message, string stackTrace, LogType type)
            {
                if (type == LogType.Warning)
                    warnings.Add(message);
            }

            Application.logMessageReceived += CollectWarning;
            try
            {
                SaveFont(font);
            }
            finally
            {
                Application.logMessageReceived -= CollectWarning;
            }

            return warnings;
        }

        // File > Save goes through AssetDatabase.SaveAssets, which writes every unsaved asset in the project, so the
        // SaveAssets tests only run where the package is embedded, like in its own repository. A game that embeds the
        // package still runs them and gets its other unsaved assets saved along with the test font.
        static void IgnoreUnlessPackageIsEmbedded()
        {
            PackageInfo package = PackageInfo.FindForAssembly(typeof(ClearDynamicFontsOnSave).Assembly);
            if (package == null || package.source != PackageSource.Embedded)
                Assert.Ignore("Only runs with the package embedded, since AssetDatabase.SaveAssets would also save the game's other unsaved assets.");
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

        static void AssertSavedFontIsCleared(string path)
        {
            // Reimported so the sub-asset checks see what's in the file, not atlas pages or a material only the loaded font has.
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Object[] savedAssets = AssetDatabase.LoadAllAssetsAtPath(path);
            Texture2D[] textures = savedAssets.OfType<Texture2D>().ToArray();
            Assert.That(textures, Has.Length.EqualTo(1));
            Assert.That(textures[0].width, Is.LessThanOrEqualTo(ClearedAtlasMaxDimension));
            Assert.That(textures[0].height, Is.LessThanOrEqualTo(ClearedAtlasMaxDimension));
            Assert.That(savedAssets.OfType<Material>().Count(), Is.EqualTo(1));
            AssertSavedFileHasNoGlyphs(path);
            Assert.That(File.ReadAllText(path), Does.Contain(EmptyKerningTable));
        }
    }
}
