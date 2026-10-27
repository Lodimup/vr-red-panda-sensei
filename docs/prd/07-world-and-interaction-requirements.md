## 7 World and interaction requirements

The real environment provides the visual background. Place an adjustable virtual practice area in front of the learner with generous spacing and a fixed orientation for the current task. All objects are pre-rendered flat sprites; world-space positioning does not require 3D modeled assets.

### Prepared sprite assets

Bundle transparent PNG sprites or sprite atlases with the application. Every launch object and Sensei animation frame is prepared before the build. Each object has exactly one image (RULING(Handler 2026-10-28): one image per object, no colour variants). Runtime code can position, scale, highlight, or play the supplied frames; the model only selects catalog IDs and permitted actions. Object sprites are realistic, high-resolution images, not pixel art: every object is one 1024×1024 RGBA PNG of the object alone, photographic studio quality, on a transparent background. In the scene each object is presented as a flat two-dimensional cardboard cutout (a die-cut standee): the bundled `<object>_card.png` adds a printed card border and a visible cardboard edge around the cutout, and is shown on a flat plane with linear filtering and mipmaps. RULING(Handler 2026-10-27): 32×32 pixel art was rejected; objects are realistic high-resolution images presented as 2D cardboard cutouts. Sensei animation frames are produced with the Sloth Studio sprite pipeline and shipped at that pipeline's full output resolution. Generate no new object images, meshes, or animation frames during a conversation.

A request for an unavailable item can produce a spoken explanation or a normal text vocabulary card. It must not create a pretend catalog entry, block the conversation while generating art, or claim that the requested interactive object exists. Additional object packs are pre-rendered and validated before they are added.

### Launch asset catalog

| **Object** | **Japanese** | **Supported use** |
| --- | --- | --- |
| Water | 水 / みず | A distinct water-bottle sprite; request, choose, compare. |
| Tea | お茶 / おちゃ | A clearly different tea sprite; request, choose, compare. |
| Cup | コップ | One cup image; select and arrange. |
| Apple | りんご | Duplicate items for quantities and preferences. |
| Banana | バナナ | Contrast with other food and practice quantities. |
| Rice ball | おにぎり | Food preferences and simple role-play. |
| Book | 本 / ほん | One book image; arrange and compare. |
| Pen | ペン | A small everyday object with a generous selection target. |
| Bag | かばん | A logical container with a clearly visible item summary. |
| Box | 箱 / はこ | A logical container and reference object. |
| Key | 鍵 / かぎ | Request, move, and follow an ordered instruction. |
| Umbrella | 傘 / かさ | Preferences and context-rich discussion. |

The catalog owns each object's ID, Japanese display forms, reading, English meaning, applicable counters, and allowed actions. Keep a maximum of 16 visible instances in the P0 scene. Use generic quantity language until a reviewed counter is explicitly targeted.

Containment is a game relation with visual feedback: an item can be placed into a bag or box, and its thumbnail remains inspectable. Left and right refer to the practice area's marked orientation, not an unstated speaker viewpoint. Store the task's reference frame and do not change it mid-instruction.

### Controls and comfort

- P0 controller controls support point, select, grab, move, release, give, recenter, and push-to-talk; keyboard input is optional recovery.

- Hand tracking is P1 unless the validated SDK integration makes it equally reliable without delaying voice or learning.

- The play area works seated or standing and can be repositioned, resized, or recalled within comfortable reach.

- No required movement toward real furniture, locomotion, crawling, or reaching outside the user's configured play space.

- Speech panels have a solid or sufficiently opaque backing, scalable type, clear Japanese glyphs, and separate controls for Japanese text, readings, romaji, and meaning.

- Use readable Japanese fonts for text next to the object images. Color is accompanied by text or another cue where it determines a task.

- Keep listening, processing, speaking, and connection states visible. Add restrained haptics and satisfying object sounds without masking Sensei's speech.

---
[PRD index](README.md)
