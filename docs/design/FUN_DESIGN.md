# Fun and Player Experience Strategy

This document defines what Vibe Snake is trying to make players feel, how each major feature earns its place, and how the team will test those design hypotheses. [GAME_DESIGN.md](GAME_DESIGN.md) owns the current rules. This document owns the intended experience and the standards used to refine those rules.

## The answer to "will all these features make it fun?"

They can, but feature count is not the cause of fun. Powers, effects, radio, unlocks, customization, progression, AI spectators, and lore become valuable only when they strengthen a coherent cycle of skill, choice, tension, payoff, and recovery. If they demand attention without improving that cycle, they make the game noisier and less fun.

The working fun thesis is:

> Plan the route, build the vibe, flirt with disaster, and recover with style.

The player fantasy is not merely "Snake with more things." The player pilots a growing, expressive signal-serpent through a living broadcast arena. A good run begins readable, develops a rhythm, invites increasingly dangerous choices, reaches a memorable climax, and makes another attempt irresistible because the player understands what they could do better.

## Exceptional is a depth claim

Exceptional does not mean maximizing the number of systems or the volume of content. It means the next decision feels inevitable and controllable, pressure remains legible, a rescue feels earned, and every supporting system reinforces the same fantasy. Extra iteration raises this standard because it increases the opportunity to perfect existing interactions. It does not excuse breadth without evidence.

A system earns promotion only when:

1. It strengthens route planning, escalating vibe, deliberate danger, or stylish recovery.
2. Its signal remains understandable under real pressure and under the supported muted, reduced-motion, flash-free, high-contrast, and controller-only profiles.
3. Deterministic tests prove the rules and event sequence, while fixed-seed human observation tests the intended feeling.
4. Negative and neutral findings remain in the evidence record and change the keep, revise, or remove decision.
5. It does not create a second source of truth for intensity, state, scoring, or content identity.
6. It does not broaden a weaker system that still fails its current depth gate.

