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
