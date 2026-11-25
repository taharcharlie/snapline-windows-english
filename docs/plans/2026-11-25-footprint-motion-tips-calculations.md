# Plan: lighter idle, Basic/Fun motion, compact Tips, calculations in notes

Status: draft for independent review. Nothing is built yet. Nothing gets committed unless the user asks.

## Context and constraints

- Snapline is a WPF app, C# 5 only, built by `build.ps1` with the in-box `csc.exe`.
- **Tests:** they run as exe flags with `--data-dir <empty dir>`: `--self-test`, `--memo-test`, `--hover-test`, `--startup-test`, `--remark-test`, `--interaction-test`, `--integration-test`, `--shortcut-test`, `--preferences-test`.
  - On this PC, startup, remark, interaction and shortcut already fail on untouched code.
  - Integration currently crashes on the clipboard contents.
  - The bar for every phase: results stay the same as the previous build, and the new checks pass.
- **The user's Snapline is usually running.** Build into a scratch folder; deploy to `dist` once at the end, after the user quits it.
- **Pebble constraint:** Pebble finds the shelf as the full-width WPF window of Snapline.exe at the top of a monitor, 4 px tall when collapsed. That shape must not change. Pebble only cares that the open window is taller than 40 px.
- **Defaults must keep today's look and behaviour,** except for the animation default, which the user asked to change to Basic.
- **The user's priorities:** keep it responsive, and reduce the footprint where it's cheap. Heavy optimisation is not wanted.

Measured idle cost, with the line collapsed:

| Copy | CPU | Memory |
|---|---|---|
| The user's copy (2 screenshots, 2 notes) | 0.8% of one core | 174 MB private, 182 MB working set |
| Empty copy | 0.9% of one core | 79 MB private |
| Pebble, for comparison | — | 78 MB private |

## Phase 1: idle footprint, staying responsive

### 1a. Adaptive tick
- **Today:** `Shelf.tick` runs every 80 ms all day (`Shelf.cs`, `tick.Interval`). Each tick calls `GetCursorPos`, `GetForegroundWindow`, `Screen.FromPoint` and the fullscreen check (`HoverPolicy.Suppresses`: class name, DWM bounds).
- **Change:** at the end of `Tick`, choose the next interval.
  - **80 ms** when any of these holds: `expanded`, `opening`, `collapsing`, `dialogOpen`, `dragging`, `FlyingCount > 0`, `lineKeys`, an open preview, a pending undo, a toast showing, or the pointer within 120 DIP of the top of the work area of the screen under it.
  - **400 ms** otherwise.
- **Also:**
  - `Reveal()` sets the fast interval at once.
  - The indicator's `MouseEnter` still opens the line synchronously, so opening never waits for a tick.
  - Hotkeys arrive as window messages, so they are unaffected.
  - The `OpenDelay` dwell needs fast ticks; the pointer is at the top then, so it gets them.
- **Risks:**
  - Things the tick does on a timer get a 400 ms granularity while idle: undo expiry, auto-clear, toast expiry, the trigger-length refresh every 30 s. That's acceptable.
  - Collapse-on-leave only runs while expanded, so it stays fast.

### 1b. Start-up without decoding every screenshot
- **Today:** the `Store` constructor calls `Load` on each screenshot (a full decode), then `Hash` (the full pixel array) and `Thumbnail`.
- **Change:**
  - Add `[DataMember] string Stamp` to `Shot`: the file length and last-write time.
  - If the stamp matches, keep the saved `Digest` and decode only a thumbnail, through `BitmapDecoder` with a decode size of `DecodePixelWidth/Height` ≤ 480. That needs a `Store.LoadThumb(path, 480)`.
  - If the stamp is missing or differs (the file was edited outside Snapline), do exactly what happens today, then save the stamp.
  - Write the stamp in `Add`/`Import` too.
- **Duplicate detection doesn't change:** the digest is still a pixel hash.

### 1c. Hand memory back after the line closes
- **When:** 4 s after a collapse has finished, if the line is still collapsed and no dialog or editor is open.
- **What:** set `GCSettings.LargeObjectHeapCompactionMode = CompactOnce`, then run `GC.Collect()` and `GC.WaitForPendingFinalizers()`, once per collapse.
- **What it won't do:** no working-set trimming, because that would add page faults on the next open.

### Done when
- Idle CPU is ≤ 0.25% of a core.
- Private memory with the user's data is about 100–120 MB after one open and close.
- The hover test passes: the blue line still opens synchronously.
- A scripted reveal after 60 s of idle takes as long as today, within noise.

## Phase 2: animation becomes a choice

