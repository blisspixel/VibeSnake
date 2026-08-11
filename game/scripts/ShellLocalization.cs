using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VibeSnake.Game;

internal enum ShellLocale : byte
{
    English = 0,
    Pseudo = 1,
}

internal readonly record struct ShellTextArgument(string Name, string Value)
{
    public static ShellTextArgument From(string name, object value) =>
        new(name, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
}

internal readonly record struct ShellTextReference(
    string Id,
    IReadOnlyList<ShellTextArgument> Arguments)
{
    public static ShellTextReference Create(
        string id,
        params ShellTextArgument[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(arguments);
        return new ShellTextReference(id, arguments);
    }
}

internal sealed record ShellTextEntry(
    string Id,
    string EnglishTemplate,
    IReadOnlyList<string> Parameters);

/// <summary>
/// Stable English copy IDs and a deterministic pseudo-locale for layout and
/// glyph qualification. English is the only required player locale for 1.0.
/// </summary>
internal static class ShellLocalization
{
    public const string CatalogId = "shell-copy-v1";
    public const string EnglishLocaleId = "en";
    public const string PseudoLocaleId = "qps-ploc";
    public const int MaximumFormattedCharacters = 2_048;

    private static readonly Regex IdPattern = new(
        "^[a-z0-9]+(?:[.-][a-z0-9]+)*$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex ParameterPattern = new(
        "\\{([a-z][a-z0-9_]*)\\}",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly ShellTextEntry[] Entries =
    [
        Entry("app.title", "VIBE SNAKE"),
        Entry("app.tagline", "RETRO CORE  //  MODERN FLOW"),
        Entry("menu.start", "START RUN"),
        Entry("menu.customize", "CUSTOMIZE"),
        Entry("menu.achievements", "ACHIEVEMENTS"),
        Entry("menu.high-scores", "HIGH SCORES"),
        Entry("menu.quit", "QUIT"),
        Entry("menu.action.start", "start selected mode"),
        Entry("menu.action.previous", "previous mode"),
        Entry("menu.action.next", "next mode"),
        Entry("menu.replay-drop", "Drop one replay file here to verify without changing it"),
        Entry(
            "menu.accessibility-shortcuts",
            "F4 flash  F5/F6 text  F7 mute  -/= volume  F8 restore  F9-F11 accessibility  F12 logs"),
        Entry("screen.onboarding.title", "LEARN VIBE SNAKE"),
        Entry("onboarding.practice", "UNSCORED INTERACTIVE PRACTICE"),
        Entry("onboarding.offer.summary", "Eight short action lessons teach the complete arcade loop."),
        Entry(
            "onboarding.offer.isolation",
            "Tutorial scores, achievements, and replays are always disabled."),
        Entry(
            "onboarding.offer.learn-description",
            "Interactive turning, wrapping, food, hunger, Shield, pause, and restart."),
        Entry(
            "onboarding.offer.skip-description",
            "Remember this choice and start a normal scored run immediately."),
        Entry("onboarding.connected-edges", "CONNECTED EDGES"),
        Entry("screen.settings.title", "SETTINGS"),
        Entry("settings.select-section", "SELECT A SECTION"),
        Entry("settings.player-data.operation", "PLAYER-DATA OPERATION IN PROGRESS"),
        Entry(
            "settings.player-data.operation-help",
            "The game remains responsive. Quit waits for this operation to finish safely."),
        Entry("settings.playtest.delete-title", "PERMANENTLY DELETE LOCAL PLAYTEST FACTS?"),
        Entry(
            "settings.playtest.delete-help",
            "No backup is created. No upload or remote copy exists."),
        Entry("settings.reset.title", "BACK UP, VERIFY, THEN RESET?"),
        Entry(
            "settings.reset.targets-help",
            "Only these exact player-data targets will be removed:"),
        Entry("settings.reset.target", "[ ] user://{target}"),
        Entry(
            "settings.reset.backup-help",
            "A SHA-256 verified backup is completed before removal."),
        Entry("settings.reset.backup-location", "Backup location: user://backups/{backup}"),
        Entry("settings.navigation.sections", "Up/Down select section"),
        Entry(
            "settings.navigation.items",
            "Up/Down select  Left/Right adjust  F8/Select restore section"),
        Entry("settings.backup.location", "Location: user://{location}"),
        Entry("settings.backup.categories", "Categories: {categories}"),
        Entry("settings.backup.navigation", "Left/Right selects another bounded backup"),
        Entry("screen.bindings.title", "INPUT BINDINGS"),
        Entry("bindings.restore-defaults", "  RESTORE_DEFAULTS"),
        Entry("bindings.navigation", "Left/Right device  Up/Down select"),
        Entry("screen.progression.title", "PROGRESSION GOALS"),
        Entry(
            "progression.explainer",
            "Every goal shows its rule, progress, Vibe eligibility, and expression reward."),
        Entry("screen.tour.title", "BROADCAST TOUR"),
        Entry(
            "tour.summary",
            "FINITE OFFLINE CIRCUIT  {completed}/{total} CLEARED  PAGE {page}/{pages}"),
        Entry(
            "tour.practice-notice",
            "Fixed-seed practice. No score submission, schedule, currency, or mechanical reward."),
        Entry("tour.navigation", "Up/Down event  Left/Right page"),
        Entry("tour.action.start", "start or same-seed rematch"),
        Entry("tour.action.back", "progression goals"),
        Entry("screen.cosmetics.title", "CUSTOMIZE YOUR SNAKE"),
        Entry(
            "cosmetics.summary",
            "{total} AUTHORED SETS  |  {earned} EARNED  |  {saved}/{slots} LOADOUTS  |  PAGE {page}/{pages}"),
        Entry(
            "cosmetics.isolation",
            "Presentation only: no hitbox, movement, score, spawn, power, AI, or input changes."),
        Entry(
            "cosmetics.navigation",
            "Up/Down set  Left/Right page  [E] equipped  [S] saved  [-] locked"),
        Entry("screen.scores.title", "LOCAL SCORES"),
        Entry(
            "scores.category-policy",
            "TOP TEN PER EXACT RULES, MODE, PURPOSE, SEED, DDA, AND CONFIG CATEGORY"),
        Entry("scores.empty", "NO SCORED NATIVE RUNS OR LEGACY IMPORTS YET"),
        Entry(
            "scores.empty-help",
            "Finish a normal run, or use the explicit legacy import below."),
        Entry("screen.content-packs.title", "CONTENT PACKS"),
        Entry("content-packs.core-ready", "CORE READY: REQUIRED AND PROTECTED"),
        Entry(
            "content-packs.optional-status",
            "OPTIONAL RADIO PACKS READY: {count}"),
        Entry(
            "content-packs.offline-help",
            "The complete game remains playable offline without radio packs."),
        Entry(
            "content-packs.storage-help",
            "Drop approved .vibesnake-pack.zip here to install it."),
        Entry(
            "content-packs.removal-help",
            "Removal always requires confirmation and targets one pack only."),
        Entry(
            "status.content-packs.import-paused",
            "FILE IMPORT PAUSED: RETURN TO THE MENU OR FINISH THE RUN"),
        Entry(
            "status.content-packs.inventory-unavailable",
            "RADIO PACK IMPORT UNAVAILABLE: CONTENT INVENTORY NOT READY"),
        Entry(
            "status.content-packs.ready",
            "RADIO PACK READY: {name}"),
        Entry(
            "status.content-packs.rejected",
            "RADIO PACK NOT INSTALLED: {reason}"),
        Entry(
            "content-packs.retention-help",
            "Saves, profiles, achievements, preferences, and replays are retained."),
        Entry(
            "content-packs.isolation-help",
            "Malformed, incompatible, tampered, or duplicate optional packs are isolated."),
        Entry("screen.replays.title", "VERIFIED REPLAYS"),
        Entry(
            "replays.integrity-help",
            "Local only. Loading revalidates integrity and deterministic outcome."),
        Entry("replays.empty", "NO SAVED REPLAYS"),
        Entry("screen.comparisons.title", "OFFLINE COMPARISONS"),
        Entry(
            "comparisons.summary",
            "Four fixed household rival slots. Verified ghosts use exact local rules and seeds."),
        Entry(
            "comparisons.inbox",
            "Explicit import source: user://imports/household-rival.vibesnake-replay.json"),
        Entry(
            "comparisons.slot-row",
            "{marker} {name}  [{state}]  {mode}  SCORE {score}"),
        Entry("comparisons.slot-detail", "STATUS {code}: {message}"),
        Entry("comparisons.seed", "SEED CODE {code}"),
        Entry(
            "comparisons.help",
            "Imports preserve the source. Delete requires fresh exact confirmation."),
        Entry(
            "comparisons.ghost-hud",
            "GHOST SLOT {slot}  SCORE {score}  DELTA {delta}  LENGTH {length}"),
        Entry("action.offline-comparisons", "offline comparisons"),
        Entry("action.import-ghost", "import selected slot"),
        Entry("action.race-ghost", "race verified ghost"),
        Entry("action.export-run-card", "export run card"),
        Entry("action.delete-ghost", "delete selected ghost"),
        Entry("menu.action.spectator", "AI channels and same-seed rivalries"),
        Entry("screen.spectator.title", "AI BROADCAST CIRCUIT"),
        Entry("spectator.selection.channel", "CHANNEL  {channel}"),
        Entry("spectator.selection.rivalry", "RIVALRY  {rival}"),
        Entry("spectator.selection.rules", "RULES  {rules}"),
        Entry("spectator.selection.seed", "SEED  {seed_class} / SLOT {slot}"),
        Entry("spectator.selection.exact-seed", "EXACT SEED  {seed}"),
        Entry("spectator.selection.speed", "SPEED  {speed}"),
        Entry("spectator.selection.explanation", "EXPLANATION  {level}"),
        Entry("spectator.selection.prediction", "PREDICTION  {prediction}"),
        Entry(
            "spectator.selection.instructions",
            "Up/Down option  Left/Right change  Confirm starts an equal-rules local match"),
        Entry(
            "spectator.selection.safety",
            "Predictions are informational. No currency, wagering, or human progression reward."),
        Entry(
            "spectator.overlay.channel",
            "{channel} VS {rival}  |  {station}  |  {shed}"),
        Entry(
            "spectator.overlay.target",
            "TARGET {target}  |  RISK {risk}  |  VIBE {vibe}"),
        Entry(
            "spectator.overlay.resources",
            "SURVIVAL {resources}  |  RECORD DELTA {delta}"),
        Entry("spectator.overlay.reason", "WHY {reason}"),
        Entry(
            "spectator.overlay.match",
            "STEP {step}/{limit}  |  {state}  |  {speed}"),
        Entry(
            "spectator.standing-row",
            "{rank}. {channel}  W {wins}  AVG {average}  BEST {best}  MILESTONES {milestones}"),
        Entry("action.ai-channels", "LET'S PLAY / AI CHANNELS"),
        Entry("action.start-broadcast", "start broadcast"),
        Entry("action.switch-channel", "switch viewed rival"),
        Entry("action.seed-challenge", "challenge exact seed"),
        Entry("action.explanation-level", "explanation level"),
        Entry("action.league-standings", "local league standings"),
        Entry("status.spectator.started", "BROADCAST STARTED: {channel} VS {rival}"),
        Entry("status.spectator.paused", "BROADCAST PAUSED"),
        Entry("status.spectator.complete", "BROADCAST COMPLETE: SEED CHALLENGE READY"),
        Entry("status.spectator.saved", "LOCAL LEAGUE RESULT SAVED"),
        Entry("status.spectator.save-failed", "LEAGUE SAVE FAILED: RESULT IS SESSION-ONLY"),
        Entry("status.spectator.challenge-started", "EQUAL-RULES SEED CHALLENGE STARTED"),
        Entry("spectator.reason.advance-food", "closing on visible food"),
        Entry("spectator.reason.advance-power", "rerouting toward a visible power"),
        Entry("spectator.reason.preserve-options", "keeping multiple exits open"),
        Entry("spectator.reason.continue-course", "holding a legal course"),
        Entry("spectator.reason.escape-hazard", "leaving nearby body danger"),
        Entry("spectator.reason.bounded-chaos", "taking a bounded unpredictable turn"),
        Entry("spectator.reason.recover-stall", "breaking a stalled visible target"),
        Entry("spectator.commentary.fallback", "Caption signal recovered. The run continues unchanged."),
        Entry("spectator.commentary.speed-demon.run-start", "Redline: Open the route. I will spend every clear cell."),
        Entry("spectator.commentary.speed-demon.food", "Redline: Food secured. The next lane is already moving."),
        Entry("spectator.commentary.speed-demon.power", "Redline: A recovery tool means the narrow line stays open."),
        Entry("spectator.commentary.speed-demon.pressure", "Redline: Tight window. Keep the exit in frame."),
        Entry("spectator.commentary.speed-demon.terminal", "Redline: Route closed. Keep the seed and measure the gap."),
        Entry("spectator.commentary.coward.run-start", "Shelter Coil: First protect the exits, then ask for points."),
        Entry("spectator.commentary.coward.food", "Shelter Coil: Clean food, with room left to leave."),
        Entry("spectator.commentary.coward.power", "Shelter Coil: Protection belongs between danger and the route."),
        Entry("spectator.commentary.coward.pressure", "Shelter Coil: Crowding detected. I am rebuilding space."),
        Entry("spectator.commentary.coward.terminal", "Shelter Coil: The safe line ended. The evidence remains."),
        Entry("spectator.commentary.greedy.run-start", "Crownchaser: Every clean bite is pressure on the record."),
        Entry("spectator.commentary.greedy.food", "Crownchaser: Value held. Keep the combo lane alive."),
        Entry("spectator.commentary.greedy.power", "Crownchaser: Use the mutation only if it protects the pace."),
        Entry("spectator.commentary.greedy.pressure", "Crownchaser: The record line is narrow, not invisible."),
        Entry("spectator.commentary.greedy.terminal", "Crownchaser: Mark the score. The same seed can answer back."),
        Entry("spectator.commentary.power-hunter.run-start", "Mutagenist: Watch the board for the first useful change."),
        Entry("spectator.commentary.power-hunter.food", "Mutagenist: Food stabilizes the next mutation route."),
        Entry("spectator.commentary.power-hunter.power", "Mutagenist: Mutation acquired. Recalculate the whole lane."),
        Entry("spectator.commentary.power-hunter.pressure", "Mutagenist: Pressure changes which resource matters now."),
        Entry("spectator.commentary.power-hunter.terminal", "Mutagenist: This form is finished. Preserve the seed."),
        Entry("spectator.commentary.drunk.run-start", "Noise Coil: Legal routes can still refuse to be predictable."),
        Entry("spectator.commentary.drunk.food", "Noise Coil: A clean bite inside an untidy signal."),
        Entry("spectator.commentary.drunk.power", "Noise Coil: New tool, new rhythm, same collision rules."),
        Entry("spectator.commentary.drunk.pressure", "Noise Coil: Static rising. The turn still has to be legal."),
        Entry("spectator.commentary.drunk.terminal", "Noise Coil: Signal dropped. No mystery, no hidden immunity."),
        Entry("spectator.commentary.optimal.run-start", "The Proof: Repeatable choices begin with visible facts."),
        Entry("spectator.commentary.optimal.food", "The Proof: Target progress confirmed without closing the exits."),
        Entry("spectator.commentary.optimal.power", "The Proof: The power earns its detour through expected utility."),
        Entry("spectator.commentary.optimal.pressure", "The Proof: Reduce exposure, preserve the next decision."),
        Entry("spectator.commentary.optimal.terminal", "The Proof: Result recorded. Reproduction is available."),
        Entry("spectator.commentary.yolo.run-start", "Edge Prophet: The boundary is a route, not a warning label."),
        Entry("spectator.commentary.yolo.food", "Edge Prophet: Food taken at the pace this edge deserves."),
        Entry("spectator.commentary.yolo.power", "Edge Prophet: A power is permission to test a sharper line."),
        Entry("spectator.commentary.yolo.pressure", "Edge Prophet: Danger visible. The choice is still mine."),
        Entry("spectator.commentary.yolo.terminal", "Edge Prophet: The edge answered. Challenge it under your rules."),
        Entry("spectator.commentary.balanced.run-start", "Meanline: Read food, powers, and exits before choosing a priority."),
        Entry("spectator.commentary.balanced.food", "Meanline: Food secured without abandoning the next option."),
        Entry("spectator.commentary.balanced.power", "Meanline: Resource gained. Balance the route around it."),
        Entry("spectator.commentary.balanced.pressure", "Meanline: Risk is rising, so survival owns this decision."),
        Entry("spectator.commentary.balanced.terminal", "Meanline: The comparison is complete. Keep the seed honest."),
        Entry("spectator.commentary.wall-hugger.run-start", "Rimkeeper: The rim is connected infrastructure. Use all of it."),
        Entry("spectator.commentary.wall-hugger.food", "Rimkeeper: Food collected while the boundary route stays useful."),
        Entry("spectator.commentary.wall-hugger.power", "Rimkeeper: Carry the tool along the edge until it earns a turn."),
        Entry("spectator.commentary.wall-hugger.pressure", "Rimkeeper: The rim is crowded. Find the next wrap window."),
        Entry("spectator.commentary.wall-hugger.terminal", "Rimkeeper: Boundary route closed. The map is still exact."),
        Entry("spectator.commentary.zen-master.run-start", "Stillwater: Keep the exits open and let the clean route appear."),
        Entry("spectator.commentary.zen-master.food", "Stillwater: Food arrives without spending future choices."),
        Entry("spectator.commentary.zen-master.power", "Stillwater: Hold the resource until the route asks for it."),
        Entry("spectator.commentary.zen-master.pressure", "Stillwater: Do not panic. Preserve one more clean opening."),
        Entry("spectator.commentary.zen-master.terminal", "Stillwater: The route rests here. The seed remains available."),
        Entry("screen.lore.title", "COIL ARCHIVE"),
        Entry("lore.summary", "{unlocked}/{total} OPEN  |  DEPTH {depth}"),
        Entry("lore.safety", "Optional context only. Controls, danger, scoring, and death remain direct."),
        Entry("lore.entry-row", "{marker} {title}  [{kind}]"),
        Entry("lore.detail-meta", "DEPTH {depth}  |  CANON {canon}  |  TYPE {kind}"),
        Entry("lore.locked.reward", "LOCKED: earn expression reward {reward}"),
        Entry("lore.locked.milestone", "LOCKED: record spectator milestone {milestone}"),
        Entry("lore.locked.replay", "LOCKED: retain {count} local replay echoes"),
        Entry("lore.navigation", "Up/Down entry  Left/Right depth  Back returns to AI channels"),
        Entry("action.lore-archive", "optional Coil archive"),
        Entry("lore.entry.station-flow-signal.title", "THE FLOW SIGNAL"),
        Entry("lore.entry.station-flow-signal.body", "The Ministry of Focus treats sustained rhythm as civic practice."),
        Entry("lore.entry.station-chaos-theory.title", "CHAOS THEORY"),
        Entry("lore.entry.station-chaos-theory.body", "The Improvisational Order uses uncertainty to prevent decay."),
        Entry("lore.entry.station-global-coil.title", "THE GLOBAL COIL"),
        Entry("lore.entry.station-global-coil.body", "Planetary relays build shared warmth from many local rhythms."),
        Entry("lore.entry.station-ourotron.title", "OUROTRON"),
        Entry("lore.entry.station-ourotron.body", "Ourotron-5 preserves imagined futures as inherited memory."),
        Entry("lore.entry.station-pit.title", "THE PIT"),
        Entry("lore.entry.station-pit.body", "Mutation engineers believe pressure reveals a signal's true shape."),
        Entry("lore.entry.station-bureau.title", "THE BUREAU"),
        Entry("lore.entry.station-bureau.body", "The Bureau makes uncertainty feel orderly without changing facts."),
        Entry("lore.entry.station-strike.title", "THE STRIKE"),
        Entry("lore.entry.station-strike.body", "The Molten Core Collective breaks imposed rhythm with clean action."),
        Entry("lore.entry.station-underground.title", "UNDERGROUND SCALES"),
        Entry("lore.entry.station-underground.body", "Neighborhood relays treat every chosen shed as an authored voice."),
        Entry("lore.entry.rival-redline.title", "REDLINE"),
        Entry("lore.entry.rival-redline.body", "Redline spends open space quickly and accepts narrow recovery windows."),
        Entry("lore.entry.rival-shelter-coil.title", "SHELTER COIL"),
        Entry("lore.entry.rival-shelter-coil.body", "Shelter Coil protects exits before pursuing a louder score line."),
        Entry("lore.entry.rival-crownchaser.title", "CROWNCHASER"),
        Entry("lore.entry.rival-crownchaser.body", "Crownchaser protects combo value and risks space for record pace."),
        Entry("lore.entry.rival-mutagenist.title", "MUTAGENIST"),
        Entry("lore.entry.rival-mutagenist.body", "Mutagenist replans whenever a useful temporary mutation appears."),
        Entry("lore.entry.rival-noise-coil.title", "NOISE COIL"),
        Entry("lore.entry.rival-noise-coil.body", "Noise Coil stays legal while making bounded unpredictability visible."),
        Entry("lore.entry.rival-proof.title", "THE PROOF"),
        Entry("lore.entry.rival-proof.body", "The Proof chooses repeatable high-value routes from visible evidence."),
        Entry("lore.entry.rival-edge-prophet.title", "EDGE PROPHET"),
        Entry("lore.entry.rival-edge-prophet.body", "Edge Prophet seeks wraps and pressure without hidden immunity."),
        Entry("lore.entry.rival-meanline.title", "MEANLINE"),
        Entry("lore.entry.rival-meanline.body", "Meanline balances survival, food, and powers as conditions change."),
        Entry("lore.entry.rival-rimkeeper.title", "RIMKEEPER"),
        Entry("lore.entry.rival-rimkeeper.body", "Rimkeeper treats connected boundaries as dependable route structure."),
        Entry("lore.entry.rival-stillwater.title", "STILLWATER"),
        Entry("lore.entry.rival-stillwater.body", "Stillwater preserves options and waits for clean openings."),
        Entry("lore.entry.mutation-glossary.title", "SANCTIONED MUTATIONS"),
        Entry("lore.entry.mutation-glossary.body", "Nine temporary tools alter survival, tempo, harvest, or geometry."),
        Entry("lore.entry.history-redline.title", "REDLINE: HEAT DEBT"),
        Entry("lore.entry.history-redline.body", "Redline learned pace by measuring which narrow exits stayed honest."),
        Entry("lore.entry.history-shelter-coil.title", "SHELTER COIL: OPEN EXIT"),
        Entry("lore.entry.history-shelter-coil.body", "A failed relay taught Shelter Coil that safety must leave a way out."),
        Entry("lore.entry.history-proof.title", "THE PROOF: REPRODUCTION"),
        Entry("lore.entry.history-proof.body", "The Proof publishes seed and method beside every confident claim."),
        Entry("lore.entry.history-meanline.title", "MEANLINE: SHARED CARRIER"),
        Entry("lore.entry.history-meanline.body", "Meanline was trained between relays that disagreed on every priority."),
        Entry("lore.entry.track-flow-breath.title", "TRACK NOTE: HELD BREATH"),
        Entry("lore.entry.track-flow-breath.body", "Cadence Vale leaves quiet measures where recovery needs room."),
        Entry("lore.entry.track-chaos-offset.title", "TRACK NOTE: OFFSET RETURN"),
        Entry("lore.entry.track-chaos-offset.body", "Dr. Sibilant calls a resolved collision an improvised cadence."),
        Entry("lore.entry.collection-mutation-prism.title", "COLLECTION: MUTATION PRISM"),
        Entry("lore.entry.collection-mutation-prism.body", "Each color marks a temporary choice, never permanent superiority."),
        Entry("lore.entry.collection-first-signal.title", "COLLECTION: FIRST SIGNAL"),
        Entry("lore.entry.collection-first-signal.body", "Relay stripes honor the first route that reached another district."),
        Entry("lore.entry.replay-first-echo.title", "REPLAY MILESTONE: FIRST ECHO"),
        Entry("lore.entry.replay-first-echo.body", "One verified command history turns a lost route into evidence."),
        Entry("lore.entry.replay-five-echoes.title", "REPLAY MILESTONE: ECHO SHELF"),
        Entry("lore.entry.replay-five-echoes.body", "Five local echoes reveal habits no single score can explain."),
        Entry("lore.entry.fragment-bureau-comfort.title", "FRAGMENT: COMFORT FORM 7"),
        Entry("lore.entry.fragment-bureau-comfort.body", "Anchor Seven confirms that uncertainty remains properly cataloged."),
        Entry("lore.entry.fragment-underground-shed.title", "FRAGMENT: SHED SIGNATURE"),
        Entry("lore.entry.fragment-underground-shed.body", "Molt One records a chosen look as a public claim of authorship."),
        Entry("lore.entry.history-ourotron-five.title", "OUROTRON-5 MEMORY STACK"),
        Entry("lore.entry.history-ourotron-five.body", "Its archive stores futures that never arrived but still guide routes."),
        Entry("lore.entry.history-strike-carrier.title", "THE STRIKE CARRIER"),
        Entry("lore.entry.history-strike-carrier.body", "Rivet's first relay replaced a stalled schedule with one clear pulse."),
        Entry("lore.entry.transcript-molt-hearing.title", "TRANSCRIPT: MOLT HEARING"),
        Entry("lore.entry.transcript-molt-hearing.body", "Witnesses agree old skins carried memory, but not who first listened."),
        Entry("lore.entry.transcript-pit-safety.title", "TRANSCRIPT: PIT SAFETY TABLE"),
        Entry("lore.entry.transcript-pit-safety.body", "Rattlebyte argues that visible risk is stricter than hidden comfort."),
        Entry("lore.entry.timeline-first-carrier.title", "TIMELINE: FIRST CARRIER"),
        Entry("lore.entry.timeline-first-carrier.body", "Heat-control pulses became the earliest shared movement signal."),
        Entry("lore.entry.timeline-great-molt.title", "TIMELINE: THE GREAT MOLT"),
        Entry("lore.entry.timeline-great-molt.body", "Biology, memory, and broadcast joined without one clean origin."),
        Entry("lore.entry.timeline-coil-accord.title", "TIMELINE: COIL ACCORD"),
        Entry("lore.entry.timeline-coil-accord.body", "The districts fixed equal rules so any seed could answer a rival."),
        Entry("lore.entry.mystery-ninth-frequency.title", "MYSTERY: NINTH FREQUENCY"),
        Entry("lore.entry.mystery-ninth-frequency.body", "Some echoes contain a carrier gap no approved station claims."),
        Entry("lore.entry.interpretation-disciplined-molt.title", "INTERPRETATION: DISCIPLINE"),
        Entry("lore.entry.interpretation-disciplined-molt.body", "The Ministry says the Molt proved coordination creates freedom."),
        Entry("lore.entry.interpretation-liberated-molt.title", "INTERPRETATION: LIBERATION"),
        Entry("lore.entry.interpretation-liberated-molt.body", "The Order says the Molt began when repetition finally broke."),
        Entry("run.classic-score", "CLASSIC [=] FIXED +10 PER FOOD"),
        Entry("run.classic-rules", "NO HUNGER  |  NO POWERS"),
        Entry("run-end.cause", "CAUSE: {cause}"),
        Entry("run-end.recovery", "RECOVERY: {recovery}"),
        Entry("run-end.tour-primary", "TOUR PRIMARY {progress}"),
        Entry("run-end.personal-best", "NEW PERSONAL BEST"),
        Entry("prompt.action", "{glyph} {action}"),
        Entry("action.back-ten", "back 10"),
        Entry("action.broadcast-tour", "Broadcast Tour"),
        Entry("action.browse-content-packs", "browse content packs"),
        Entry("action.browse-input-bindings", "browse input bindings"),
        Entry("action.browse-run-unlocks", "browse run unlocks"),
        Entry("action.browse-verified-replays", "browse verified replays"),
        Entry("action.browse-versioned-scores", "browse versioned local scores"),
        Entry("action.cancel", "cancel"),
        Entry("action.cancel-unchanged", "cancel unchanged"),
        Entry("action.cancel-without-deleting", "cancel without deleting"),
        Entry("action.cancel-without-writing", "cancel without writing"),
        Entry("action.cancel-back", "cancel/back"),
        Entry("action.capture", "capture"),
        Entry("action.choose", "choose"),
        Entry("action.cosmetic-sets", "cosmetic sets"),
        Entry("action.create-backup-reset", "create backup and reset"),
        Entry("action.cycle-radio", "cycle radio station"),
        Entry("action.delete-permanently", "delete permanently"),
        Entry("action.equip", "equip"),
        Entry("action.exit-safely", "exit safely"),
        Entry("action.export-verified", "export verified"),
        Entry("action.faster", "faster"),
        Entry("action.highlight-next-goal", "highlight next goal"),
        Entry("action.keep-current-data", "keep current data"),
        Entry("action.learn-tutorial", "HELP"),
        Entry("action.list", "list"),
        Entry("action.load", "load"),
        Entry("action.next-category", "next category"),
        Entry("action.next-page", "next page"),
        Entry("action.open", "open"),
        Entry("action.or", "or"),
        Entry("action.delete-one-replay", "permanently delete one replay"),
        Entry("action.play-pause", "play/pause"),
        Entry("action.prepare-delete", "prepare delete"),
        Entry("action.previous-category", "previous category"),
        Entry("action.previous-page", "previous page"),
        Entry("action.progression-goals", "progression goals"),
        Entry("action.replays-status", "REPLAYS"),
        Entry("action.restart", "restart"),
        Entry("action.return", "return"),
        Entry("action.save-loadout", "save loadout"),
        Entry("action.sections", "sections"),
        Entry("action.select", "select"),
        Entry("action.settings", "settings"),
        Entry("action.settings-before-play", "settings before play"),
        Entry("action.skip-menu", "skip to menu"),
        Entry("action.slower", "slower"),
        Entry("action.step", "step"),
        Entry("action.swap", "swap"),
        Entry("action.toggle-hud", "toggle clean capture"),
        Entry("action.toggle-use", "toggle/use"),
        Entry("action.versioned-scores", "versioned local scores"),
        Entry("status.settings.load-defaults", "SETTINGS LOAD FAILED: DEFAULTS ACTIVE"),
        Entry(
            "status.onboarding.progress-unavailable",
            "TUTORIAL PROGRESS UNAVAILABLE: SESSION ONLY"),
        Entry(
            "status.onboarding.progress-unreadable",
            "TUTORIAL PROGRESS UNREADABLE: CHOOSE SAFELY"),
        Entry(
            "status.onboarding.progress-session-only",
            "TUTORIAL PROGRESS ACTIVE THIS SESSION ONLY"),
        Entry(
            "status.onboarding.progress-save-failed",
            "TUTORIAL PROGRESS ACTIVE THIS SESSION; SAVE FAILED"),
        Entry("status.settings.save-unavailable", "SETTINGS SAVE UNAVAILABLE"),
        Entry(
            "status.settings.save-failed",
            "SETTINGS SAVE FAILED: CURRENT SESSION ONLY"),
        Entry(
            "status.progression.load-defaults",
            "PROGRESSION LOAD FAILED: SESSION DEFAULTS ACTIVE"),
        Entry(
            "status.progression.save-failed",
            "PROGRESSION SAVE FAILED: CURRENT SESSION ONLY"),
        Entry("status.progression.highlighted", "HIGHLIGHTED NEXT GOAL: {goal}"),
        Entry(
            "status.progression.highlight-save-failed",
            "GOAL HIGHLIGHT ACTIVE THIS SESSION; SAVE FAILED"),
        Entry(
            "status.bindings.save-unavailable",
            "INPUT BINDINGS SAVE UNAVAILABLE"),
        Entry(
            "status.bindings.session-save-failed",
            "BINDINGS ACTIVE THIS SESSION; SAVE FAILED"),
        Entry("status.tour.primary-incomplete", "PRIMARY INCOMPLETE: {progress}"),
        Entry(
            "status.tour.rematch-owned",
            "SAME-SEED REMATCH COMPLETE; REWARD ALREADY OWNED"),
        Entry(
            "status.tour.completion-rejected",
            "EVENT COMPLETION REJECTED; NO REWARD GRANTED"),
        Entry(
            "status.onboarding.practice-isolated",
            "PRACTICE IS LOCAL, UNSCORED, AND NEVER WRITES A REPLAY"),
        Entry(
            "status.onboarding.skipped",
            "TUTORIAL SKIPPED: REPLAY ANY TIME FROM HELP"),
        Entry(
            "status.onboarding.available",
            "TUTORIAL AVAILABLE FROM H OR CONTROLLER LEFT STICK"),
        Entry(
            "status.onboarding.exited",
            "TUTORIAL EXITED SAFELY: NO SCORE OR PROGRESS CHANGED"),
        Entry(
            "status.onboarding.complete",
            "TUTORIAL COMPLETE: START A SCORED RUN WHEN READY"),
        Entry("status.tour.locked", "LOCKED: CLEAR {requirements}"),
        Entry("status.scores.import-cancelled", "IMPORT CANCELLED; NO FILE CHANGED"),
        Entry(
            "status.scores.import-confirm",
            "CONFIRM IMPORT FROM user://imports/high_scores.json; SOURCE STAYS UNCHANGED"),
        Entry(
            "status.scores.import-read-only",
            "IMPORT BLOCKED: NATIVE SCORE HISTORY IS READ-ONLY"),
        Entry(
            "status.bindings.browse-help",
            "Left/Right device  Up/Down select  Confirm remap  Back cancel"),
        Entry("status.bindings.remap-cancelled", "Remap cancelled."),
        Entry(
            "status.bindings.conflict",
            "CONFLICT: {token} belongs to {owner}. Confirm swaps with {action}; Back cancels."),
        Entry(
            "status.bindings.conflict-cancelled",
            "Conflict cancelled; bindings unchanged."),
        Entry(
            "status.settings.playtest-delete-cancelled",
            "PLAYTEST SUMMARY DELETION CANCELLED"),
        Entry("status.settings.reset-cancelled", "DATA RESET CANCELLED: NOTHING CHANGED"),
        Entry("status.settings.confirm-action", "PRESS CONFIRM TO USE THIS ACTION"),
        Entry("status.settings.diagnostics-copied", "DIAGNOSTICS PATH COPIED"),
        Entry(
            "status.settings.diagnostics-limited",
            "DIAGNOSTICS ACCESS LIMITED: COPY OR FOLDER OPEN FAILED"),
        Entry(
            "status.settings.playtest-delete-confirm",
            "CONFIRM PERMANENT SUMMARY AND EXPORT DELETION"),
        Entry(
            "status.settings.playtest-export-unavailable",
            "PLAYTEST SUMMARY EXPORT UNAVAILABLE"),
        Entry("status.settings.playtest-exported", "EXPORTED {count}: user://{path}"),
        Entry(
            "status.settings.playtest-export-failed",
            "PLAYTEST SUMMARY EXPORT FAILED SAFELY"),
        Entry(
            "status.settings.playtest-delete-unavailable",
            "PLAYTEST SUMMARY DELETION UNAVAILABLE"),
        Entry(
            "status.settings.playtest-delete-failed",
            "PLAYTEST SUMMARY DELETION FAILED SAFELY"),
        Entry(
            "status.player-data.reset-unavailable",
            "PLAYER-DATA RESET IS UNAVAILABLE"),
        Entry(
            "status.player-data.reset-review",
            "REVIEW THE EXACT RESET LIST; CONFIRM OR CANCEL"),
        Entry(
            "status.player-data.recovery-unavailable",
            "PLAYER-DATA RECOVERY IS UNAVAILABLE"),
        Entry(
            "status.player-data.inspecting",
            "INSPECTING BACKUPS AND VERIFYING HASHES"),
        Entry(
            "status.player-data.recovery-closed",
            "RECOVERY CLOSED: CURRENT DATA UNCHANGED"),
        Entry("status.player-data.no-backups", "NO PLAYER-DATA BACKUPS FOUND"),
        Entry(
            "status.player-data.restore-location-blocked",
            "RESTORE BLOCKED: BACKUP LOCATION ACCESS ATTEMPTED"),
        Entry(
            "status.player-data.restoring",
            "RESTORING VERIFIED BACKUP WITHOUT OVERWRITE"),
        Entry(
            "status.player-data.operation-failed",
            "PLAYER-DATA OPERATION FAILED SAFELY"),
        Entry("status.player-data.reset-blocked", "RESET BLOCKED: {code}"),
        Entry(
            "status.player-data.reset-complete",
            "RESET COMPLETE; VERIFIED BACKUP: user://{location}"),
        Entry("status.player-data.restore-blocked", "RESTORE BLOCKED: {code}"),
        Entry(
            "status.player-data.restored",
            "VERIFIED BACKUP RESTORED AND PLAYER DATA RELOADED"),
        Entry(
            "status.player-data.wait-replay",
            "WAIT FOR REPLAY WORK BEFORE RESETTING DATA"),
        Entry(
            "status.player-data.creating-backup",
            "CREATING AND VERIFYING PLAYER-DATA BACKUP"),
        Entry(
            "status.player-data.quit-paused",
            "QUIT PAUSED: FINISHING PLAYER-DATA OPERATION"),
        Entry(
            "status.player-data.quit-canceled",
            "QUIT CANCELED: PLAYER-DATA OPERATION FAILED"),
        Entry("status.onboarding.reset-offered", "TUTORIAL WILL BE OFFERED AGAIN"),
        Entry(
            "status.onboarding.reset-save-failed",
            "TUTORIAL RESET ACTIVE THIS SESSION; SAVE FAILED"),
        Entry("status.unlock.saved", "UNLOCK SAVED: {unlock}"),
        Entry("status.unlock.saved-many", "UNLOCKS SAVED: {count}"),
        Entry("status.tour.event-cleared", "EVENT CLEARED: {reward}"),
        Entry(
            "status.tour.save-failed",
            "TOUR SAVE FAILED: REWARD ACTIVE THIS SESSION ONLY"),
        Entry("status.tour.retry-ready", "RETRY READY: PRIMARY {progress}"),
        Entry(
            "status.tour.card-cleared",
            "CLEARED; SAME-SEED REMATCH AVAILABLE"),
        Entry("status.tour.card-available", "AVAILABLE; FIXED-SEED PRACTICE RUN"),
        Entry(
            "status.tour.card-locked",
            "LOCKED; COMPLETE THE LISTED PREREQUISITES"),
        Entry("status.cosmetics.loadout-saved", "LOADOUT SAVED: {cosmetic}"),
        Entry("status.cosmetics.selected", "SELECTED: {cosmetic}"),
        Entry(
            "status.scores.already-imported",
            "PYTHON TOP TEN ALREADY IMPORTED INTO LEGACY 0.2"),
        Entry(
            "status.scores.optional-import",
            "OPTIONAL LEGACY IMPORT IS AVAILABLE"),
        Entry(
            "status.scores.import-success",
            "IMPORTED {count} SCORE(S) INTO LEGACY 0.2; SOURCE UNCHANGED"),
        Entry(
            "status.scores.import-was-complete",
            "PYTHON TOP TEN WAS ALREADY IMPORTED; SOURCE UNCHANGED"),
        Entry(
            "status.scores.import-source-missing",
            "COPY high_scores.json TO user://imports/ THEN TRY AGAIN"),
        Entry(
            "status.scores.import-too-large",
            "IMPORT BLOCKED: SOURCE EXCEEDS 64 KIB"),
        Entry(
            "status.scores.import-invalid",
            "IMPORT BLOCKED: INVALID PYTHON SCORE FILE"),
        Entry(
            "status.scores.import-destination-blocked",
            "IMPORT BLOCKED: NATIVE SCORE HISTORY IS NOT WRITABLE"),
        Entry(
            "status.scores.import-io-failed",
            "IMPORT FAILED SAFELY; SOURCE UNCHANGED"),
        Entry("status.bindings.keyboard-selected", "Keyboard bindings selected."),
        Entry("status.bindings.controller-selected", "Controller bindings selected."),
        Entry(
            "status.bindings.capture-keyboard",
            "Press a key for {action} (Back cancels)"),
        Entry(
            "status.bindings.capture-controller",
            "Press a controller button or move an axis for {action} (Back cancels)"),
        Entry("status.settings.bindings-restored", "INPUT BINDINGS RESTORED"),
        Entry(
            "status.settings.bindings-session-failed",
            "INPUT BINDINGS ACTIVE THIS SESSION; SAVE FAILED"),
        Entry("status.settings.use-adjust", "USE LEFT OR RIGHT TO ADJUST"),
        Entry(
            "status.settings.read-only-contract",
            "READ-ONLY DETERMINISTIC GAMEPLAY CONTRACT"),
        Entry(
            "status.settings.playtest-deleted",
            "LOCAL PLAYTEST SUMMARIES AND EXPORTS PERMANENTLY DELETED"),
        Entry(
            "status.settings.playtest-delete-empty",
            "NO LOCAL PLAYTEST SUMMARIES OR EXPORTS TO DELETE"),
        Entry("status.settings.controls-restored", "CONTROLS DEFAULTS RESTORED"),
        Entry(
            "status.settings.controls-session-failed",
            "CONTROLS DEFAULTS ACTIVE THIS SESSION; SAVE FAILED"),
        Entry(
            "status.player-data.backup-verified",
            "VERIFIED BACKUP: CONFIRM TO RESTORE OR BACK TO KEEP CURRENT DATA"),
        Entry(
            "status.player-data.backup-corrupt",
            "CORRUPT/INCOMPLETE: RESTORE BLOCKED; CONFIRM OPENS LOCATION"),
        Entry(
            "onboarding.lesson.movement-required",
            "This lesson needs the highlighted action, not movement."),
        Entry(
            "onboarding.lesson.pause-later",
            "Pause will be practiced after the movement lessons."),
        Entry(
            "onboarding.lesson.pause-complete",
            "Paused safely. Confirm now restarts with deliberate intent."),
        Entry(
            "onboarding.lesson.restart-later",
            "Restart is available after the pause lesson."),
        Entry(
            "onboarding.lesson.complete",
            "Tutorial complete. Practice scores were never competitive."),
        Entry(
            "onboarding.lesson.turn-up",
            "Turn up. Legal turns are buffered for the next rules step."),
        Entry(
            "onboarding.lesson.turn-accepted",
            "Turn accepted exactly once. Now try the opposite direction."),
        Entry(
            "onboarding.lesson.reverse-down",
            "Press down, directly opposite the current upward movement."),
        Entry(
            "onboarding.lesson.reverse-rejected",
            "Opposite reversal rejected without changing state. Move left through the edge."),
        Entry(
            "onboarding.lesson.wrap-left",
            "Move left. The head is already at the left edge."),
        Entry(
            "onboarding.lesson.wrap-complete",
            "Edges connect. Move right into food to grow and score."),
        Entry("onboarding.lesson.food-right", "Move right into the food marker."),
        Entry(
            "onboarding.lesson.food-complete",
            "Food grows the snake and raises score. Move right twice without eating."),
        Entry(
            "onboarding.lesson.hunger-right",
            "Keep moving right while the hunger counter drains."),
        Entry(
            "onboarding.lesson.hunger-warning",
            "Starvation warning: one move remains. Move right once more."),
        Entry(
            "onboarding.lesson.starved",
            "No food means starvation. Move right into the Shield power-up."),
        Entry("onboarding.lesson.shield-right", "Move right into the Shield marker."),
        Entry(
            "onboarding.lesson.shield-collected",
            "Shield collected. Use Pause before the next rules step."),
        Entry("status.controller.connected", "CONTROLLER CONNECTED: {device}"),
        Entry("status.controller.disconnected", "CONTROLLER DISCONNECTED: {device}"),
        Entry("cosmetics.requirement.available", "AVAILABLE FROM START  1/1"),
        Entry(
            "cosmetics.requirement.tour-unlocked",
            "UNLOCKED {current}/1: {requirement} IN {event}"),
        Entry(
            "cosmetics.requirement.tour-locked",
            "LOCKED {current}/1: {requirement} IN {event}"),
        Entry(
            "cosmetics.requirement.detail-unlocked",
            "UNLOCKED {current}/1: {requirement}"),
        Entry(
            "cosmetics.requirement.detail-locked",
            "LOCKED {current}/1: {requirement}"),
        Entry("cosmetics.requirement.detail-event", "TOUR EVENT: {event}"),
        Entry("settings.section.gameplay", "GAMEPLAY"),
        Entry("settings.section.controls", "CONTROLS"),
        Entry("settings.section.audio", "AUDIO"),
        Entry("settings.section.display", "DISPLAY"),
        Entry("settings.section.accessibility", "ACCESSIBILITY"),
        Entry("settings.section.data", "DATA"),
        Entry("status.settings.item-saved", "{item} SAVED"),
        Entry("status.settings.vibe-adaptation-saved", "VIBE ADAPTATION SAVED"),
        Entry(
            "status.settings.playtest-enabled",
            "LOCAL PLAYTEST SUMMARIES OPTED IN; NO UPLOAD"),
        Entry(
            "status.settings.playtest-disabled",
            "LOCAL PLAYTEST SUMMARIES OFF; EXISTING DATA RETAINED"),
        Entry("status.settings.master-mute-saved", "MASTER MUTE SAVED"),
        Entry("status.settings.music-mute-saved", "MUSIC MUTE SAVED"),
        Entry("status.settings.sfx-mute-saved", "SFX MUTE SAVED"),
        Entry("status.settings.ui-mute-saved", "UI MUTE SAVED"),
        Entry("status.settings.mono-saved", "MONO OUTPUT SAVED"),
        Entry("status.settings.fullscreen-saved", "FULLSCREEN SAVED"),
        Entry("status.settings.display-saved", "DISPLAY MODE SAVED"),
        Entry("status.settings.contrast-saved", "HIGH CONTRAST SAVED"),
        Entry("status.settings.motion-saved", "REDUCED MOTION SAVED"),
        Entry("status.settings.flash-saved", "FLASH-FREE SAVED"),
        Entry("status.settings.gameplay-restored", "GAMEPLAY DEFAULTS RESTORED"),
        Entry("status.settings.audio-restored", "AUDIO DEFAULTS RESTORED"),
        Entry("status.settings.display-restored", "DISPLAY DEFAULTS RESTORED"),
        Entry(
            "status.settings.accessibility-restored",
            "ACCESSIBILITY DEFAULTS RESTORED"),
        Entry("status.settings.all-ready", "ALL SETTINGS READY"),
        Entry("status.bindings.error-empty", "BINDING REJECTED: EMPTY DATA"),
        Entry("status.bindings.error-json", "BINDING REJECTED: INVALID JSON"),
        Entry("status.bindings.error-schema", "BINDING REJECTED: UNSUPPORTED SCHEMA"),
        Entry("status.bindings.error-field", "BINDING REJECTED: INVALID FIELD"),
        Entry(
            "status.bindings.error-required-action",
            "BINDING REJECTED: REQUIRED ACTION WOULD BE MISSING"),
        Entry("status.bindings.error-conflict", "BINDING REJECTED: UNRESOLVED CONFLICT"),
        Entry(
            "feedback.power.last-stand-reversed",
            "LAST STAND: DEATH REVERSED"),
        Entry(
            "feedback.power.shield-broke",
            "SHIELD BROKE: COLLISION BLOCKED"),
        Entry(
            "feedback.power.last-stand-window",
            "LAST STAND RECOVERY WINDOW"),
        Entry(
            "feedback.power.activation.shield",
            "SHIELD ONLINE: 1 COLLISION BLOCK"),
        Entry(
            "feedback.power.activation.phase-shift",
            "PHASE SHIFT ONLINE: BODY PASS"),
        Entry("feedback.power.activation.last-stand", "LAST STAND ARMED"),
        Entry(
            "feedback.power.activation.slow-mo",
            "SLOW-MO ONLINE: HALF STEP RATE"),
        Entry(
            "feedback.power.activation.boost",
            "BOOST ONLINE: DOUBLE STEP RATE"),
        Entry(
            "feedback.power.activation.magnet",
            "MAGNET ONLINE: FOOD PULL"),
        Entry(
            "feedback.power.activation.bait",
            "BAIT ARMED: EAT CURRENT FOOD TO PULL THE NEXT"),
        Entry(
            "feedback.power.bait-triggered",
            "BAIT TRIGGERED: NEXT FOOD LOCKED AT {x},{y}"),
        Entry(
            "feedback.power.activation.gluttony",
            "GLUTTONY ONLINE: EAT WITHOUT GROWTH"),
        Entry(
            "feedback.power.activation.segment-detach",
            "SEGMENTS DETACHED: TIMED HAZARDS"),
        Entry("feedback.power.expired", "{power} SIGNAL EXPIRED"),
        Entry("feedback.power.cleared", "{power} SIGNAL CLEARED"),
        Entry("feedback.power.detected", "{power} SIGNAL DETECTED"),
        Entry("feedback.achievement", "ACHIEVEMENT: {achievement}"),
        Entry("feedback.starvation-warning", "STARVATION WARNING"),
        Entry("feedback.combo-expired", "COMBO EXPIRED"),
        Entry("feedback.combo-level", "COMBO {count}: {level}"),
        Entry("feedback.near-miss.style-streak", "+{points} STYLE STREAK!"),
        Entry("feedback.near-miss.clutch", "+{points} CLUTCH!"),
        Entry(
            "feedback.near-miss.threading",
            "+{points} THREADING THE NEEDLE!"),
        Entry("feedback.near-miss.close-call", "+{points} CLOSE CALL!"),
        Entry(
            "broadcast.station.flow-signal.id.1",
            "Cadence Vale: FLOW SIGNAL. HOLD THE LINE."),
        Entry(
            "broadcast.station.flow-signal.id.2",
            "Cadence Vale: RHYTHMOS CARRIER LOCKED."),
        Entry(
            "broadcast.station.flow-signal.id.3",
            "Cadence Vale: BREATHE. ROUTE. CONTINUE."),
        Entry(
            "broadcast.station.chaos-theory.id.1",
            "Dr. Sibilant: CHAOS THEORY. VARIABLES LIVE."),
        Entry(
            "broadcast.station.chaos-theory.id.2",
            "Dr. Sibilant: IMPROVISATION ORDER ON AIR."),
        Entry(
            "broadcast.station.chaos-theory.id.3",
            "Dr. Sibilant: KEEP ONE EXIT UNWRITTEN."),
        Entry(
            "broadcast.station.global-coil.id.1",
            "Sol Coil: GLOBAL COIL. SIGNALS TOGETHER."),
        Entry(
            "broadcast.station.global-coil.id.2",
            "Sol Coil: THE PLANETARY RELAY IS OPEN."),
        Entry(
            "broadcast.station.global-coil.id.3",
            "Sol Coil: ONE CIRCUIT. MANY RHYTHMS."),
        Entry(
            "broadcast.station.ourotron.id.1",
            "Vektor Null: OUROTRON. THE FUTURE REMEMBERS."),
        Entry(
            "broadcast.station.ourotron.id.2",
            "Vektor Null: ARCHIVE LOOP RESTORED."),
        Entry(
            "broadcast.station.ourotron.id.3",
            "Vektor Null: TOMORROW SHEDS BACKWARD."),
        Entry(
            "broadcast.station.the-pit.id.1",
            "DJ Rattlebyte: THE PIT. PRESSURE HAS A VOICE."),
        Entry(
            "broadcast.station.the-pit.id.2",
            "DJ Rattlebyte: RATTLEBYTE IN THE RED."),
        Entry(
            "broadcast.station.the-pit.id.3",
            "DJ Rattlebyte: RECOVER LOUDER THAN YOU FALL."),
        Entry(
            "broadcast.station.the-bureau.id.1",
            "Anchor Seven: THE BUREAU. YOUR SIGNAL IS FILED."),
        Entry(
            "broadcast.station.the-bureau.id.2",
            "Anchor Seven: ANCHOR SEVEN. FACTS REMAIN PENDING."),
        Entry(
            "broadcast.station.the-bureau.id.3",
            "Anchor Seven: COMFORT NOTICE RECEIVED."),
        Entry(
            "broadcast.station.the-strike.id.1",
            "Rivet: THE STRIKE. BREAK THE GIVEN RHYTHM."),
        Entry(
            "broadcast.station.the-strike.id.2",
            "Rivet: RIVET ON THE MOLTEN LINE."),
        Entry(
            "broadcast.station.the-strike.id.3",
            "Rivet: MAKE THE NEXT ROUTE YOURS."),
        Entry(
            "broadcast.station.underground-scales.id.1",
            "Molt One: UNDERGROUND SCALES. LEAVE A MARK."),
        Entry(
            "broadcast.station.underground-scales.id.2",
            "Molt One: MOLT ONE BELOW THE CARRIER."),
        Entry(
            "broadcast.station.underground-scales.id.3",
            "Molt One: THE GAPS ARE PART OF THE BEAT."),
    ];

    private static readonly IReadOnlyDictionary<string, ShellTextEntry> ById =
        Entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);

    static ShellLocalization()
    {
        if (Entries.Length != ById.Count)
        {
            throw new InvalidOperationException("Shell localization IDs must be unique.");
        }
        if (Entries.Any(entry =>
            !IdPattern.IsMatch(entry.Id)
            || string.IsNullOrWhiteSpace(entry.EnglishTemplate)
            || entry.EnglishTemplate.Length > MaximumFormattedCharacters))
        {
            throw new InvalidOperationException("Shell localization catalog contains an invalid entry.");
        }
    }

    public static IReadOnlyList<ShellTextEntry> All => Entries;

    public static bool ContainsId(string id) =>
        !string.IsNullOrWhiteSpace(id) && ById.ContainsKey(id);

    public static string Format(
        string id,
        ShellLocale locale,
        params ShellTextArgument[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(arguments);
        if (!ById.TryGetValue(id, out var entry))
        {
            throw new KeyNotFoundException("Unknown shell copy ID: " + id);
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var argument in arguments)
        {
            if (!entry.Parameters.Contains(argument.Name, StringComparer.Ordinal)
                || !values.TryAdd(argument.Name, ValidateValue(argument)))
            {
                throw new ArgumentException(
                    "Shell copy arguments must be expected and unique.",
                    nameof(arguments));
            }
        }
        if (values.Count != entry.Parameters.Count)
        {
            throw new ArgumentException(
                "Shell copy arguments must exactly match the template parameters.",
                nameof(arguments));
        }

        var formatted = locale switch
        {
            ShellLocale.English => entry.EnglishTemplate,
            ShellLocale.Pseudo => PseudoLocalizeTemplate(entry.EnglishTemplate),
            _ => throw new ArgumentOutOfRangeException(nameof(locale)),
        };
        foreach (var parameter in entry.Parameters)
        {
            formatted = formatted.Replace(
                "{" + parameter + "}",
                values[parameter],
                StringComparison.Ordinal);
        }
        if (formatted.Length > MaximumFormattedCharacters)
        {
            throw new InvalidOperationException("Formatted shell copy exceeds the output bound.");
        }
        return formatted;
    }

    private static ShellTextEntry Entry(string id, string englishTemplate)
    {
        var parameters = ParameterPattern.Matches(englishTemplate)
            .Cast<Match>()
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new ShellTextEntry(id, englishTemplate, parameters);
    }

    private static string ValidateValue(ShellTextArgument argument)
    {
        if (string.IsNullOrWhiteSpace(argument.Name)
            || argument.Value.Length > 256
            || argument.Value.Contains('\r')
            || argument.Value.Contains('\n'))
        {
            throw new ArgumentException("Shell copy argument is invalid.", nameof(argument));
        }
        return argument.Value;
    }

    private static string PseudoLocalizeTemplate(string template)
    {
        var builder = new StringBuilder(template.Length * 2);
        var translatableCharacters = 0;
        for (var index = 0; index < template.Length; index++)
        {
            if (template[index] == '{')
            {
                var end = template.IndexOf('}', index + 1);
                if (end < 0)
                {
                    throw new InvalidOperationException("Shell copy contains an open parameter token.");
                }
                builder.Append(template, index, end - index + 1);
                index = end;
                continue;
            }

            var character = template[index];
            if (char.IsLetter(character))
            {
                translatableCharacters++;
            }
            builder.Append(Accent(character));
        }

        var padding = Math.Max(4, (int)Math.Ceiling(translatableCharacters * 0.35));
        return "[!! " + builder + " " + new string('~', padding) + " !!]";
    }

    private static char Accent(char value) => value switch
    {
        'A' => 'Å',
        'E' => 'Ë',
        'I' => 'Ï',
        'O' => 'Ø',
        'U' => 'Ü',
        'a' => 'á',
        'e' => 'ë',
        'i' => 'ï',
        'o' => 'ø',
        'u' => 'ü',
        'C' => 'Ç',
        'c' => 'ç',
        'N' => 'Ñ',
        'n' => 'ñ',
        _ => value,
    };
}

internal sealed record LocalizationQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    string CatalogId,
    string RequiredLocale,
    string PseudoLocale,
    int StringCount,
    int ParameterizedStringCount,
    int MigratedRequiredFlowCount,
    double MinimumPseudoExpansionRatio,
    int MissingGlyphCount,
    bool ExactParameterValidation,
    bool InputGlyphParameterPreserved,
    bool MaximumTextScaleLayoutPassed,
    int RulesCopyIdCount,
    bool RulesCopyIdsResolved,
    int FeedbackCopyIdCount,
    int BroadcastCopyIdCount,
    bool BroadcastCopyIdsResolved,
    bool SourceAuditPerformed,
    int RemainingDirectDrawLabelLiteralCount,
    int RemainingDirectPromptLiteralCount,
    int RemainingDirectStatusLiteralCount,
    int RemainingComposedStatusLiteralCount,
    int RemainingDomainStatusExpressionCount,
    string MigrationStatus)
{
    public string Serialize() => JsonSerializer.Serialize(
        this,
        new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        }) + "\n";
}
