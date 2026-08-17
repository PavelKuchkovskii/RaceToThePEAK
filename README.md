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
- Every personal PVP campfire claim rolls a Campfire Ability. One main ability is stored at a time, while the last racer to each fire can also hold one separate Chaos charge.
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
4. **PVP**: adds a crimson PVP blowgun with a matching inventory icon to ordinary luggage with a **50% drop chance**. Real deaths can return to the previous campfire or use the corpse timer; next-fire respawning is unavailable because a dead player cannot personally claim that checkpoint. If the special blowgun causes a scout to pass out, their pocket items drop and they are sent immediately to the previous campfire without waiting to become bones. The normal blowgun and all other pass-out causes keep vanilla behavior. Optionally, every opened luggage chest can close and roll new loot after a host-configurable delay (default **5 minutes**).

Settings are locked after the race leaves the Airport. Only the lobby host can open the RaceToThePeak panel; guests receive the room's active settings silently. Its key is independently configurable as `UI.MenuKey` in RaceToThePeak's BepInEx config and defaults to **F3**, leaving PEAK Unlimited's **F2** menu completely untouched.

## Campfire progression settings

The same host panel provides three waiting policies for non-PVP respawn modes:

**PVP override:** the waiting selector is ignored and hidden in PVP. Every living racer activates each campfire independently. A globally loaded biome remains locally blocked for that racer until their own sequential claim is accepted; no teammate or lobby member can block the interaction.

1. **Wait for nobody**: outside PVP, the first living racer activates the campfire and loads the next biome for everyone. Other racers do not need to claim the same transition. Final rising hazards use separate player clocks.
2. **Wait for team**: one living teammate must be at the campfire, and every other connected teammate must be in range or actually dead. An unconscious teammate still counts as living. The next biome is loaded globally once, but its boundary opens locally only for teams that completed the campfire. Final rising hazards share one clock per team.
3. **Wait for lobby**: every connected living player must be in range, matching PEAK's lobby-wide progression. Final rising hazards retain their vanilla global synchronization.

Disconnected players and bots are excluded. A player who did not select a troop is treated as a one-person team for progression. Already-lit campfires remain logically claimable by later teams, so the first team never grants access to its competitors.

## PVP Campfire Abilities

Each personally completed campfire replaces the racer's previous main ability with one weighted random ability. Press **F4** by default to use an active main ability; passive abilities work automatically. Keys, Mega Launch tuning and every roll weight are configurable in RaceToThePeak's BepInEx config.

- **Adrenaline** applies the original Lollipop and Energy Drink effects together.
- **Shield** passively consumes itself to block the next directed Exhaust, Recall or Ghost Runner.
- **Exhaust** targets a random racer ahead and raises their stamina consumption by 40% for 8 seconds. It is not consumed when no target exists.
- **Second Wind** automatically recovers the owner from their next unconscious state and grants 2 seconds of protection.
- **Catch Up** is a passive race-progress boost. A smaller built-in boost also helps the current last-place racer even without holding the ability.
- **Recall** warns and returns the leader to their previous campfire only when the lead is sufficiently large.
- **Chaos Horn** removes bonus stamina from every other living non-ghost racer.
- **Ghost Runner** lets a ghost penalize the living racer they are spectating for 15 seconds.
- **Mega Launch** starts a five-second countdown, then applies a physical impulse in the current look direction. It is reusable after a configurable cooldown.

The last racer to personally activate each campfire also receives one separate **Chaos** charge, stored in addition to the main slot and activated with **F5** by default. Its configurable weighted roll can restore stamina, grant global Adrenaline, knock everyone unconscious, swap racers between recorded safe positions, or move racers to a previous or middle campfire. Only one Chaos charge can be stored.

Ordinary non-critical food from luggage can secretly become **Mega Launch Food** without changing its appearance, name or description. Its configurable per-item chance rises from **1.5%** for the leader to **18%** for a racer far behind. Eating it reveals a five-second warning and launches the consumer without replacing or requiring a Campfire Ability.

During a run, the same configured key (**F3** by default) opens a separate host-only race controls panel in every respawn mode. Its confirmed **End current run** action uses PEAK's normal networked results flow, allowing an unfinished run to end in defeat and the existing room to return to the Airport without recreating the lobby. Guests cannot open or use this panel.

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