### Settings
- **Remove the "Cards sway" Auto/Always/Never control.** Add to `Preferences`:
  - `[DataMember] int Animation`: 0 = Basic (default), 1 = Fun.
  - `[DataMember] bool FunOnBattery`: false (the default) means "reduce animation on battery" is on.
  - `[DataMember] int AnimationRevision`: for a one-time migration.
- **Migration, once, when `AnimationRevision < 1`:**
  - `Motion == 1` (Always) → Fun, with `FunOnBattery = true`.
  - Anything else → Basic.
- **Theme:** `Theme.Fun` replaces `Theme.Calm` and `Theme.MotionMode`.
  - It is true when Animation is Fun and not (on battery and not `FunOnBattery`).
  - The battery state is cached for 5 s.
  - `SystemParameters.ClientAreaAnimation` is no longer consulted.
  - `ForceLively` (tests) forces Fun, and `--calm` forces Basic.

### What each mode does

| Today's use of `Theme.Calm` | Basic | Fun |
|---|---|---|
| `HoverEffects.SetHangingAnimation`: sway | Cards hang still at their lean; no forever-sway clocks are created at all, which saves the idle-open render cost | Phase 3 physics |
| `ShelfMotion` falling paper | No fall. The card fades out over 150 ms, then it's removed | The falling paper, as today |
| `ShelfLine.GlideTo`: scroll glide | Glides (cheap, and both modes keep it) | Glides, plus the cards swing |

- **Both modes keep:** the open and close slide, the press sink, the focus scale and the hold bar.
- **Frame rate:** `Timeline.SetDesiredFrameRate(…, 24)` on the sway goes away. In Fun, the physics runs on `CompositionTarget.Rendering`, the display's own frame rate.

### Preferences → Misc → Motion
- **Animation:** Basic / Fun, with the note "Fun: cards swing like clothes on a line."
- **Reduce animation on battery:** a switch, on by default.
- **Open after resting on the line:** stays.

### Tests
- Update `PreferencesTests` (the motion checks).
- Add migration checks: Always → Fun and on-battery, Never/Auto → Basic.
- `--calm` keeps working.

## Phase 3: Fun mode, cards hanging like clothes

- **A pendulum per card.** Each `LineCard` gets one: angle θ and angular velocity ω, around its peg (the existing `RotateTransform swing`, centre (w/2, −15)).
  - The rest angle is the card's lean.
  - Spring constant k with damping c, under-damped (ζ ≈ 0.25). A 2° kick settles in about 1.2 s.
  - |θ| is clamped to 8°.
- **One loop.** A single `CompositionTarget.Rendering` handler runs while the line is open in Fun. It stops when the line collapses, or after 2 s with every card at rest and no gust due. While it's stopped, each card simply holds its rest angle.
- **What drives the swing:**
  - **The pivot moving:** horizontal acceleration of the card's peg. During a glide it comes from `view`'s second difference times `SlotWidth`; while dragging (reorder), from the pointer. The torque is −a·cos θ / L.
  - **Pointer brushing:** the pointer crossing a card's top third while moving faster than 300 DIP/s gives a small impulse in the direction of travel.
  - **Wind:** every 9–20 s (random), a gust travels across the line at 900 DIP/s and gives each card a small impulse as it passes.
  - **Hanging a new card** (a deck count increase seen in `Render`): the new card drops 24 DIP onto the line with a damped spring on Y, and a slight swing.
  - **Hover:** the existing "straighten on hover" becomes damping the target to 0 while the pointer is on the card.
- **Replaces** the `DoubleAnimation` breeze, but only in Fun.
- **Kept:** `PauseHoverEffects` and `ClearHoverEffects` keep working; the physics state lives on `LineCard` and is rebuilt in `Render`.
- **Not in scope:** a rope dip (the rope geometry stays static), and per-card rope tension.
- **Tests:**
  - Unit-test the integrator: a kick settles under 0.2° within 1.5 s and never exceeds 8°.
  - Render a snapshot mid-swing to look at.

## Phase 4: compact Tips card

- **Setting:** `[DataMember] int TipsCard`: 0 = Full (default), 1 = Compact. It goes in Preferences → Look.
- **Layout:** today the Tips card takes a full slot. `HangingCenter(i, n)` assumes n+1 equal slots.
  - The Tips slot becomes `TipsSlot`, equal to `SlotWidth` (Full) or `round(64 * CardScale) + 36` (Compact).
  - `HangingCenter`, `CardsPerPage` (`(shelfWidth − 112 − TipsSlot) / SlotWidth`) and `RopeNodes` (the last node at the Tips centre) use it.
  - Reordering (`ShelfGestures`, the slot maths uses `HangingCenter(0, count)` and `SlotWidth`) is unaffected for cards.
