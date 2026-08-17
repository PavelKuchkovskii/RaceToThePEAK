# Race To The PEAK
Ever find yourself racing to get to the top? Looking for a reason to sabotage your friends? This mod aims to facilitate just that!
![image](https://raw.githubusercontent.com/Raiderj9/RaceToThePEAK/refs/heads/master/Pictures/Leaderboard.png)

This mod introduces Troops (Teams) that players can join to team up as they race to the PEAK. Will you beat the other troops <br>
through climbing prowess, or resort to sabotage to be the fastest up the PEAK.

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/raiderj9) 

## Features
- Troops. Race as a team or alone in 1 of 12 troops.
- Armbands. Rep your troop with this extra bit of swag.
- Climb Timer. Track your progress up to the PEAK.
- Leaderboard. See each troop's time and current altitude.
- Three host-configurable campfire progression policies outside PVP: wait for nobody, wait for each team independently, or wait for the whole lobby.
- PVP uses mandatory personal campfire claims: nobody waits for another racer, but nobody may cross a boundary before activating that campfire themselves.
- Every racer receives a weighted random Campfire Ability at the start of PVP and after every personal campfire claim. One main ability is stored at a time, while the last racer to each fire can also hold one separate Chaos charge.
- Old biomes and camp roots remain available while a player, unfinished team, corpse timer or valid respawn target still needs them, then unload as one safe contiguous range.
- Sun, sky, storms and Gloom visuals follow each client's observed racer, so unlocking a later biome does not obscure earlier racers' routes.
- Final rising lava or Gloom uses a per-player clock when waiting for nobody, a shared per-team clock when waiting for teams, and PEAK's vanilla global clock when waiting for the lobby.
- Previous-campfire respawns use the checkpoint legitimately completed by the active player/team/lobby progression scope, never another team's later global map progress.
- Four host-configurable respawn modes, available from the Airport with **F3** by default.
- Respawn statues always give items instead of reviving players.
- Scoutmaster spawning is disabled.
- Compatible with PEAK Unlimited and arbitrary lobby sizes.

## Respawn settings
The lobby host can press **F3** in the Airport to configure and synchronize the race rules:

1. **Next campfire**: add a configurable penalty (default **5 minutes**) and revive when the player's team/lobby completes its next campfire. This option is unavailable with **Wait for nobody**, because it could push a player through an unearned biome.
2. **Timed at corpse**: add a configurable penalty (default **5 minutes**) and revive at the player's own corpse after a configurable delay (default **30 seconds**). A compact team-colored countdown is shown to the dead player and their teammates.
3. **Previous campfire**: immediately revive at the previous campfire with a configurable penalty (default **0 minutes**).
4. **PVP**: enables personal campfire progression, the crimson PVP blowgun, separate real-death rules, Campfire Abilities and Chaos. Its complete rules and Airport settings are described in the PVP section below.

Settings are locked after the race leaves the Airport. Only the lobby host can open the RaceToThePeak panel; guests receive the room's active settings silently. Its key is independently configurable as `UI.MenuKey` in RaceToThePeak's BepInEx config and defaults to **F3**, leaving PEAK Unlimited's **F2** menu completely untouched.

## Campfire progression settings

The same host panel provides three waiting policies for non-PVP respawn modes:

**PVP override:** the waiting selector is ignored and hidden in PVP. Every living racer activates each campfire independently. A globally loaded biome remains locally blocked for that racer until their own sequential claim is accepted; no teammate or lobby member can block the interaction.

1. **Wait for nobody**: outside PVP, the first living racer activates the campfire and loads the next biome for everyone. Other racers do not need to claim the same transition. Final rising hazards use separate player clocks.
2. **Wait for team**: one living teammate must be at the campfire, and every other connected teammate must be in range or actually dead. An unconscious teammate still counts as living. The next biome is loaded globally once, but its boundary opens locally only for teams that completed the campfire. Final rising hazards share one clock per team.
3. **Wait for lobby**: every connected living player must be in range, matching PEAK's lobby-wide progression. Final rising hazards retain their vanilla global synchronization.

Disconnected players and bots are excluded. A player who did not select a troop is treated as a one-person team for progression. Already-lit campfires remain logically claimable by later teams, so the first team never grants access to its competitors.

## PVP mode

PVP is a complete race mode selected from the **RESPAWN** section of the Airport panel. It does not use the normal Nobody/Team/Lobby waiting selector. Instead, progression, combat rewards and death rules are personal to each racer.

### Personal campfire progression

