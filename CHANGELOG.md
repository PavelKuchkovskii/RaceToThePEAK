### 0.10.0
* Replaced PVP's selectable group waiting policy with mandatory personal campfire claims: every racer can activate immediately, but cannot cross until their own sequential claim succeeds.
* Added one-slot, master-authoritative Campfire Ability inventory synchronized through Photon room properties; each new campfire roll replaces the previous main ability.
* Added Adrenaline, Shield, Exhaust, Second Wind, Catch Up, Recall, Chaos Horn, Ghost Runner and reusable Mega Launch, with configurable activation keys and weighted rolls.
* Added race-progress-aware targeting and catch-up scaling based on earned checkpoints plus normalized progress through the current segment.
* Added a separate one-charge Chaos slot for the last racer at each campfire, with configurable Full Stamina, Infinite Stamina, Global Adrenaline, Global Unconscious, safe Player Swap, Previous Campfire and Middle Campfire effects.
* Added hidden Mega Launch food to ordinary non-critical luggage food. Position-based chances are configurable from leader through far-behind, and the item remains visually indistinguishable until consumed.
* Added synchronized ability HUD feedback, timed status effects, shield consumption, Mega Launch cooldown/countdown and short unconscious protection around launches.
* Removed PVP next-campfire death respawning because it would bypass the dead racer's mandatory personal activation; legacy/configured values migrate to previous-campfire respawning.
* Added all gameplay-affecting PVP ability, Chaos, Mega Launch and hidden-food tuning to the host lobby panel. The section exists only while PVP is selected, synchronizes through room properties and remains stable across host migration.
* Changed the default PVP controls to F for the main ability and C for Chaos, including a one-time migration from the previous F4/F5 defaults.
* Added embedded 256px icons for every Campfire Ability and Chaos, plus ready, passive and numeric cooldown HUD states with a visual recharge bar.
* Reworked the ability HUD as a floating, cardless icon stack on the left-center of the screen. It avoids the teammate respawn timer and PEAK's stamina, status and inventory interface while retaining clear cooldown feedback.
* Added one synchronized random starting ability for every racer in PVP, using the same configured weights as campfire rewards. Starting grants persist across host migration and do not include Chaos.
* Replaced reused vanilla biome seals with precise local checkpoint gates after each campfire, so lagging racers can always reach and claim an already-lit fire while unearned biomes remain inaccessible. The master client also corrects boundary bypasses and physics desynchronization.
* Fixed first-biome race scores so directed abilities such as Exhaust can identify racers ahead while the user is still on the beach.

### 0.9.0
* Added three synchronized campfire waiting policies for every race mode: wait for nobody, wait for each team independently, or wait for the whole lobby.
* Added host-authoritative, monotonic player/team/lobby checkpoint progress with Photon late-join and host-migration synchronization.
* Players without an explicitly selected troop are treated as one-person teams; disconnected players and bots never block a campfire.
* Disabled next-campfire death respawning when waiting for nobody, including PVP real deaths, and safely migrate incompatible old/configured combinations to previous-campfire respawning.
* Later teams can logically complete an already-lit campfire. Loaded transitions are blocked locally only for teams that have not completed them, and passed barriers never return for eligible teams.
* Reworked biome retention to preserve one contiguous route for lagging players, unfinished teams, corpse timers, previous-campfire targets and PVP blowgun returns, then unload genuinely unneeded older segments.
* Scoped final rising lava/Gloom by waiting policy: per player for nobody, shared per team for team waiting, and vanilla global behavior for lobby waiting. Existing no-reset-on-death behavior is unchanged.
* A fully wiped team now returns to its own last completed checkpoint while another team remains alive; another team's later fire can no longer pull it forward. A full lobby wipe still ends the run.
* Rebuilt the host F3 lobby menu into scrollable Progression, Respawn, PVP and Current Rules sections with contextual descriptions, compatibility validation and responsive sizing.
* Hardened team-change and campfire-completion RPC validation so the master client verifies sender ownership, range, team readiness and sequential checkpoint progress.

### 0.8.5
* Moved RaceToThePeak's host lobby and in-run panels to an independent configurable key, defaulting to F3.
* Removed the Harmony interception and forced closing of PEAK Unlimited's configuration window; PEAK Unlimited retains full ownership of its F2 binding.
* Panel hints now display the currently configured RaceToThePeak menu key.

### 0.8.4
* Previous-campfire respawns and PVP blowgun knockouts now use each victim's personal climbing progress instead of the lobby's furthest unlocked segment.
* Each actor's furthest personally reached lit campfire is synchronized through Photon room properties and survives falling back, death and host migration.
* Racers can no longer be teleported forward to a campfire they have not reached merely because a leader unlocked later biomes.
* Beach racers with no reached campfire still return to their personal starting spawn.

### 0.8.3
* Final-biome rising lava and rising Gloom now start independently when each player physically enters the final segment.
* Per-player entry timestamps are synchronized through Photon room properties for spectators, late joiners and host migration.
* Death and respawning do not reset a player's final-hazard timer.
* Final hazard rendering follows the observed player, while lava damage and Gloom status application use only the local player's own timer and segment.
* Earlier biomes are excluded from final rising-field damage and visual Gloom, while non-final lava, Volcano hazards and Void behavior remain vanilla.