- **Compact card:**
  - Width `round(64 * CardScale)`, height as Full.
  - A large "?", then ✂ (capture, copy only) and ＋ (new note) as square buttons, with the corner brackets kept.
  - Resting on it opens the same help popup, re-centred with `HorizontalOffset = (width − 306)/2`.
- **Bin while dragging:** when a card is pulled down (the bin face shows), the compact card's bin face is drawn at full Tips size, extending to the left with a negative margin, so the drop target is as large as today.
  - WPF hit-tests children outside their parent's bounds when nothing clips them, so the drop events still reach the card.
  - Verify this in the interaction test or by hand.

## Phase 5: calculations in notes (like Antinote)

### Model: new `src/NoteMath.cs`, a pure function with no WPF
- **Modes:** `NoteMath.ModeOf(text)` returns None / Math / Sum / Avg. The first line, trimmed and case-insensitive, must be exactly `math`, `sum` or `avg`.
- **Results:** `NoteMath.Evaluate(text)` returns a result string (or null) for each line, plus an optional total line.
- **Math lines:**
  - **Parser:** recursive descent over numbers (invariant `.`; a comma never counts as a decimal mark, and `1,234` is read as 1234 only when the groups are exactly 3 digits).
  - **Operators:** `+ - * / ^`, unary minus, parentheses, postfix `%` (meaning /100), and `a% of b`.
  - **Functions:** `sqrt abs round floor ceil sin cos tan log ln exp min max`. Constants: `pi e`.
  - **Variables:** `name = expr` defines a variable for later lines.
  - **A line already ending `= <number>`** (autocompleted) shows no second result.
- **Conversions, in any mode:** `<expr> <unit> (in|to) <unit>`.
  - **Units:** length, mass, volume (including cups, tsp, tbsp, fl oz, gal), area, speed, time, data (B/KB/MB/GB/TB as 1000s and KiB/MiB/GiB as 1024s), and temperature (C, F, K, including °).
  - **Mismatched kinds** (`5 kg in m`) give no result. That's not an error; the line just stays plain.
- **Sum/avg lines:** the last number on each line counts (`coffee 4.50` gives 4.50). Lines without a number are skipped. The total line reads `Sum: 12.5` / `Average: 4.17`.
- **Formatting:** up to 6 significant decimals with trailing zeros trimmed, invariant culture, thousands separated by a thin space.
- **Hard limits** so a note can't hang the app: 200 lines, 500 characters per line, 64 levels of nesting.

### Display
- **On the card:** in `NoteMarkup.Fill`, for a calc note, after each line with a result append a Run `"  = 84"` in `Theme.Mid`, then the total line in `Accent`. The keyword line is shown dimmed.
- **In the editor:**
  - An overlay `Canvas` over the TextBox, with a TextBlock for each result.
  - Each result sits at the right end of its line, using `GetRectFromCharacterIndex` on the line's last character.
  - It refreshes on `TextChanged`, on scroll and on resize, and is not hit-testable.
- **Autocomplete, in the editor in a calc note, caret at the end of a line that has a result:**
  - Typing `=` inserts `= 84` (the `=` plus the result).
  - `Tab` inserts ` = 84`.
  - Anywhere else, `Tab` keeps its normal behaviour.

### Copy
- `CopyNote` copies the text with results filled in for lines that don't already have them, plus the total line for sum and avg.
- The marks are stripped as now.

### Tests
- A new `--math-test` with a table of expressions → expected strings: operator precedence, percent, variables, every unit kind, temperature offsets, sum/avg parsing, the limits, non-calc notes unchanged.
- Editor checks:
  - `=` and `Tab` autocomplete;
  - copy output;
  - the card rendering inlines for one note (as in `MemoTests` today).

## Delivery
- **Per phase:**
  - build to scratch;
  - run every test flag except shortcut and integration (they show a dialog on failure on this PC) and compare with the previous build;
  - render pictures of the visual changes and look at them.
- **README:** a Preferences section (Animation, Reduce on battery, Tips card), a Notes section (calculations), and a "footprint" line.
- **At the end:** the user quits Snapline; build into `dist`, restore `snapline.ico`, and relaunch with `--startup`.

## Open questions for the reviewer

1. Is 400 ms safe for every tick duty while collapsed?
2. Does the GC call after collapse risk any jank?
3. Is the LineCard rebuild in `Render` compatible with persistent pendulum state? A card's state is lost on every redraw; is that acceptable, given redraws happen on deck change, glide crossings and focus?
4. Is the negative-margin bin really hit-testable for drag-and-drop in WPF?
5. Do the decimal parsing rules clash with any common way people write numbers in notes?

## Changes after the independent review (2026-11-25)

The review checked the plan against the code. These changes supersede the sections above where they differ.

