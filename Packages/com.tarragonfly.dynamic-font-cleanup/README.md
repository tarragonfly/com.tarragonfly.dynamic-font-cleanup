# Tarragon.Fly() Dynamic Font Cleanup

Dynamic font assets save the glyphs and atlas they generate at runtime into the same `.asset` file as
their settings. So every play session leaves a big diff on the font, and if two people play the game
before committing, they get merge conflicts on it.

This package clears that generated data during Unity's `OnWillSaveAssets` callback, so supported saves
keep the font's settings without its generated glyphs and atlas pages. It handles standalone `.asset`
files whose main asset is one of these font types:

- TextMeshPro font assets (`TMP_FontAsset`), used by uGUI and world-space TMP text.
- TextCore font assets (`UnityEngine.TextCore.Text.FontAsset`), used by UI Toolkit.

Fonts set to **Dynamic** or **Dynamic OS** with **Clear Dynamic Data On Build** ticked are cleared, the
same fonts Unity's own build and quit cleanup picks. **Static** fonts and fonts with that box unticked are
left alone.

Supports Unity 6.0 to 6.7. Editor only, nothing ends up in builds. From Unity 6000.7.0b6, Unity clears
TextCore fonts on save by itself (per font, with **Dynamic Data Persistence**), so there the package only
handles TMP fonts. The EditMode tests pass on 6000.0.82f1, 6000.6.7f1 and 6000.7.0b6. 6.7 support goes by
the 6.7 beta, so it still needs a check on the final 6.7 release.

## Install

In the Package Manager, pick **Install package from git URL** and paste this, keeping a release tag
after the `#`:

```
https://github.com/tarragonfly/com.tarragonfly.dynamic-font-cleanup.git?path=Packages/com.tarragonfly.dynamic-font-cleanup#v1.0.0
```

Or add it to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.tarragonfly.dynamic-font-cleanup": "https://github.com/tarragonfly/com.tarragonfly.dynamic-font-cleanup.git?path=Packages/com.tarragonfly.dynamic-font-cleanup#v1.0.0"
  }
}
```

To run the package's tests from your game's Test Runner, also add it to `testables`:

```json
{
  "testables": ["com.tarragonfly.dynamic-font-cleanup"]
}
```

The tests make their own font assets in a new `Assets/DynamicFontCleanupTests` folder from the
LiberationSans copy in `Tests/Editor/Fonts` (OFL licensed, the same font TMP's essentials use). Unity
adds a number to the folder name if a folder by that name already exists, and each test deletes only the
folder it created. If a test run gets killed before it cleans up, the next run deletes the leftover
`Assets/DynamicFontCleanupTests` folder, as long as it holds nothing but the test fonts.

## Good to know

- Fonts stored as sub-assets are outside this package's scope. Creating or importing a font can also
  write generated data without running the save callback, and Unity only saves fonts that have unsaved
  changes, so a font like that stays as it is when you save. Change any setting on it and change it back,
  then save it before committing.
- Custom tools that write assets without invoking `OnWillSaveAssets` bypass cleanup. Unity documents
  `SaveAssetIfDirty` as bypassing that callback, although the package's tests save that way and see it
  running on the versions above. Check the behavior on the Editor version your tools use.
- Unity's own **Clear Dynamic Data On Build** cleanup only runs when you make a build or close the
  Editor, so before 6.7 the font file still changes on every save. This package handles the saves.
- Fonts made with older TMP versions often have **Clear Dynamic Data On Build** unticked, and on Unity 6.0
  every new TextCore (UI Toolkit) font starts with it unticked too. Tick it on any dynamic font you want
  kept clean.
- Filling a dynamic font with characters on purpose so they're ready at startup, or hand-tuning its
  kerning? Untick **Clear Dynamic Data On Build** on that font. Saving clears the kerning tables too,
  since play sessions add kerning pairs to them as well.
- A dynamic font whose source font file is missing is skipped with a warning, because its glyphs
  couldn't be made again.
- TMP text open in a scene adds its glyphs back right after a save, so a font can show as modified again
  in the Editor. UI Toolkit text only rebuilds when Live Reload is on for its panel, otherwise it can
  show blank until it redraws. The file on disk stays clean either way, and the next save clears it again.
- On Unity 6.5 and 6.6, Unity saves a TextCore font by itself when it gets a new atlas page. The package
  skips those saves, so the glyphs stay in memory until your next save clears them.

## Credits

Based on the idea in [@cxode's Unity forum post](https://forum.unity.com/threads/tmpro-dynamic-font-asset-constantly-changes-in-source-control.1227831/#post-8934711)
and [STARasGAMES/tmpro-dynamic-data-cleaner](https://github.com/STARasGAMES/tmpro-dynamic-data-cleaner).
This version is written from scratch for Unity 6 and adds TextCore fonts.
