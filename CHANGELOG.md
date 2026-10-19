# Changelog

## Unreleased

- Added Lava Flow v1 with the original thin flowing color strip and renamed the blob effect Lava Flow v2. Each button toggles its effect off on a second click; selecting a version switches off the other.

- Limited zoom to 100–400%, enabled navigation by clicking the full left/right image side zones, and added double-click to reset zoom and pan. Side-zone drags still pan; zoomed single clicks wait for the system double-click interval so resetting zoom cannot accidentally navigate.

- Fixed bottom controls and Lava Flow clipping in maximized windows by compensating for resize-frame overflow on all four work-area edges. Added a bottom-bar visibility regression with an active desktop menu overlay.

- Unified viewer tools, slideshow toggle, zoom/fullscreen controls, and right-side library actions with the rectangular edge-aligned window controls. Bar buttons use full-height hit areas and consistent hover, pressed, focus, and toggle states.

- Fixed the maximized library header being covered by desktop menu replacements. Both library and viewer respect the actual top work area and visible topmost menu strips; fullscreen retains zero inset. Added a simulated menu-bar regression covering library, fullscreen entry, and overlay removal.

- Made window controls rectangular, 46 pixels wide, full bar height, and flush with the top/right edges. Added a red close-button hover state and a charcoal top bar distinct from the image background.

- Removed the forced 32-pixel fullscreen top gap. Fullscreen content now reaches all monitor edges, while normal and maximized windows respect the reported work area; added edge-alignment checks through repeated fullscreen toggles.

- Rotated the upright-lamp conditions into the strip: warm left base, cool right top, rightward hot-wax ascent, leftward cool-wax descent, rotated reservoir and backlight. Added tests for the axial thermal force, end temperatures, and full thermal reversal.

- Reduced initial wax bodies by roughly half and small satellite drops by roughly two thirds; raised display pace to 1.65x after visual feedback that the previous flow was crowded and too slow.

- Researched real lava-lamp behaviour and removed time acceleration and artificial random forcing; added slow thermal response, cooling-dependent viscosity, modest wax expansion, and wax/carrier drag feedback.
- Added more wax bodies with varied sizes and thermal phases, and checks covering sustained slow convection and thermal reversal.

- Replaced preset Lava Flow paths with a slow thermal particle fluid: pressure, viscosity, surface cohesion, rising/sinking wax, and an advected photo-colored carrier liquid.
- Seeded a different wax composition, heat distribution, and circulation from each photo's pixel content; removed the quarter-area restriction.
- Each photo seeds a different wax composition and carrier-liquid circulation, with smooth palette transitions.
- Matched the slideshow button to the other dark controls with a light icon and subtle active state.
- Bounded animation rendering work and isolated self-test state from the real photo library.

## 1.0.0

- Renamed Photos Plus to Immersion with the approved photo-frame icon.
- Native photo library, favorites, search, folder import and save-copy rotation.
- Cursor-anchored zoom and drag-to-pan with hidden image scrollbars.
- Fullscreen slideshow with shuffle, loop, timing and Lava Flow toggle.
- Animated six-color Lava Flow sampled from the current photo.
- Dark information and menu panels, side-hover navigation arrows.
- Fast direct-photo startup and background thumbnail loading.
- Windows default-app registration, preserving earlier file handlers and library data.