- **Fixed first:** a bug in the glide. `lastFrame = now;` had ended up inside a trailing comment, so frame times grew all through a glide and glides ended abruptly.
- **Phase 1a, tick:**
  - The next interval is chosen in a `finally` around `Tick`, so the early returns can't skip it. `tick.Interval` is set only when it changes.
  - **Fast set:** `expanded`, `opening`, `collapsing`, `dragging`, `FlyingCount > 0`, `pending.Count > 0`, or the pointer within 120·dpi px of the top of the work area.
  - The indicator's MouseEnter switches to the fast tick at once, so the OpenDelay dwell is accurate.
  - The per-tick fullscreen check runs only while expanded; `TryRevealFromIndicator` checks fullscreen itself.
  - **Measure:** process CPU time over 5 minutes, and a real pointer hover (`SetCursorPos`) with OpenDelay 0 and above 0.
- **Phase 1b, start-up:**
  - The stamp is `LastWriteTimeUtc.Ticks` plus the length.
  - The thumbnail comes through `Store.Preview`, clamped to the native size, with the 64 MB and 50 MP checks kept.
  - `Store.Refresh` skips work when the stamp matches. That also removes the redraw that follows every capture.
  - The constructor saves only when it added stamps.
- **Phase 1c, memory:**
  - One `GC.Collect` after the Store loads, then again 4 s after a collapse, only if the line was opened since the last one and it is still collapsed with no editor open.
  - No LOH compaction unless a measurement shows it's needed.
  - **Also:** the note preview loads its picture through `Store.Preview` with a size cap, and drops it when the preview closes.
- **Phase 2:**
  - **Basic still glides**, and the calm-skips-the-glide test is updated to match.
  - Basic keeps today's instant removal, with no fade.
  - Basic still registers each hanging motion, but holds the lean as a plain value instead of a forever clock.
  - `Motion` stays a DataMember so the migration can read it. The migration lives in the Store constructor, next to `CaptureShortcutRevision`.
  - Windows' animation setting is not consulted; the user asked for that.
  - Everything that still mentions sway, Calm or MotionMode gets updated: README, tray Help, Preferences, the tests (`Pref-Motion-*` becomes `Pref-Animation-*`).
- **Phase 3:**
  - Pendulum state lives in a `Dictionary<object, Pendulum>` keyed by deck item, plus one for Tips. `Dress` attaches it and `Render` prunes it, so redraws don't reset a swing.
  - In Fun, any clock on `swing.Angle` is cleared before the physics writes to it.
  - The integrator runs at a fixed step of at most 60 Hz.
  - It respects `hoverEffectsPaused`.
  - The peg's acceleration comes from the second difference of its actual left edge between frames, so one path covers glides, drags and reorders.
  - The sway is created in five places (notes, screenshots, links, Tips, HoverEffects); all of them change.
  - **Drop-in:** a new card gets a dedicated `TranslateTransform` in the group built by `Dress`, not `Canvas.Top` or Press. "This card is new" comes from a set of known items, not from `Render`'s `force`.
  - **Pointer brushing:** a small impulse from horizontal pointer speed on MouseMove, limited.
- **Phase 4:**
  - Add `TipsCenter(n)`, and set `HangingCenter`'s origin to `(W − n·S − T)/2`.
  - Cards gliding past the last slot fade out within half a slot, so none shows beside a narrow Tips card.
  - The ＋ button keeps the AutomationId `NewNote`.
  - The bin face extends with a negative margin; the review confirms WPF hit-tests it.
- **Phase 5:**
  - Conversions only in calc notes. Calc lines are evaluated raw, with no `NoteMarkup` on those lines.
  - **Commas:**
    - Inside a function's parentheses, a comma separates arguments.
    - `1,234` groups only when every group is exactly 3 digits and it isn't inside a function.
    - In sum/avg, a line with an ambiguous comma (`2,50`) doesn't count.
    - A month name before the last number (`Nov 25`) doesn't count.
  - **Units:** `in` is a unit only right after a number. Cups are written `cup`/`cups`, never `c`. `min` is minutes and `m` is metres. Bytes are `B`, `KB`, …; there are no bits.
  - **Autocomplete:**
    - `=` goes through `PreviewTextInput`.
    - It only fills in when the line is a plain expression, not a `name =` assignment.
    - Inserted results and copied text use plain digits; the thin space is for display only.
    - Inserts respect `MaxNoteText`, and line ends handle CRLF.
  - **Results** show on the card and in the hover preview (both use `Fill`). Drag-out stays raw text.
  - **Tests:** the math table goes in `--memo-test`, not a new flag.
- **Pebble:** no phase touches `Position`, so the collapsed window keeps its shape. The drop-in and the bin stay inside the canvas.
