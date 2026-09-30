# SFC foreground art revamp — review workspace

User direction (2026-09-30): replace the foreground pixel art with the refinement
of peak Super Famicom games. Backgrounds are outside this art replacement.

The user selected **style-sheet-v2**: "2안 가자". Its palette, material planes and
silhouette language are the approved production direction. Native-size pilot
sprites and animation are now being prepared; see [curation](CURATION.md).

This directory holds review candidates, generation prompts, and asset inventory.
Candidates are not accepted production sprites or Unity atlases. Do not import
them into Assets/Art without the user's art review. Existing player builds remain
usable while the replacement set is prepared.

The production target remains 640×360 / PPU 16 with the existing collision and
attachment contracts. Palette, silhouette, materials and animation are being
redesigned. Candidate images must pass native-size and frame-continuity review
before production integration.

- [한국어 제작·검수 기준](BRIEF.md)
- [Native pilot results and remaining work](pilot/README.md)
- [Interactive native sprite / engine review](pilot/review/index.html)
- [Unity old/new comparison](pilot/review/pilot-static-comparison.png)
- [Candidate v1](candidates/style-sheet-v1.png)
- [Style v2 — selected by the user](candidates/style-sheet-v2.png)
- [Full foreground inventory](foreground-inventory.json)
- [Prompts](prompts.md) and [generation manifest](generation-manifest.json)