The proof order is core control and attributable death, deterministic cross-platform foundations, one escalation and recovery grammar, depth across the existing nine powers, fair modes and balance, then authored expression, radio, spectatorship, and lore. The [roadmap excellence locks](../../ROADMAP.md#excellence-ordering-locks) make that order enforceable.

## Four player needs

Every major feature must serve at least one need and must not materially damage the others.

| Need | Player feeling | Vibe Snake expression | Failure mode |
| --- | --- | --- | --- |
| Competence | "I am learning and getting better" | Exact controls, readable danger, route planning, combo mastery, attributable deaths, useful replays | Random or hidden rules make outcomes feel arbitrary |
| Autonomy | "This run reflects my choices" | Route forks, power-up detours, mode selection, radio choice, loadouts, challenge selection | Automatic systems play the game for the player |
| Expression | "This is my snake and my vibe" | Cosmetics, stations, trails, run cards, chosen goals, AI affinities | Cosmetics obscure the board or become a grind checklist |
| Connection | "This world and its characters remember meaning" | AI rivalries, hosts, optional lore, local ghosts, seed sharing, collections | Exposition interrupts play or fake social systems feel manipulative |

Research on games repeatedly connects enjoyment and engagement with competence, autonomy, and relatedness. Customization is strongest when it increases autonomy, control, and attachment rather than functioning as decoration alone. These findings inform hypotheses, not automatic proof that a particular implementation is fun.

## The nested loops

### The next ten seconds

1. Read the head, food, body, timer, and one relevant opportunity.
2. Commit to a route with buffered cardinal turns.
3. Receive immediate, unambiguous feedback.
4. Revise the plan as the body and temporary effects change.

The game fails at this scale if effects hide cells, input feels late, the timer is difficult to read, or the player cannot explain a death.

### The run

1. Establish a safe rhythm.
2. Link food into a combo.
3. Choose whether to detour for tactical power.
4. Let audiovisual intensity rise with demonstrated mastery.
5. Survive a high-pressure climax or lose the chain.
6. Recover, learn, and restart with minimal friction.

### The play session

1. Pursue a self-chosen mastery, discovery, or collection goal.
2. Compare a run with a personal best, replay, AI rival, or shared seed.
3. Unlock expression or story context without increasing base survival power.
4. Leave with a clear next curiosity instead of an obligation.

## One escalation language

Starvation, combo, near misses, score bonuses, powers, particles, radio, and background changes currently compete for salience. Version 1.0 needs one hierarchy:

1. Starvation communicates urgency.
2. Combo defines positive escalation and the current Vibe Level.
3. Power-ups create tactical deviations from the route.
4. Near misses recognize intentional or recoverable danger.
5. Score records the outcome.

The combo thresholds already present in scoring provide a useful first Vibe Level map:

| Working level | Combo range | Gameplay message | Presentation budget |
| --- | ---: | --- | --- |
| Grounded | 0 to 2 | Read and establish control | Clean grid, base music, minimal particles |
| Flow | 3 to 4 | The chain is real | One music layer, restrained trail, clear tier cue |
| Heat | 5 to 9 | Route pressure is rising | Stronger pulse, percussion or stem, richer pickups |
| Overdrive | 10 to 19 | Mastery is visible | Full mix, controlled camera impulse, high-value stingers |
| Transcendent | 20 and above | The run has reached a rare climax | Distinct crown state, maximum safe intensity, persistent run marker |

The exact names are content work, but the structure is a rules requirement. Each level transition fires one typed event. Audio, background, particles, HUD, haptics, and accessibility alternatives subscribe to that event. No subsystem independently guesses intensity from score.

### Readability budget

- The head, the next cell, food, and fatal obstacles remain readable at every level.
- Camera shake never changes the logical pointer or hides a required turn.
- Hitstop never consumes buffered input or advances starvation behind a frozen image.
- A zero-shake, reduced-motion, flash-free profile preserves the same rules and scoring category.
- Critical cues use at least two practical channels, such as shape plus sound or text plus motion.
- Decorative particles are culled before gameplay indicators.
- A transition announces itself once. Repeated pulses do not compete with collision or starvation warnings.

## Powers: deepen choice before adding breadth

The nine working powers are enough for 1.0. The missing ingredient is not another effect. It is stronger agency, anticipation, and interaction.

| Tactical family | Powers | Intended question |
| --- | --- | --- |
| Protection | Shield, Last Stand, Phase Shift | How aggressively can I route while protected? |
| Tempo | Slow-Mo, Boost | Do I want control or score-pressure speed? |
| Harvest | Magnet, Bait, Gluttony | How do I reshape the next food sequence? |
| Geometry | Segment Detach | Is immediate freedom worth creating temporary hazards? |

### Required refinement

1. Telegraph a spawn before it becomes collectible, using a grid-safe shape and sound.
2. Make its type and remaining visibility legible before the player commits to a detour.
3. Place it so collection is a route decision, not an accidental reward on the default path.
4. Show activation, duration, expiry, and consumption with one shared visual grammar.
5. Explain the effect in onboarding and the pause glossary without requiring lore knowledge.
6. Record offered, collected, activated, expired, consumed, and death-adjacent events in local run summaries.
7. Prevent redundant offers, especially Shield beside Last Stand or a tempo power that negates the current one without a meaningful choice.

### Mutation Fork experiment

A promising autonomy mechanic is an occasional two-choice Mutation Fork. Two powers appear in separated, readable positions and collecting one withdraws the other. This preserves Snake's movement-only input while giving the player a meaningful build decision. It must remain an experiment until seeded simulations and human tests show that it creates planning rather than clutter.

### Synergies worth making legible

| Combination | Player story | Design control |
| --- | --- | --- |
| Boost plus Phase Shift | High-speed line through a dangerous body knot | Strong style feedback, no hidden collision window |
| Slow-Mo plus Magnet | Deliberate food capture and recovery | Useful onboarding and accessibility-friendly strategy |
| Bait plus Boost | Predict a nearby respawn, then convert it quickly | Spawn preview must make the plan learnable |
| Gluttony plus Magnet | Score and timer recovery without body growth | Score category and duration remain explicit |
| Segment Detach plus protection | Trade body relief for a temporary obstacle field | Obstacles need a countdown and distinct silhouette |
| Last Stand after a long combo | Dramatic recovery instead of a flat death | Recovery state must be controllable and never feel automatic or random |

Anti-synergies should be intentional, visible, and rare. The spawn director should not silently waste a player's opportunity.

## Progression without grind

Progression should point at interesting play, celebrate competence, and expand expression. It should never repair an uninteresting core loop.

### Three progression lanes

- Mastery: personal bests, combo tiers, clean recoveries, wrap techniques, and challenge medals.
- Discovery: stations heard, AI rivals observed, power synergies found, lore fragments, and unusual run conditions.
- Identity: cosmetics, themed sets, loadouts, trails, portraits, station affiliations, and run-card frames.

The player chooses a highlighted goal from any lane. End-of-run presentation shows what changed and why. A visible requirement and progress bar replace surprise unlocks and vague grinding.

### Unlock rules

- Give the first meaningful expression choice early enough to demonstrate the system.
- Mix achievable milestones, experiments, and mastery goals instead of relying on lifetime counters.
- Replace extreme repetition thresholds with authored challenges when the repeated act is no longer interesting.
- Use no paid power, random paid reward, daily obligation, streak loss, or fear-of-missing-out schedule.
- Keep mechanical starting power identical for new and experienced profiles.
- Let players preview locked cosmetics on a safe model and identify the exact requirement.
- Group cosmetics into small thematic collections that reveal optional world context when completed.

The current 10,800 combinations are breadth, not proof of value. A smaller set of clearly authored, readable, well-previewed combinations is better than thousands of combinations that clip, reduce contrast, or feel interchangeable.

## Customization as play

Customization should affect attachment and presentation while leaving hitboxes and scoring unchanged.

- Validate head contrast, body continuity, eye placement, accessory bounds, and trail occlusion automatically.
- Provide named loadouts and instant preview under Grounded and Transcendent effects.
- Allow station-themed and AI-rival-themed sets without forcing them on the player.
- Persist accessibility-safe overrides independently from cosmetic choices.
- Capture the selected look in replay metadata and shareable run cards.
- Never make a rare cosmetic inherently less readable than a default cosmetic.

## Radio as a reactive world

The radio is a differentiator when it feels authored. A large folder of tracks is not yet a GTA-style radio system.

Each station needs:

- A musical identity with curated inclusion and exclusion rules.
- A host voice, point of view, visual identity, and relationship to the Coil.
- Station IDs, short bumpers, transition stingers, and metadata that never obscure critical cues.
- Shuffle-bag and cooldown rules that avoid immediate repetition.
- Resume and skip behavior that respects the current track.
- Event-aware ducking for collision warnings, power activation, and death.
- Optional, preauthored intensity layers or compatible stingers tied to Vibe Level.
- A complete rights, source, loudness, clipping, and content manifest.

Adaptive audio should react to a small stable event vocabulary. It should not cut between songs on every combo change. The preferred first design keeps the chosen track playing, changes safe layers or filters at major Vibe transitions, and uses brief host or station material only at natural boundaries such as run start, milestone, recovery, and post-run.

## Let's Play as a game, not a screensaver

Research on game spectatorship identifies learning, affective entertainment, social connection, tension release, and personal identification as important motives. An offline AI mode can serve several of these without pretending to be a live social network.

### Viewer agency

- Choose an AI channel, seed, ruleset, or rivalry.
- Pause, change simulation speed, inspect the board, and hide explanatory overlays.
- See the AI's current target, risk tolerance, planned route class, and active power priority.
- Predict a run milestone for fun, with no currency or progression advantage.
- Challenge the same seed immediately or race a saved ghost under identical rules.

### Character truth

- Every advertised personality trait maps to measurable policy weights.
- Seeded league reports demonstrate that personalities make recognizably different choices.
- A bold AI may accept risk, but it may not receive hidden information or altered collision rules.
- Handcrafted commentary reacts to typed events and personality context. Runtime generative AI is not required.
- Rivalries and records persist locally as fiction and statistics, separate from human progression.

### Broadcast clarity

The viewer should understand what is at stake before a climax. The overlay can show target, Vibe Level, survival resources, personal record delta, and a concise reason for a surprising choice. It must not expose a wall of internal weights during normal viewing.

## Lore with three depths

Lore should reward curiosity without taxing the core loop.

### Surface

Station names, host lines, AI portraits, arena details, power names, achievement descriptions, and short environmental changes imply a coherent world during normal play.

### Discoverable

Optional codex entries, rival histories, track notes, cosmetic-set descriptions, replay milestones, and recovered broadcast fragments connect details after runs.

### Deep archive

Longer transcripts, timelines, hidden relationships, alternate interpretations, and completion mysteries serve players who actively seek them. They remain outside critical instructions and active high-pressure play.

The [world and broadcast bible](WORLD_BIBLE.md) owns the foundation canon for the Coil, signal-serpents, station institutions, rival identities, Broadcast Tour, vocabulary, tone, continuity, and content review. This strategy owns whether those elements earn their attention cost during play.

## The missing social layer, kept offline-first

Version 1.0 does not need accounts or servers to create comparison and connection.

- Stable seed codes let people play the same board sequence.
- Replay files and local ghosts make skill visible.
- Run cards summarize rules, seed, score, combo, length, station, powers, and cosmetic look.
- Local rival slots compare household or friend profiles without a global leaderboard.
- AI leagues create recurring characters and stories.
- Import is explicit, validated, size-limited, and never executes code.

## Modes and scope

Version 1.0 should perfect two human rulesets:

- Classic: movement, food, growth, wrapping, self-collision, fixed speed, and a clean score category.
- Vibe: starvation, combo escalation, near misses, the nine powers, disclosed adaptive policy, progression, and full presentation.

Seed challenges and ghosts are wrappers around these rules, not additional rulesets. New modes wait until both core modes pass clarity, balance, accessibility, replay, and content gates.

## How fun is evaluated

Automatic QA can prove that rules are consistent and expose balance outliers. It cannot decide whether a recovery feels heroic or a station becomes tiring. Human evaluation remains a release and experience-claim gate, not a reason to pause reversible implementation. When people are unavailable, retain the automated evidence bundle, keep the subjective result explicitly unverified, and continue the next technically unblocked task.

### Observation questions

- Can a new player move, eat, die, and restart without explanation?
- Can the player state why they died?
- Does each power change a route decision before it changes a number?
- Does escalation increase excitement without reducing board readability?
- Does a broken combo invite recovery or make the run feel over?
- Do players choose stations, goals, cosmetics, and AI channels for understandable reasons?
- After several runs, do they describe a skill they improved and a curiosity they want to pursue?
- Do reduced-motion and muted play preserve tension and clarity?

### Behavioral evidence

Record locally and with consent when applicable: restart choice, run length, food routes, offered and collected powers, avoidable deaths after effects, combo tier reached, recovery success, station changes, goal selection, unlock preview, replay use, and seed rematch. These signals locate questions. They do not label a player as having fun.

### Experiment discipline

1. State one hypothesis and the player group it concerns.
2. Change one dominant system where practical.
3. Hold seeds and rules versions constant for comparisons.
4. Combine observation, interview, run data, and replay review.
5. Keep negative and neutral results.
6. Revert features that add attention cost without a repeatable benefit.
7. Record the build hash, rules identity, seed, input device, accessibility profile, and observer notes so a later iteration can reproduce the comparison.
8. Do not use test count, coverage, document completeness, or feature count as a proxy for the player-facing hypothesis.

## Features that do not earn automatic inclusion

- More power-up types before the nine existing types are deep and readable.
- Permanent stat upgrades that make a new profile weaker.
- Unannounced DDA in comparable score categories.
- Runtime generative commentary or lore.
- Online accounts, global leaderboards, battle passes, daily streaks, or live-service obligations.
- Effects that override controls or hide a fatal cell.
- Lore required to understand a rule.
- Achievements whose only purpose is repeating an already-mastered action many more times.

## Research basis

- [Ryan, Rigby, and Przybylski, 2006](https://selfdeterminationtheory.org/SDT/documents/2006_RyanRigbyPrzybylski_MandE.pdf): competence, autonomy, relatedness, intuitive control, and presence are useful predictors of game enjoyment and motivation.
- [Przybylski, Rigby, and Ryan, 2010](https://journals.sagepub.com/doi/10.1037/a0019440): game engagement can be modeled through satisfaction of competence, autonomy, and relatedness.
- [Hunicke, LeBlanc, and Zubek, MDA](https://aaai.org/papers/ws04-04-001-mda-a-formal-approach-to-game-design-and-game-research/): mechanics create dynamics that produce player-facing aesthetics, so feature implementation must be traced to intended experience.
- [Sweetser and Wyeth, GameFlow](https://www.valuesatplay.org/wp-content/uploads/2007/09/sweetser.pdf): concentration, challenge, skill, control, clear goals, feedback, immersion, and social interaction provide a practical enjoyment review framework.
- [Customization, autonomy, control, and attachment study](https://www.sciencedirect.com/science/article/pii/S0747563215001090): customization is most useful when it supports agency and attachment.
- [Sjoblom and Hamari, motivations for watching game streams](https://research.utu.fi/converis/portal/detail/Publication/17789818?lang=en_GB): spectatorship serves cognitive, affective, social, tension-release, and personal-integrative motives.
- [Hutchings and McCormack, adaptive music](https://arxiv.org/abs/1907.01154): context-aware adaptive music can increase reported immersion and alignment with game-world concepts, supporting measured rather than constant musical reactivity.
