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
- Campfires can be activated by a single racer; completed biomes, old camps and passed boundaries remain available to racers behind them.
- Sun, sky, storms and Gloom visuals follow each client's observed racer, so unlocking a later biome does not obscure earlier racers' routes.
- Final rising lava or Gloom starts separately when each racer enters the final biome. Its timer is synchronized for spectating and does not reset after death.
- Previous-campfire respawns use the furthest lit camp personally reached by that racer, never the leader's global map progress.
- Four host-configurable respawn modes, available from the Airport with **F3** by default.
- Respawn statues always give items instead of reviving players.
- Scoutmaster spawning is disabled.
- Compatible with PEAK Unlimited and arbitrary lobby sizes.

## Respawn settings
The lobby host can press **F3** in the Airport to configure and synchronize the race rules:

1. **Next campfire**: add a configurable penalty (default **5 minutes**) and revive when the next campfire is activated.
2. **Timed at corpse**: add a configurable penalty (default **5 minutes**) and revive at the player's own corpse after a configurable delay (default **30 seconds**). A compact team-colored countdown is shown to the dead player and their teammates.
3. **Previous campfire**: immediately revive at the previous campfire with a configurable penalty (default **0 minutes**).
4. **PVP**: adds a crimson PVP blowgun with a matching inventory icon to ordinary luggage with a **50% drop chance**. Real deaths have their own lobby-selectable respawn strategy: previous campfire, timed at the corpse, or next activated campfire. If the special blowgun causes a scout to pass out, their pocket items drop and they are always sent immediately to the previous campfire without waiting to become bones, independently of the real-death setting. The normal blowgun and all other pass-out causes keep vanilla behavior. Optionally, every opened luggage chest can close and roll new loot after a host-configurable delay (default **5 minutes**).

Settings are locked after the race leaves the Airport. Only the lobby host can open the RaceToThePeak panel; guests receive the room's active settings silently. Its key is independently configurable as `UI.MenuKey` in RaceToThePeak's BepInEx config and defaults to **F3**, leaving PEAK Unlimited's **F2** menu completely untouched.

During a run, the same configured key (**F3** by default) opens a separate host-only race controls panel in every respawn mode. Its confirmed **End current run** action uses PEAK's normal networked results flow, allowing an unfinished run to end in defeat and the existing room to return to the Airport without recreating the lobby. Guests cannot open or use this panel.

## Notes
Things to be aware of while using this mod:
- Your individual timer pauses at an unlit campfire and resumes when it is activated. Racers arriving at an already-lit campfire keep timing normally.
- Team scores and displayed team altitude are the averages of all members of your troop.
- A Scout checkpoint flag always resolves before the selected respawn mode. A successful flag revive adds no time penalty.
- PVP has its own configurable death penalty (default **0 minutes**). A blowgun knockout itself adds no time penalty; its punishment is the immediate return to the previous campfire.
- PVP real deaths can respawn at the previous campfire, after the configured corpse delay (default **30 seconds**), or when the next campfire is activated. The corpse option uses the same team-colored countdown HUD as the standalone timed mode.
- PVP luggage refresh is disabled by default. When enabled, unclaimed old loot is removed as the chest closes; already collected items remain with their players. Respawn chests never refresh.
- Every racer receives the final biome's authored rising-hazard duration from their own first entry. Leaving, dying or respawning does not restart that timer.
- Falling into an older biome does not forget a personally reached campfire, but another racer unlocking a fire does not grant it to players who have never reached it.
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
