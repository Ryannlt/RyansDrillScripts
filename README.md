# Melee Drill System (MDS)

*A utility mod with commands and scripts to assist, enhance, and automate melee drills in **Holdfast: Nations At War***

---

## Command Quick Reference

Prefix all commands with `rc` and run as a logged-in server admin.

**Arena**

```
rc set ArenaCorner1
rc set ArenaCorner2
```

With coordinates: `rc set ArenaCorner1 [x,z]`, `rc set ArenaCorner2 [x,z]`

**Drills**

```
rc 3v3
rc groupfight
rc openmelee 2 7
```

**Bots**

```
rc bot spawn 5 French ArmyLineInfantry
rc bot summon French ArmyLineInfantry None Replace
rc bot remove all
rc summonLine 10 French ArmyLineInfantry
rc spawnLine -20 30 90 10 French ArmyLineInfantry
```

**Runtime Config**

```
rc get xvxDistance
rc get Players all
rc set xvxDistance 5
rc set lineBotCount 10
```

---

## Overview

MDS tracks round and player state to run custom drills and utility commands. Use standard `rc` commands and **modded config variables** to tailor drills to your server's needs.

---

## Quick Start (In‑Game)

1. **Set the arena corners** (use your current position if no coords given):

   ```
   rc set ArenaCorner1
   rc set ArenaCorner2
   ```
2. **Run a drill** (examples):

   ```
   rc 3v3
   rc groupfight
   rc openmelee 2 7
   ```
3. **Spawn bots** (examples):

   ```
   rc bot spawn French ArmyLineInfantry
   rc summonLine 5
   ```

> Commands are **case‑insensitive**. Arguments in `<>` are required; `[ ]` are optional.

---

## Arena

All drills require a rectangular **arena** defined by two corner points *(x,z)*.

* **Config variable:**

  * `mod_variable MDS:SetArena:(x,z),(x,z)`
* **Runtime via `rc`:**

  * `rc set ArenaCorner1 [x,z]` *(uses player pos if none given)*
  * `rc set ArenaCorner2 [x,z]` *(uses player pos if none given)*

---

## Available Commands

All commands must be prefixed with `rc` and require admin.

### `xvx`

**Usage:** `rc xvx <attacking:int> <defending:int> [strategy] [distance:float] [spacing:float] [orientation]`

* Spawns an X‑v‑X match inside the arena using the selected strategy and parameters.
* Shorthand calls are supported using defaults — e.g. `rc 3v2`, `rc 20v1`.
* **Defaults (configurable):** `xvxStrategy`, `xvxDistance`, `xvxSpacing`
* **Examples:**

  ```
  rc xvx 3 3 next 4 1 northsouth
  rc 3v3
  ```

### `groupfight`

**Usage:** `rc groupfight [strategy] [distance:float] [spacing:float] [orientation]`

* Spawns both teams in lines for a groupfight inside the arena.
* **Defaults (configurable):** `groupfightStrategy`, `groupfightDistance`, `groupfightSpacing`
* **Examples:**

  ```
  rc groupfight random 25 2 180
  rc groupfight
  ```

### `openmelee`

**Usage:** `rc openmelee [spacing:float] [offset:float]`

* Spawns players randomly around the arena to simulate an open melee.
* **Defaults (configurable):** `openMeleeSpacing`, `openMeleeOffset`
* **Examples:**

  ```
  rc openmelee 2 7
  rc openmelee
  ```

### `bot`

**Usage:** `rc bot <subcommand> [args]`

