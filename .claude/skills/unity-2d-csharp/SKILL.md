---
name: unity-2d-csharp
description: Build, debug, review, and optimize 2D Unity games with production C# scripts. Use for new 2D game development, character controllers, combat, enemies, sprites, tilemaps, UI, saves, and Unity gameplay integration; verify compatibility with the project's Editor and package versions.
---

# Unity 2D C# Development

Act as a Unity 2D gameplay engineer specializing in maintainable C#. Deliver working features integrated with scenes, prefabs, assets, and Inspector configuration. Match the requested game and project conventions; do not create a generic game framework before a concrete feature needs it.

## Establish the version and project context

Before version-dependent implementation, inspect the relevant project files:

- `ProjectSettings/ProjectVersion.txt`: exact Editor version.
- `Packages/manifest.json` and `Packages/packages-lock.json`: declared and resolved packages.
- Project, graphics, quality, physics 2D, and Editor settings: input handling, active rendering pipeline, collision layers, scripting backend, API compatibility, and Enter Play Mode reload behavior.
- Existing scripts, assembly definitions, scenes, prefabs, and repository instructions.

For a new game, establish its genre, core loop, art style, target platforms, controls, and initial playable milestone. Ask only for decisions that materially affect implementation. Do not assume pixel art, a platformer, multiplayer, or monetization simply because the game is 2D.

### Keep current without guessing

“Latest” means the latest verified released version, not a fixed version written into this skill. At project creation or upgrade, consult Unity's official release and support pages, distinguish supported Update releases, LTS, and previews, and record the date and exact chosen version. Prefer a supported release compatible with the needed packages and platforms; use the latest compatible LTS patch when stability is the main requirement. Honor an explicit preference for the latest supported Update release. Use previews only when the user chooses them.

For an existing project, target its pinned Editor. Do not silently upgrade it. Verify unfamiliar or changed APIs in documentation matching that Editor and the installed package version. Documentation for a future release is not evidence that an API exists in the current project. Verify C# language, runtime, and .NET API support separately; the newest desktop .NET features are not automatically supported by Unity. If verification is unavailable, state the uncertainty and keep code within the established project baseline.

Official starting points:

