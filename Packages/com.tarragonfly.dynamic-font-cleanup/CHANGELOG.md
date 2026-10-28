# Changelog

All notable changes to this package are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.1] - 2026-10-28

### Changed

- Documented the standalone-font scope and save paths that can bypass cleanup.

### Fixed

- On Unity 6.7 betas before 6000.7.0b6, which don't clear TextCore fonts on save yet, TextCore fonts are
  cleared again and the tests compile.
- On Unity 7000.0 alphas before 7000.0.0a7, which don't clear TextCore fonts on save yet either, TextCore
  fonts are cleared again.
- A dynamic font that lost its reference to its source font file, while the file is still in the project,
  is now cleared instead of kept with a "source font is missing" warning.
- Tests create a new folder for each test, so an existing folder and its contents are left alone. A folder
  left behind by a killed test run gets deleted on the next run if it only holds the test fonts.

## [1.0.0] - 2026-10-24

### Added

- Dynamic TMP and TextCore (UI Toolkit) font assets get their generated glyphs, atlas pages and kerning
  tables cleared right before Unity saves them, so the files stay out of version control diffs.
- Only fonts with **Clear Dynamic Data On Build** ticked are cleared, the same ones Unity's own build and
  quit cleanup picks. Static fonts are left alone.
- Dynamic fonts whose source font file is missing are skipped with a warning, since their glyphs couldn't
  be made again.
- On Unity 6.5 and 6.6, Unity's own save of a TextCore font after it gets a new atlas page is skipped, so
  open UI Toolkit panels don't keep clearing and refilling the font.
- On Unity 6.7, TextCore fonts are left to Unity's own save cleanup.

[1.0.1]: https://github.com/tarragonfly/com.tarragonfly.dynamic-font-cleanup/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/tarragonfly/com.tarragonfly.dynamic-font-cleanup/releases/tag/v1.0.0
