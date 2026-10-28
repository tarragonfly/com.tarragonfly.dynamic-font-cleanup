# Tarragon.Fly() Dynamic Font Cleanup

Clears generated glyphs and atlas data from standalone dynamic TextMeshPro and TextCore (UI Toolkit)
font assets during Unity's save callback, reducing version control diffs. Fonts need
**Clear Dynamic Data On Build** enabled. From Unity 6000.7.0b6, Unity clears TextCore fonts by itself
(per font, with **Dynamic Data Persistence**), and the package only handles TMP fonts. The package itself is
`Packages/com.tarragonfly.dynamic-font-cleanup`, and its
[README](Packages/com.tarragonfly.dynamic-font-cleanup/README.md) has the details.

Install it in a game from the Package Manager with this git URL, keeping a release tag after the `#`:

```
https://github.com/tarragonfly/com.tarragonfly.dynamic-font-cleanup.git?path=Packages/com.tarragonfly.dynamic-font-cleanup#v1.0.0
```

The rest of this repository is an empty Unity 6000.0 project, set up so the package stays embedded
while it's being worked on. Open it in Unity and run the EditMode tests from the Test Runner.