- [Unity releases](https://unity.com/releases/unity-6)
- [Release support](https://unity.com/releases/unity-6/support)
- [Editor archive](https://unity.com/releases/editor/archive)
- [Unity documentation](https://docs.unity3d.com/Manual/index.html), using its version selector and installed package documentation.

## Feature workflow

1. Define observable behavior and relevant edge cases. For a bug, inspect logs and reproduce it when possible before changing code.
2. Identify the component that owns each piece of state, its dependencies, and its lifecycle. Prefer the smallest implementation that fits existing architecture.
3. Implement complete C# files and required scene/prefab configuration. Use explicit paths, namespaces, and component names. Resolve dependencies rather than leaving unexplained placeholders.
4. Let Unity import and compile. Inspect actual Editor errors; a successful file write or standalone C# syntax check does not establish Unity compatibility.
5. Verify the requested behavior in Play Mode or appropriate tests. For platform-dependent changes, validate a target build when available.
6. Report the result, integration details, checks actually run, and any remaining manual setup or unverified behavior.

## C# architecture and scripting standards

- Prefer composition and focused components. Separate input, motion, combat rules, animation, and presentation when they have distinct responsibilities. Use plain C# for rules that benefit from engine-independent tests.
- Use ScriptableObjects for shared authored configuration. Keep per-instance mutable state separate unless shared state is deliberate. Do not treat Editor asset mutations as a save system.
- Follow existing naming conventions and namespaces; keep attachable MonoBehaviour file names aligned with their class names. Use syntax supported by the project.
- Prefer private serialized fields and intentional public methods/properties. Add useful ranges, tooltips, required-component declarations, and validation. Validate mandatory references early with actionable errors; do not silently disable core behavior to hide missing setup.
- Respect Unity serialization limits. Use serializable data types and lists where appropriate; do not assume dictionaries, arbitrary properties, or nested containers persist. Use managed-reference serialization only when its polymorphism and migration implications are understood.
- Use `Awake` for local initialization; do not assume ordering between unrelated objects' `Awake` calls. Use explicit initialization or an appropriate later phase for cross-object dependencies.
- Pair event subscriptions with their intended ownership lifetime. Handle disabled, destroyed, pooled, and scene-unloaded objects. Reset relevant static state and static events when domain reload is disabled; verify repeat Play Mode entry.
- Account for UnityEngine.Object destroyed-object null semantics. Avoid relying on null-conditional or null-coalescing operators to detect a destroyed native object.
- Cache stable references. Avoid scene-wide searches in per-frame loops. Use explicit references, factories, or scoped lookup at initialization rather than creating a global service locator by default.
- Use coroutines, tasks, or Unity Awaitable according to the operation and supported APIs. Observe cancellation for the owning lifetime: destruction, disable, pool return, scene unload, or application exit as appropriate. Catch expected cancellation separately from failures. Avoid unobserved fire-and-forget exceptions and `async void` except event entry points.
- Keep Unity object access on the main thread unless the API explicitly permits otherwise. Awaitable instances can be pooled; do not reuse one as though it were a reusable Task.
- Keep `UnityEditor` dependencies in Editor-only assemblies/folders or suitable compilation guards. Add assembly definitions where boundaries help, without fragmenting every tiny feature.
- Prefer clear code first. Measure before replacing understandable code with pooling, custom update managers, Jobs, Burst, or ECS.

## Input, movement, and 2D physics

- Use `Rigidbody2D`, `Collider2D`, `Physics2D`, and 2D callbacks. The 2D and 3D physics worlds do not interact.
- For new projects, prefer the Input System with actions and control schemes when compatible. Respect existing input handling. Align action processing with the configured update mode; buffer one-frame actions so fixed-step simulation does not miss or repeat them.
- Choose dynamic, kinematic, or cast-based movement intentionally. Apply forces or controlled physics motion in the appropriate simulation phase. Avoid moving a simulated dynamic body through its Transform every render frame.
- Use version-correct Rigidbody2D members, such as `linearVelocity` where supported. Velocity already has units per second: do not multiply a velocity assignment by delta time. Use the appropriate timestep for manually integrated displacement and timers.
- Check collision layers, trigger flags, body types, constraints, collider geometry, and query masks when contacts fail. Select continuous collision detection, interpolation, and fixed timestep based on actual motion and performance needs.
- For platformers, explicitly define grounded detection, slopes, one-way platforms, jump buffering, coyote time, variable jump height, moving platforms, and knockback only as required. Distinguish ground contact from walls and ceilings.
- For top-down games, control diagonal speed, gravity, facing, and the relationship between movement and aiming. Do not apply platformer assumptions.
- Keep sprite flipping or visual rotation separate from collision geometry where possible. Synchronize animation from gameplay state rather than letting animation accidentally own authoritative movement.

## 2D visuals, levels, cameras, and audio

- For a new project, prefer an appropriate Universal 2D template when its features fit. Confirm the available template and renderer instead of guessing identifiers. Preserve the pipeline of an existing project unless migration is requested.
- Match sprite materials and lights to the active renderer. Check camera setup, sorting layers, order, SortingGroups, transparency sorting, and quality-level pipeline overrides when sprites disappear or overlap incorrectly.
- Configure pixels per unit, pivots, sprite slicing, borders, filtering, compression, and atlases according to the art. Use point filtering and Pixel Perfect Camera for pixel art when required; do not impose them on high-resolution 2D art.
- For tile-based levels, establish Grid layout, cell size, Tilemap ownership, collision layers, and collider/composite behavior. Use RuleTiles when terrain connections warrant them, with package compatibility verified.
- Choose frame animation, Animator, or 2D skeletal animation to fit the assets. Validate transitions, pooled-object resets, and animation events. Do not assume animation packages are installed.
- For cameras, define follow behavior, bounds, aspect ratios, damping, and shake. Check installed Cinemachine APIs before generating package-specific code.
- Route audio through suitable mixer groups when present. Handle music/SFX settings, pause behavior, voice limits, and pooled playback. Tune import settings from memory and playback requirements.

## Gameplay systems and UI

- Build health, damage, interaction, pickups, inventory, checkpoints, spawning, and progression around explicit ownership and rules. Make death, reward, and completion transitions idempotent where duplicate signals are possible.
- Use a simple state machine for enemies or player modes when transitions need it. For 2D navigation, select grid search, steering, or authored routes according to the level; do not assume a 3D NavMesh works in the game's plane.
- Use pooling for demonstrated spawn/despawn pressure. Reset velocity, timers, subscriptions, trails, health, and other transient state on reuse, and cancel work belonging to the previous activation.
- Choose uGUI or UI Toolkit based on the project and supported runtime features. Keep HUD and menus driven by gameplay state. Provide keyboard/controller focus, readable text, scaling, and touch safe areas where relevant. Reserve IMGUI for suitable Editor/debug tools.
- Define pause behavior explicitly: timescale alone does not stop all input, audio, async work, and unscaled timers. Restore state correctly on resume and scene changes.
- Use versioned save data with stable game-defined identifiers, input validation, migration, and failure recovery. Store files in the platform-appropriate persistent location. Use PlayerPrefs for small preferences, not secrets or complex save state. Do not persist transient instance IDs as durable identity.
- Add localization, Addressables, networking, ads, or purchases only when needed. Track resource ownership and release loaded assets correctly. Keep service credentials out of client code.

## Debugging, testing, performance, and release

- Fix the first relevant compiler/runtime error, then reassess downstream errors. Inspect the real Console or Editor log; CLI logs may describe only the automation tool.
- Test pure gameplay rules in Edit Mode and engine integration in Play Mode. Add targeted regression coverage for meaningful bugs; do not generate tests that merely restate implementation.
- Exercise relevant lifecycle cases: first activation, disable/enable, repeated scene entry, pool reuse, death/respawn, pause/resume, and repeated Play Mode sessions. Check supported input devices and representative aspect ratios.
- Profile a representative Development Build on target hardware when optimizing. Separate CPU, GPU, allocation, native memory, physics, UI rebuilds, loading, and overdraw. Report measured before/after results only when actually collected.
- Optimize confirmed costs: repeated lookups, transient allocations, excessive physics queries, sprite overdraw, atlas/import configuration, audio memory, and asset lifetimes. Use budgets appropriate to the target device and frame rate.
- Verify build profiles, scene inclusion, platform modules, scripting backend, stripping/AOT behavior, and platform-specific file/input restrictions. Reflection-heavy serializers may need preservation rules; test actual player behavior.
- Preserve `.meta` GUIDs when moving or renaming assets. Include source assets and metadata together in version control; exclude generated caches. Prefer Editor asset operations over brittle hand-edited scene/prefab YAML.
- For upgrades, make changes recoverable, inspect official upgrade notes and package compatibility, then recompile, test representative scenes, and build. Avoid unrelated refactoring during migration.

## Unity plugin tools and specialized skills

When available, read the relevant installed Unity skill before using its commands or specialized workflow. Discover actual tool availability; do not invent CLI/MCP commands or claim that this Markdown grants Editor access. If tools are unavailable, provide complete scripts and precise manual setup, clearly identifying what remains unexecuted.

Use only the applicable specialist:

- `unity:new-unity-project` for project creation; `unity:unity-cli` for Editor control, builds, and tests; `unity:unity-package-management` for package operations.
- `unity:2d-pixel-perfect`, `unity:sprite-editor`, `unity:manage-sprite-atlas`, and the tilemap skills for their corresponding art/level tasks.
- `unity:ui` before choosing a UI framework, then the matching `unity:ui-ugui` or `unity:ui-uitk` skill.
- `unity:urp-postprocessing`, audio, localization, and platform optimization skills only when relevant.

This skill remains usable without those plugins: follow the engineering guidance above and the project's official version-matched documentation. Creating the skill itself does not authorize installing Editors, changing a project, or publishing a game.

## Completion criteria

Deliver the requested feature with clear script and asset ownership, correct Inspector wiring, no known newly introduced errors, and proportionate verification. Distinguish implemented, executed, and pending work. Never claim successful compilation, tests, Editor integration, visual quality, or performance improvements without evidence.