- Nobody waits for anybody else. A racer can activate a campfire as soon as they reach it.
- Every racer must personally activate every campfire in sequence. A biome may already be loaded for other players, but its boundary remains blocked until that racer claims the preceding fire.
- A campfire that is already visually lit remains logically claimable by racers who arrive later.
- PVP checkpoint state, abilities and Chaos charges are synchronized through the room and survive host migration.
- Individual timing and final-biome rising hazards follow each racer's own campfire progress.

### PVP blowgun, deaths and luggage

- Ordinary luggage has a **50% chance** to replace one rolled reward with the crimson PVP blowgun. Respawn chests are excluded.
- A victim knocked unconscious by this special blowgun drops their pocket items and is sent immediately to their own previous campfire. This adds no time penalty and does not wait for a skeleton death.
- Real skeleton deaths use the host's PVP death penalty and either **Previous fire** or **Corpse timer**. Next-fire respawning is disabled because it would bypass the required personal claim.
- Opened luggage can optionally close and roll fresh loot after **30-1800 seconds**. This is disabled by default; the default enabled delay is **300 seconds**. Collected items are never removed and respawn chests never refresh.

### Ability inventory and controls

| Slot | Capacity | How it is received | Use |
| --- | ---: | --- | --- |
| Main Campfire Ability | 1 | One weighted roll at the start of the run and one after every personal campfire activation | **F** for active abilities; passive abilities trigger automatically |
| Chaos | 1 | Awarded to the last racer who personally completes each campfire | **C** |

The starting roll uses the same host-configured weights as campfire rewards, so every racer has an ability in the first biome. Receiving a new main ability always replaces the previous one, even if it was unused. Active abilities are consumed after a valid use, except **Mega Launch**, which remains stored and uses a cooldown. Chaos is a separate one-charge slot and is never included in the starting roll.

The **F** and **C** bindings are personal client settings and can be changed in RaceToThePeak's BepInEx config. Pressing **F** while holding a passive ability only reports that it is passive; it does not consume it.

### Main ability pool

Weights are relative rather than percentages. Setting a weight to `0` disables that result; if every weight is `0`, Adrenaline is used as the safe fallback.

| Ability | Type | Default weight | Effect |
| --- | --- | ---: | --- |
| **Adrenaline** | Active, one use | 12 | Applies the original Lollipop and Energy Drink effects together. |
| **Shield** | Passive, one block | 12 | Blocks and consumes itself against the next directed Exhaust, Recall or Ghost Runner. |
| **Exhaust** | Active, one use | 12 | Targets a random racer ahead and increases their stamina use by 40% for 8 seconds. It is not consumed when no valid target exists. |
| **Second Wind** | Passive, one recovery | 12 | Automatically recovers the owner from the next ordinary unconscious state, clears accumulated cold, heat, poison, spores and drowsiness without removing persistent conditions, and grants 2 seconds of protection. |
| **Catch Up** | Passive while held | 12 | Improves movement, jumping, climbing and stamina recovery by 5-25% according to the gap to the leader. The last-place racer also qualifies for this gap-based aid without holding the ability. |
| **Recall** | Active, one use | 10 | After a warning, returns the living leader to their previous campfire when the lead is at least one checkpoint or otherwise large enough. |
| **Chaos Horn** | Active, one use | 10 | Removes bonus stamina from every other living non-ghost racer. This is a main ability, not the separate Chaos slot. |
| **Ghost Runner** | Active, one use | 8 | While the owner is a ghost, increases the spectated living target's stamina use by 20% for 15 seconds. |
| **Mega Launch** | Active, reusable | 12 | After a five-second countdown, launches the owner in their current look direction. Force and cooldown are host-configurable. |

### Chaos effects

Only one Chaos charge can be stored. Activating it with **C** consumes the charge and rolls one synchronized effect from a separately weighted pool.

| Effect | Default weight | Result |
| --- | ---: | --- |
| **Full Stamina** | 22 | Restores stamina to every living racer. |
| **Infinite Stamina** | 18 | Grants every living racer infinite stamina for 5 seconds. |
| **Global Adrenaline** | 18 | Applies Adrenaline to every living racer. |
| **Global Unconscious** | 16 | Warns everyone, then knocks all living racers unconscious after 10 seconds. |
| **Player Swap** | 12 | Randomly pairs living racers and swaps them between their recorded safe positions. |
| **Previous Campfire** | 10 | Moves all living racers to their own previous campfires. |
| **Middle Campfire** | 4 | Moves all living racers to a middle campfire without claiming that destination for them. |