### 0.8.2
* Fixed the newly activated Gloom/Swamp environment visually leaking into retained earlier biomes.
* Sun, sky lighting and day/night profiles now follow the character observed by each client instead of the furthest globally unlocked campfire.
* Gloom height fog and safe-zone shader values now run only while the observed character is physically inside the Gloom segment.
* Alpine snowstorms remain local to their existing wind-zone bounds and no longer combine with the next biome's global lighting profile for racers behind.

### 0.8.1
* Fixed PEAK's lower segment boundary remaining active after a solo campfire transition and blocking racers below.
* Previous campfire roots now remain active together with their completed biomes, including their luggage and connection geometry.
* The upper wall of the current segment remains enabled, so racers cannot enter an unloaded future biome.
* Void-specific walls and progression remain untouched.

### 0.8.0
* Added a separate lobby setting for respawning after a real skeleton death in PVP mode.
* PVP real deaths can now use the previous campfire, the synchronized corpse timer, or the next activated campfire.
* The corpse option uses the existing configurable corpse delay (default 30 seconds) and team countdown HUD.
* PVP blowgun knockouts remain independent: they always move the victim immediately to the previous campfire without a death penalty.
* The PVP death penalty still applies only to real deaths regardless of the selected PVP death respawn strategy.

### 0.7.0
* Added optional per-luggage refresh timers for PVP mode, configurable by the host in the Airport F2 panel.
* Opened luggage closes after the configured delay (default 5 minutes) and rolls a fresh set of items when reopened.
* Refresh deadlines and unclaimed loot IDs are synchronized through Photon room properties for late joiners and host migration.
* Unclaimed old loot is removed on refresh to prevent item accumulation; items already taken by players are preserved.
* Respawn chests are excluded, and the feature has no effect outside PVP mode or while disabled.

### 0.6.2
* Added a separate crimson inventory-slot icon for the custom PVP blowgun.
* The ordinary blowgun's icon and UI data remain unchanged, including alternate accessibility icons.

### 0.6.1
* Fixed closing the in-run F2 panel hiding only its visuals while leaving PEAK's MenuWindow input lock active.
* F2 now closes the RaceToThePeak panel cleanly and restores cursor capture and movement.
* Added soft compatibility with PEAK Unlimited 4.x so a single F2 press cannot open or swap between both mods' panels.
* Any stale PEAK Unlimited configuration window is closed when changing scenes, preventing it from trapping input after returning to the Airport.

### 0.6.0
* Added a separate host-only F2 race controls panel during active runs in all four respawn modes.
* Added a confirmation-protected End Current Run action using PEAK's native networked results and return-to-Airport flow.
* Guests still receive no F2 panel, and lobby rule settings remain unavailable during a run.
* Custom respawn processing now stops as soon as PEAK marks the run as ended.

### 0.5.0
* Added a fourth PVP respawn mode based on immediate previous-campfire respawning.
* Added a distinct crimson networked copy of PEAK's blowgun with its own item ID and prefab identity.
* Ordinary luggage has a 50% chance to include the PVP blowgun while PVP mode is active.
* Only a confirmed pass-out caused by the PVP blowgun redirects the victim immediately; normal blowguns and unrelated pass-outs retain vanilla behavior.
* PVP knockouts drop pocket items, skip the skeleton wait, add no timer penalty, and preserve Scout checkpoint flag priority.
* Added a separate configurable PVP death penalty, defaulting to zero.
* Wrapped Photon prefab loading through a delegating pool for compatibility with PEAK Unlimited and other prefab pools.

### 0.4.0
* Added a compact team-colored corpse respawn countdown for the dead player and their teammates.
* Synchronized corpse respawn deadlines through Photon room properties for matching timers, late joins, and host migration.
* Fixed custom respawns triggering when a player merely passed out; all modes now wait for actual skeleton death.
* Added a host-only F2 lobby menu with settings synchronized to every client and late joiner.
* Added three respawn modes: next campfire, timed at corpse, and immediate at the previous campfire.
* Added separate death penalties for every mode (defaults: 5, 5, and 0 minutes) and a configurable corpse delay (default: 30 seconds).
* Scout checkpoint flags retain priority over custom respawns and do not add a time penalty.
* Timed and immediate respawn modes no longer end the run when the final living racer dies.
* Leaderboard rows now show current altitude in meters; team altitude is averaged like team time.
* Reworked leaderboard bounds and population handling for PEAK Unlimited lobbies.

### 0.3.0
* Updated campfire revival for PEAK 2.x's three-argument revive RPC.
* Respawn statues now reliably spawn items instead of reviving players.
* Campfires can be activated by one racer without waiting for everyone else.
* Previous biomes remain active so racers can continue climbing after another player advances.
* Supports any lobby size and remains compatible with PEAK Unlimited.
* Disabled Scoutmaster spawning.

### 0.2.6 
* Fixes for most recent patch of PEAK

### 0.2.1 
* Fixed spawn location on campfires

### 0.2.0 
* Color coded the leaderboard to better show what team is in what position
* Made the scorpion purple more indigo to differentiate from some of the other troops
* Added new Narwhal team emblem
* Added new leaderboard image

### 0.1.0 
* Beta Release
