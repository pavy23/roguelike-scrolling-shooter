# SFC foreground art revamp — review workspace

User direction (2026-09-30): replace the foreground pixel art with the refinement
of peak Super Famicom games. Backgrounds are outside this art replacement.

This directory holds review candidates, generation prompts, and asset inventory.
Candidates are not accepted production sprites or Unity atlases. Do not import
them into Assets/Art without the user's art review. Existing player builds remain
usable while the replacement set is prepared.

The production target remains 640×360 / PPU 16 with the existing collision and
attachment contracts. Palette, silhouette, materials and animation are being
redesigned. Candidate images must pass native-size and frame-continuity review
before production integration.

- [한국어 제작·검수 기준](BRIEF.md)
- [Candidate v1](candidates/style-sheet-v1.png)
- [Candidate v2 — recommended direction](candidates/style-sheet-v2.png)
- [Full foreground inventory](foreground-inventory.json)
- [Prompts](prompts.md) and [generation manifest](generation-manifest.json)
