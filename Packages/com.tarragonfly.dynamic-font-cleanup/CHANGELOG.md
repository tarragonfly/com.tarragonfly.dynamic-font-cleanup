# Changelog

All notable changes to this package are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

[1.0.0]: https://github.com/tarragonfly/com.tarragonfly.dynamic-font-cleanup/releases/tag/v1.0.0