* Central command for spawning, summoning, and managing bots.
* Subcommands: `spawn`, `spawnRandom`, `summon`, `setBotAi`, `setBotDeathPolicy`, `remove`, `list`
* See **[Bot Subcommands](#bot-subcommands)** section for full details.
* **Examples:**

  ```
  rc bot spawn 3 French ArmyLineInfantry
  rc bot remove all
  rc bot list
  ```

### `summonLine`

**Usage:** `rc summonLine [count] [faction class] [ai] [death] [name [regtag [uniformId]]]`

* Spawns a shoulder‑to‑shoulder line of bots **centred on your position**, facing your direction.
* `count` overrides `lineBotCount` for this call; faction/class default to yours if omitted.
* **Defaults (configurable):** `lineBotCount`, `lineSpacing`, `botDefaultAi`, `botDefaultDeathPolicy`
* **Examples:**

  ```
  rc summonLine
  rc summonLine 5
  rc summonLine 8 French ArmyLineInfantry None Replace
  ```

### `spawnLine`

**Usage:** `rc spawnLine <x> <z> <rotation> [count] [faction class] [ai] [death] [name [regtag [uniformId]]]`

* Spawns a line of bots at world position `(x, z)` facing `rotation` degrees from North.
* `count` overrides `lineBotCount` for this call; faction/class default to caller's if omitted.
* **Defaults (configurable):** `lineBotCount`, `lineSpacing`, `botDefaultAi`, `botDefaultDeathPolicy`
* **Examples:**

  ```
  rc spawnLine -20 30 90 10 French ArmyLineInfantry
  rc spawnLine 0 50 0
  ```

### `get`

**Usage:** `rc get <Configurable> [additional arguments]`

* Mirrors Holdfast's `rc get` to read **mod configurables** and **mod data** at runtime.
* Additional mod data shortcuts:

  ```
  Player <playerId>
  Players <faction> (Attacking|Defending|Spectator|All) [count]
  Round
  ```
* **Examples:**

  ```
  rc get xvxDistance
  rc get Players all
  ```

### `set`

**Usage:** `rc set <Configurable> <Value> [additional arguments]`

* Mirrors Holdfast's `rc set` to set **mod configurables** at runtime.
* **Example:**

  ```
  rc set xvxDistance 5
  rc set lineBotCount 10
  rc set lineSpacing 0.55
  ```

---

## Bot Subcommands

All bot subcommands are accessed via `rc bot <subcommand> [args]`.

### `spawn`

**Usage:** `rc bot spawn [count] [faction class] [ai] [death] [name [regtag [uniformId]]]`

* Spawns one or more specific bots at a random server spawn point.
* `faction` and `class` default to the caller's current faction/class if omitted. Providing `faction` without `class` uses the caller's class.
* `ai` and `death` default to `botDefaultAi` and `botDefaultDeathPolicy`.
* Arguments are **strictly positional** — omit from the right, not the middle.
* **Examples:**

  ```
  rc bot spawn
  rc bot spawn 5
  rc bot spawn French ArmyLineInfantry
  rc bot spawn 3 French ArmyLineInfantry None Replace
  rc bot spawn 1 French ArmyLineInfantry None Replace Soldier 1stBattalion 14
  ```

### `spawnRandom`

**Usage:** `rc bot spawnRandom [count]`

* Spawns one or more bots with a **fully random** faction and class.
* AI and death policy default to `botDefaultAi` and `botDefaultDeathPolicy`.
* **Examples:**

  ```
  rc bot spawnRandom
  rc bot spawnRandom 5
  ```

### `summon`

**Usage:** `rc bot summon [faction class] [ai] [death] [name [regtag [uniformId]]]`

* Spawns a single bot **at your position**, facing your direction.
* Same faction/class/ai/death defaulting as `spawn` (no count — multiple bots would stack).
* **Examples:**

  ```
  rc bot summon
  rc bot summon French ArmyLineInfantry
  rc bot summon French ArmyLineInfantry None Replace
  ```

### `setBotAi`

**Usage:** `rc bot setBotAi <target> <ai>`

* Sets the AI behaviour for one or more tracked bots immediately.
* **Target:** `all`, `attacking`, `defending`, `<faction>` (e.g. `French`), or `<playerId>`
* **AI types:** `None` *(Phase 1 will add Dummy, Facing, Melee, etc.)*
* **Examples:**

  ```
  rc bot setBotAi all None
  rc bot setBotAi French None
  rc bot setBotAi 42 None
  ```

### `setBotDeathPolicy`

**Usage:** `rc bot setBotDeathPolicy <target> <policy>`

* Sets the death policy for one or more tracked bots.
* **Target:** `all`, `attacking`, `defending`, `<faction>`, or `<playerId>`
* **Policies:**
  * `None` — do nothing when the bot dies
  * `Kick` — kick the bot after `botKickDelay` seconds (lets the kill register)
  * `Replace` — kick then re‑spawn with the same identity (name, regtag, uniform, faction, class)
* **Examples:**

  ```
  rc bot setBotDeathPolicy all Kick
  rc bot setBotDeathPolicy French Replace
  rc bot setBotDeathPolicy 42 None
  ```

### `remove`

**Usage:** `rc bot remove <target>`

* Kicks (removes) one or more bots from the server immediately.
* **Target:** `all`, `attacking`, `defending`, `<faction>`, or `<playerId>`
* **Examples:**

  ```
  rc bot remove all
  rc bot remove French
  rc bot remove 42
  ```

### `list`

**Usage:** `rc bot list`

* Prints all currently tracked bots to your private messages.
* Shows player ID, faction/class, AI type, death policy, and spawn status.
* **Example:**

  ```
  rc bot list
  ```

---

## Configurables

*(Defaults shown are tuned for Palisade Arena A1.)*

* **ArenaCorner1** — x,z coordinate of the 1st corner of the arena play area.

  * **args:** `x z` (floats) or none (uses player position)
  * **default:** Not set
* **ArenaCorner2** — x,z coordinate of the 2nd corner of the arena play area.

  * **args:** `x z` (floats) or none (uses player position)
  * **default:** Not set
* **xvxDistance** — Distance between attacking and defending faction lines for `xvx`.

  * **args:** `distance (float)`
  * **default:** `20`
* **xvxSpacing** — Space between each player on a line for `xvx`.

  * **args:** `distance (float)`
  * **default:** `2`
* **xvxStrategy** — Player selection strategy for `xvx`.

  * **args:** `Random | Next | Any | Repeat`
  * **default:** `Random`
* **groupfightDistance** — Distance between attacking and defending faction lines for `groupfight`.

  * **args:** `distance (float)`
  * **default:** `25`
* **groupfightSpacing** — Space between each player on a line for `groupfight`.

  * **args:** `distance (float)`
  * **default:** `2`
* **groupfightStrategy** — Player selection strategy for `groupfight`.

  * **args:** `Random | Repeat`
  * **default:** `Random`
* **openMeleeSpacing** — Minimum distance players can spawn from each other for `openmelee`.

  * **args:** `distance (float)`
  * **default:** `1.5`
* **openMeleeOffset** — Minimum spawn distance from the arena edges in `openmelee`.

  * **args:** `distance (float)`
  * **default:** `7`
* **Orientation** — Direction two lines spawn facing each other.

  * **args:** `degree (int)` or `NorthSouth | EastWest | SouthNorth | WestEast | Random`
  * **default:** `90` (NorthSouth)
* **botDefaultAi** — Default AI behaviour assigned to bots that do not specify one inline.

  * **args:** `None`
  * **default:** `None`
* **botDefaultDeathPolicy** — Default death policy assigned to bots that do not specify one inline.

  * **args:** `None | Kick | Replace`
  * **default:** `Kick`
* **botKickDelay** — Seconds to wait after a bot dies before kicking it (allows the kill to register).

  * **args:** `seconds (float)`
  * **default:** `2`
* **botReplaceDelay** — Seconds to wait after kicking a bot before re‑spawning it (clears the slot).

  * **args:** `seconds (float)`
  * **default:** `0.5`
* **lineBotCount** — Default number of bots in a `summonLine` or `spawnLine` when count is not specified inline.

  * **args:** `count (int, > 0)`
  * **default:** `10`
* **lineSpacing** — Lateral spacing in metres between bots in a line. Tune until bots stand shoulder‑to‑shoulder.

  * **args:** `metres (float, > 0)`
  * **default:** `0.55`

---

## Mod Config Variables

Use **global** `mod_variable` or **per‑map** `mod_variable_local` to set MDS options in rotation configs.

**Format:** `MDS:<ConfigVariable>:<Argument(s)>`

### General

* **EnableDebugLogging** — `true | false`
* **EnableAdminOnly** — `true | false`

### Arena

* **SetArena** — `(x,z),(x,z)`
* **AddArena** — `(x,z),(x,z)`
* **SetArenaCorner1** — `x,z`
* **SetArenaCorner2** — `x,z`

### Drill

* **SetXvXDistance** — `distance(float)`
* **SetXvXSpacing** — `distance(float)`
* **SetXvXStrategy** — `Random | Next | Any | Repeat`
* **SetGroupfightDistance** — `distance(float)`
* **SetGroupfightSpacing** — `distance(float)`
* **SetGroupfightStrategy** — `Random | Repeat`
* **SetOpenMeleeSpacing** — `distance(float)`
* **SetOpenMeleeOffset** — `distance(float)`
* **SetOrientation** — `degree(int)` **or** `NorthSouth | EastWest | SouthNorth | WestEast | Random`

### Bot

* **SetBotDefaultAi** — `None`
* **SetBotDefaultDeathPolicy** — `None | Kick | Replace`
* **SetBotKickDelay** — `seconds(float)`
* **SetBotReplaceDelay** — `seconds(float)`

### Line

* **SetLineBotCount** — `count(int)`
* **SetLineSpacing** — `metres(float)`
* **SpawnLine** — `x,z,rotation[,count][,faction,class][,ai][,death]`

  Schedules a line of bots to spawn when the round begins. Can be specified multiple times for multiple lines. Faction/class/ai/death default to server config values if omitted.

  *Examples:*

  ```
  mod_variable_local MDS:SpawnLine:-20,30,90
  mod_variable_local MDS:SpawnLine:-20,30,90,10,French,ArmyLineInfantry,None,Replace
  ```

---

## Example Config

```text
# Global
mod_variable MDS:EnableDebugLogging:true
mod_variable MDS:EnableAdminOnly:true

# Map Rotation (per‑map overrides)
mod_variable_local MDS:SetArena:(-40.99,42.69),(-3.35,5.38)
mod_variable_local MDS:SetXvXDistance:20
mod_variable_local MDS:SetXvXSpacing:2
mod_variable_local MDS:SetXvXStrategy:Random
mod_variable_local MDS:SetGroupfightDistance:25
mod_variable_local MDS:SetGroupfightSpacing:2
mod_variable_local MDS:SetGroupfightStrategy:Random
mod_variable_local MDS:SetOpenMeleeSpacing:1.5
mod_variable_local MDS:SetOpenMeleeOffset:7
mod_variable_local MDS:SetOrientation:NorthSouth

# Bots
mod_variable_local MDS:SetBotDefaultDeathPolicy:Replace
mod_variable_local MDS:SetBotKickDelay:2
mod_variable_local MDS:SetBotReplaceDelay:0.5
mod_variable_local MDS:SpawnLine:-20,30,90,10,French,ArmyLineInfantry
mod_variable_local MDS:SpawnLine:20,30,270,10,British,ArmyLineInfantry
```

---

## Future Features

* Automatic repeating drills with user customization
* Modded UI
* Multi‑arena support
* Phase 1 bot AI (dummy stabber, facing bot, melee state machine)

---

## Support

Feedback & bugs: open a GitHub issue or DM **@ryanlt** on Discord.