### Ability HUD

The in-run HUD uses a floating, cardless icon stack at the left-center of the screen, away from PEAK's teammate respawn timer, stamina, status and inventory UI. It shows only slots and bonuses currently owned. Ready active abilities have a bright underline, passive abilities use blue, and a cooling-down Mega Launch is dimmed with remaining seconds and a filling readiness line.

### PVP-only Airport settings

The following controls appear in the host's **F3** lobby panel only while **PVP** is selected:

- PVP death penalty and real-death destination.
- Optional opened-luggage refresh and its delay.
- Optional **Test mode** (off by default), which exposes host-only in-run controls for rerolling every active player's main Ability and giving every active player one Chaos charge.
- Relative weight for every main ability and every Chaos effect.
- Mega Launch cooldown and launch force.
- Hidden Mega Launch Food chances for the leader, middle, near-last, last-place and far-behind tiers.

These rules synchronize to all clients, remain stable if the host changes and lock when the run starts. Personal **F/C** key bindings are not controlled by the host.

### Hidden Mega Launch Food

Ordinary non-critical food from luggage can secretly become **Mega Launch Food** without changing its appearance, name or description. Default per-item chances are **1.5%** for the leader, **4%** for the middle, **7%** for near-last, **12.5%** for last place and **18%** when at least 1.5 segments behind the leader. Eating it reveals a five-second warning and launches the consumer without replacing or requiring a Campfire Ability.

During a run, the same configured key (**F3** by default) opens a separate host-only race controls panel in every respawn mode. Its confirmed **End current run** action uses PEAK's normal networked results flow, allowing an unfinished run to end in defeat and the existing room to return to the Airport without recreating the lobby. When PVP Test mode is enabled, this panel also has separate **Reroll all abilities** and **Give Chaos to all** actions. Guests cannot open or use this panel.

## Notes
Things to be aware of while using this mod:
- Your individual timer pauses while your selected progression scope still needs to complete a campfire, and resumes when that player/team/lobby completes it. PVP always uses the player's own campfire claim.
- Team scores and displayed team altitude are the averages of all members of your troop.
- A Scout checkpoint flag always resolves before the selected respawn mode. A successful flag revive adds no time penalty.
- PVP has its own configurable death penalty (default **0 minutes**). A blowgun knockout itself adds no time penalty; its punishment is the immediate return to the previous campfire.
- PVP real deaths can respawn at the previous campfire or after the configured corpse delay (default **30 seconds**). The corpse option uses the same team-colored countdown HUD as the standalone timed mode.
- PVP luggage refresh is disabled by default. When enabled, unclaimed old loot is removed as the chest closes; already collected items remain with their players. Respawn chests never refresh.
- Final-biome hazard clocks follow the selected waiting policy. Leaving, dying or respawning does not currently restart the relevant player/team/global timer.
- Falling into an older biome does not forget an earned checkpoint, but another team loading a biome does not grant access or a later respawn target to teams that have not completed its campfire.
- If a team is completely wiped while another team remains alive under a next-campfire death rule, the wiped team revives at its own last completed campfire (or the beach) without gaining progress. A complete lobby wipe still ends the run.
- The custom PVP blowgun is a distinct synchronized item and therefore requires the same mod version on every client.
- The respawn statues will no longer respawn players, and always default to giving an item.
- All racers should install the same mod version so timers and respawns remain deterministic.

This mod can be used standalone, but I intended for it to be played using:
[PEAK Unlimited](https://thunderstore.io/c/peak/p/glarmer/PEAK_Unlimited/) 

PEAK Unlimited is optional; no direct dependency is required.

This mod is also in active development. If you encounter bugs, issues, or would like to help with development. You can find me on<br>
 the Peak Discord Modding server or leaving an issue on github.

## Planned
- Add Localized Text.
- Finish adding emblems to each teams armbands.
- Add a table/desk/stand for armband selection.
- Add a toggle to turn team mode off or on from in lobby.
- Find a better way to handle or manage game shaders.
- Wait for all remaining players before ending game.
- Change survivors on helicoptor to winning team.
- Change game time to winning team time.
- ensure respawn totem respawns someone from the users troop.


## Images
here are some other images

![image](https://raw.githubusercontent.com/Raiderj9/RaceToThePEAK/refs/heads/master/Pictures/TroopArmbands.png)

![image](https://raw.githubusercontent.com/Raiderj9/RaceToThePEAK/refs/heads/master/Pictures/ScoutWithArmband.png)
