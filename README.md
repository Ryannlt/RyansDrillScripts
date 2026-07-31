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
rc bot setBotAi all Dueling
rc bot cfg 42 stabInterval 2.5
rc bot move all seek me
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
rc set globalAI StabbingDummy stabInterval 2.5
rc get globalAI StabbingDummy stabInterval
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
* Shorthand calls are supported using defaults. e.g. `rc 3v2`, `rc 20v1`.
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
* Subcommands: `spawn`, `spawnRandom`, `summon`, `setBotAi`, `setBotDeathPolicy`, `remove`, `list`, `move`, `cfg`
* Dev/probe tools: `probe`, `act`
* See **[Bot Subcommands](#bot-subcommands)** for full details and **[Bot AI Types](#bot-ai-types)** for the available AI behaviours.
* **Examples:**

  ```
  rc bot spawn 3 French ArmyLineInfantry
  rc bot remove all
  rc bot list
  ```

### `summonLine`

**Usage:** `rc summonLine [count] [faction] [class] [ai] [death] [name [regtag [uniformId]]]`

* Spawns a shoulder‑to‑shoulder line of bots **centred on your position**, facing your direction.
* `count` overrides `lineBotCount` for this call; faction/class default to yours if omitted.
* `faction` accepts `attacking` / `defending` (resolved to the round's factions) as well as a faction name.
* **Defaults (configurable):** `lineBotCount`, `lineSpacing`, `botDefaultAi`, `botDefaultDeathPolicy`
* **Examples:**

  ```
  rc summonLine
  rc summonLine 5
  rc summonLine 8 French ArmyLineInfantry None Replace
  ```

### `spawnLine`

**Usage:** `rc spawnLine <x> <z> <rotation> [count] [faction] [class] [ai] [death] [name [regtag [uniformId]]]`

* Spawns a line of bots at world position `(x, z)` facing `rotation` degrees from North.
* `count` overrides `lineBotCount` for this call; faction/class default to caller's if omitted.
* `faction` accepts `attacking` / `defending` (resolved to the round's factions) as well as a faction name.
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
* `faction` accepts `attacking` / `defending` (resolved to the round's factions) as well as a faction name (e.g. `French`).
* `ai` and `death` default to `botDefaultAi` and `botDefaultDeathPolicy`.
* Arguments are **strictly positional**: omit from the right, not the middle.
* **Examples:**

  ```
  rc bot spawn
  rc bot spawn 5
  rc bot spawn Attacking ArmyLineInfantry
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

**Usage:** `rc bot summon [faction] [class] [ai] [death] [name [regtag [uniformId]]]`

* Spawns a single bot **at your position**, facing your direction.
* Same faction/class/ai/death defaulting as `spawn`.
* **Examples:**

  ```
  rc bot summon
  rc bot summon French ArmyLineInfantry
  rc bot summon Defending ArmyLineInfantry None Replace
  ```

### `setBotAi`

**Usage:** `rc bot setBotAi <target> <ai>`

* Sets the AI behaviour for one or more tracked bots immediately.
* **Target:** `all`, `attacking`, `defending`, `<faction>` (e.g. `French`), or `<playerId>`
* **AI types:** `None`, `Manual`, `StabbingDummy`, `RiposteDummy`, `Dueling`: see **[Bot AI Types](#bot-ai-types)**.
* **Examples:**

  ```
  rc bot setBotAi all Dueling
  rc bot setBotAi French RiposteDummy
  rc bot setBotAi 42 StabbingDummy
  ```

### `setBotDeathPolicy`

**Usage:** `rc bot setBotDeathPolicy <target> <policy>`

* Sets the death policy for one or more tracked bots.
* **Target:** `all`, `attacking`, `defending`, `<faction>`, or `<playerId>`
* **Policies:**
  * `None`: do nothing, defaulting to in game handling (They respawn at a random spawn as a random class)
  * `Kick`: kick the bot after `botKickDelay` seconds (lets the kill register)
  * `Replace`: kick then re-spawn with the same identity (name, regtag, uniform, faction, class) at death location
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

### `move`

**Usage:** `rc bot move <target> <behavior> [args]`

* Drives bots that are on the **`Manual`** AI (set it first with `rc bot setBotAi <target> Manual`). Bots on other AIs are left untouched.
* **Target:** `all`, `attacking`, `defending`, `<faction>`, or `<playerId>`
* **Behaviors:**
  * `seek <dest>`: run toward a point/player
  * `arrive <dest>`: like seek, but decelerate to a smooth stop
  * `flee <dest>`: run directly away (`flee me facing me` = backpedal toward you)
  * `pursue <dest>`: lead a moving target to intercept it (predictive seek)
  * `evade <dest>`: flee from where a target is heading (predictive flee)
  * `wander`: roam continuously with gentle random turns
  * `facepoint <dest>`: rotate in place to face a point/player
  * `face <deg>`: rotate in place to a heading (degrees from North)
  * `stop`: halt movement
* **`<dest>`** = `x z` (two numbers), `<playerId>`, or `me`. A player or `me` destination is tracked live as they move.
* **Flags** (any combination, appended anywhere): `separate` (spread apart from other bots), `avoid` (steer around walls), `dodge` (steer around moving agents).
* **`facing <dest>`**: optional; decouples which way the bot faces from the way it travels.
* **Examples:**

  ```
  rc bot setBotAi all Manual
  rc bot move all seek me
  rc bot move all arrive 12 -4 facing me avoid
  rc bot move 42 flee me facing me
  rc bot move all wander separate
  ```

### `cfg`

**Usage:** `rc bot cfg <target> [<lever> <value>]`

* Sets or lists per-bot AI levers, a granular override for one bot or group on top of the global default. It only affects bots whose AI is configurable (`StabbingDummy`, `RiposteDummy`, and the `Dueling` tiers); others are skipped with a message.
* **Target:** `all`, `attacking`, `defending`, `<faction>`, or `<playerId>`
* With `<lever> <value>`: set that lever on the matching bots.
* Without a lever: list the matching bots' current levers and values.
* A lever's default comes from `globalAI`: see **[Bot AI Types](#bot-ai-types)**.
* **Examples:**

  ```
  rc bot cfg 42 stabInterval 2.5
  rc bot cfg all stabDirection High
  rc bot cfg 42
  ```

### `probe` & `act` *(dev tools)*

Diagnostic helpers used while building and validating AI behaviour. They have no effect on normal drills.

* **`rc bot probe <playerId|me> [on|off]`**: logs a player's melee packet actions and hurt events to the server log (to learn the input tokens and timings). Toggles if `on|off` is omitted. Works on any player, human or bot.
* **`rc bot act <playerId> <actionToken> [argument]`**: fires a single raw `carbonPlayers` playerAction at a player/bot (e.g. `MeleeBlockHigh`, `MeleeStrikeHigh`, `ExecuteMeleeWeaponStrike`). No AI required.

---

## Bot AI Types

Assign with `rc bot setBotAi <target> <ai>`, inline when spawning (e.g. `rc bot summon <faction> <class> <ai>`), or via the `botDefaultAi` default.

| AI | Behaviour |
| --- | --- |
| `None` | Does nothing. Stands where it spawned. |
| `Manual` | Manually driven with `rc bot move` (a movement test harness: seek, arrive, flee, pursue, evade, wander, face…). Issues no orders on its own. |
| `StabbingDummy` | Static training dummy. Stands facing its spawn direction and stabs on a fixed cadence for a player to practice blocking and attacking against. Aim it by facing the way you want when you summon it. Configurable (see below). |
| `RiposteDummy` | Reactive melee. Stands its ground, blocks, and only counters once provoked, never throwing first. A patient sparring partner: walk up and attack it, and it blocks and ripostes. |
| `DuelingEasy` / `DuelingNormal` / `Dueling` | Sentry melee at three difficulty tiers. Each stays passive, reading and blocking the closest player in range, until a player attacks it and it blocks the hit; then it locks onto that attacker and fights to the death, returning to passive when the target dies (a replacement starts passive again). They only respond to human players (`ignoreBots`). The tiers differ only in reaction speed: `DuelingEasy` is sluggish and beatable, `DuelingNormal` is human, and plain `Dueling` is the hardest, with instant blocks and ripostes. |

`RiposteDummy` and the `Dueling` tiers are presets of one configurable melee AI: the same behaviour with different capability toggles (`press`, `riposte`, `move`, `pursue`, `engageOnAttack`) and tuning. `StabbingDummy` is a separate static-stabber AI. You can tweak any of them per bot with `rc bot cfg`.

### Configurable AI levers

Some AIs expose named **levers** you can tune. A lever starts at its built-in default, is overridden by the global default if one is set, and is overridden again by a per-bot value.

* **Per-bot override**: `rc bot cfg <target> <lever> <value>` (that bot/group only). List with `rc bot cfg <target>`.
* **Global default**: `rc set globalAI <AiType> <lever> <value>`; new bots of that AI start from it. Read with `rc get globalAI <AiType> <lever>`, and persist it in a rotation config with the `SetGlobalAi` config variable.

Changing a global default affects **newly created** bots of that AI, not ones already spawned.

**`StabbingDummy` levers**

| Lever | Values | Default | Meaning |
| --- | --- | --- | --- |
| `stabInterval` | float > 0 (seconds) | `1.7` | Delay between stabs. |
| `stabDirection` | `Random` / `High` / `Low` / `Alternate` | `Random` | Which way each stab is thrown (e.g. `High` to drill high blocks). |

*Example: a slow, high-only dummy.*

```
rc bot summon Defending ArmyLineInfantry
rc bot setBotAi <id> StabbingDummy
rc bot cfg <id> stabInterval 3
rc bot cfg <id> stabDirection High
```

**`RiposteDummy` / `Dueling*` levers**

`RiposteDummy` and the three `Dueling` difficulty tiers are presets of one melee AI and share the same levers. The `Dueling` tiers are identical except for their reaction speeds. Booleans are `true`/`false` (`on`/`off` still accepted as input).

**Toggles & targeting** (the `Dueling` tiers share one column):

| Setting | `RiposteDummy` | `Dueling*` | Meaning |
| --- | --- | --- | --- |
| `press` | `false` | `true` | Throw the first blow when the enemy isn't threatening. |
| `riposte` | `true` | `true` | Counter after the guard absorbs a hit. |
| `move` | `false` | `true` | Hold/adjust melee spacing vs. stand its ground. |
| `pursue` | `false` | `true` | Advance toward a target that's too far, versus only holding or backing off. `false` lets a player back away and disengage instead of being followed. |
| `stickyTarget` | `false` | `false` | Keep one target while valid, versus re-picking the closest each tick. |
| `targetRange` | `3` | `3` | Only engage players within this many metres (`0` = unlimited); drops the target past it. For the `Dueling` tiers this is the passive read and provoke range. |
| `engageOnAttack` | `false` | `true` | Start passive (block only, with `press`, `riposte`, and `pursue` suppressed) and engage only a player whose attack it blocks, a hit aimed at it rather than anyone swinging nearby, fighting that target until it dies, then returning to passive. |

**Difficulty**: the reaction levers that separate the three `Dueling` tiers (`seconds ≥ 0`):

| Lever | `RiposteDummy` | `DuelingEasy` | `DuelingNormal` | `Dueling` | Meaning |
| --- | --- | --- | --- | --- | --- |
| `blockReactionMin` | `0.1` | `0.3` | `0.1` | `0` | Min delay between reading an attack and raising the guard, where `0` is instant. This is the main difficulty knob. |
| `blockReactionMax` | `0.2` | `0.5` | `0.2` | `0` | Max of that delay. Each block picks a random value in the min to max range. |
| `riposteReactionMin` | `0` | `0.2` | `0` | `0` | Min delay between a block landing and the counter. |
| `riposteReactionMax` | `0.5` | `0.8` | `0.5` | `0` | Max of that delay. |
| `attackReadBeat` | `0.6` | `0.9` | `0.6` | `0.3` | Extra randomised beat added to the attack cooldown (pacing; lower = presses faster). |

**Shared tuning**: same defaults across all these presets (`seconds ≥ 0` or `metres`, floats):

| Lever | Default | Meaning |
| --- | --- | --- |
| `riposteWindow` | `0.6` | How long the post-block counter stays available (seconds). |
| `offensiveRange` | `0.7` | Close spacing it presses to (metres). |
| `offensiveRangeVariance` | `0.1` | Random jitter added on top of `offensiveRange`. |
| `defensiveRange` | `2.0` | Reading spacing it guards from (metres). |
| `defensiveRangeVariance` | `0.4` | Random jitter added on top of `defensiveRange`. |
| `attackRange` | `2.0` | How close before a press attack (throwing first) commits a stab, in metres. A riposte ignores this and always throws at the target, so a stationary bot still counters an attacker who backed off. |
| `ignoreTeam` | `true` | Target any player regardless of faction (`false` = enemies only). Defaults to `true` so you don't have to be on the opposing team to use a bot. |
| `ignoreBots` | `true` | Target only human players, skipping bots. Defaults to `true` so bots focus the player and don't provoke each other. |
| `passiveRange` | `0.6` | `Dueling` tiers only: the hold distance while waiting. It's small so the bot stands its ground instead of backing off to `defensiveRange` from an approaching player; it uses `defensiveRange` once engaged. |

**Attacker-lock** (automatic, no lever): once a player within melee range begins a strike, the bot locks onto them through the exchange, including its riposte, regardless of who else is closer, so it can't be pulled off an attacker mid-fight.

*Example: pick a difficulty out of the box, or fine-tune one lever.*

```
rc bot summon Defending ArmyLineInfantry DuelingEasy
rc bot cfg <id> blockReactionMax 0.6   # make this one even slower
```

---

## Configurables

*(Defaults shown are tuned for Palisade Arena A1.)*

* **ArenaCorner1**: x,z coordinate of the 1st corner of the arena play area.

  * **args:** `x z` (floats) or none (uses player position)
  * **default:** Not set
* **ArenaCorner2**: x,z coordinate of the 2nd corner of the arena play area.

  * **args:** `x z` (floats) or none (uses player position)
  * **default:** Not set
* **xvxDistance**: Distance between attacking and defending faction lines for `xvx`.

  * **args:** `distance (float)`
  * **default:** `20`
* **xvxSpacing**: Space between each player on a line for `xvx`.

  * **args:** `distance (float)`
  * **default:** `2`
* **xvxStrategy**: Player selection strategy for `xvx`.

  * **args:** `Random | Next | Any | Repeat`
  * **default:** `Random`
* **groupfightDistance**: Distance between attacking and defending faction lines for `groupfight`.

  * **args:** `distance (float)`
  * **default:** `25`
* **groupfightSpacing**: Space between each player on a line for `groupfight`.

  * **args:** `distance (float)`
  * **default:** `2`
* **groupfightStrategy**: Player selection strategy for `groupfight`.

  * **args:** `Random | Repeat`
  * **default:** `Random`
* **openMeleeSpacing**: Minimum distance players can spawn from each other for `openmelee`.

  * **args:** `distance (float)`
  * **default:** `1.5`
* **openMeleeOffset**: Minimum spawn distance from the arena edges in `openmelee`.

  * **args:** `distance (float)`
  * **default:** `7`
* **Orientation**: Direction two lines spawn facing each other.

  * **args:** `degree (int)` or `NorthSouth | EastWest | SouthNorth | WestEast | Random`
  * **default:** `90` (NorthSouth)
* **botDefaultAi**: Default AI behaviour assigned to bots that do not specify one inline.

  * **args:** `None | Manual | StabbingDummy | RiposteDummy | DuelingEasy | DuelingNormal | Dueling` (see [Bot AI Types](#bot-ai-types))
  * **default:** `None`
* **botDefaultDeathPolicy**: Default death policy assigned to bots that do not specify one inline.

  * **args:** `None | Kick | Replace`
  * **default:** `Kick`
* **botKickDelay**: Seconds to wait after a bot dies before kicking it (allows the kill to register).

  * **args:** `seconds (float)`
  * **default:** `2`
* **botReplaceDelay**: Seconds to wait after kicking a bot before re‑spawning it (clears the slot).

  * **args:** `seconds (float)`
  * **default:** `0.5`
* **globalAI**: Global **default** value for a configurable AI lever (per-bot overrides are the separate `rc bot cfg` command). Reads/writes one lever at a time. See [Configurable AI levers](#configurable-ai-levers).

  * **set args:** `<AiType> <lever> <value>`: e.g. `rc set globalAI StabbingDummy stabInterval 2.5`
  * **get args:** `<AiType> <lever>`: e.g. `rc get globalAI StabbingDummy stabInterval`
  * **default:** each AI's built-in lever values (e.g. `StabbingDummy stabInterval` = `1.7`)
* **lineBotCount**: Default number of bots in a `summonLine` or `spawnLine` when count is not specified inline.

  * **args:** `count (int, > 0)`
  * **default:** `10`
* **lineSpacing**: Lateral spacing in metres between bots in a line. Set so bots will be shoulder to shoulder.

  * **args:** `metres (float, > 0)`
  * **default:** `0.55`

---

## Mod Config Variables

Use **global** `mod_variable` or **per‑map** `mod_variable_local` to set MDS options in rotation configs.

**Format:** `MDS:<ConfigVariable>:<Argument(s)>`

### General

* **EnableDebugLogging**: `true | false`
* **EnableAdminOnly**: `true | false`

### Arena

* **SetArena**: `(x,z),(x,z)`
* **AddArena**: `(x,z),(x,z)`
* **SetArenaCorner1**: `x,z`
* **SetArenaCorner2**: `x,z`

### Drill

* **SetXvXDistance**: `distance(float)`
* **SetXvXSpacing**: `distance(float)`
* **SetXvXStrategy**: `Random | Next | Any | Repeat`
* **SetGroupfightDistance**: `distance(float)`
* **SetGroupfightSpacing**: `distance(float)`
* **SetGroupfightStrategy**: `Random | Repeat`
* **SetOpenMeleeSpacing**: `distance(float)`
* **SetOpenMeleeOffset**: `distance(float)`
* **SetOrientation**: `degree(int)` **or** `NorthSouth | EastWest | SouthNorth | WestEast | Random`

### Bot

* **SetBotDefaultAi**: `None | Manual | StabbingDummy | RiposteDummy | DuelingEasy | DuelingNormal | Dueling`
* **SetBotDefaultDeathPolicy**: `None | Kick | Replace`
* **SetBotKickDelay**: `seconds(float)`
* **SetBotReplaceDelay**: `seconds(float)`
* **SetGlobalAi**: `<AiType>,<lever>,<value>`

  Sets a global default for one configurable AI lever at map load, the persistent form of `rc set globalAI`. Repeat it for each lever. Per-bot `rc bot cfg` still overrides. Spaces also work in place of commas.

  *Examples:*

  ```
  mod_variable_local MDS:SetGlobalAi:StabbingDummy,stabInterval,2.5
  mod_variable_local MDS:SetGlobalAi:StabbingDummy,stabDirection,High
  ```
* **SpawnBot**: `x,z,rotation[,faction][,class][,ai][,death][,name[,regtag[,uniformId]]]`

  Schedules a single bot to spawn at a world position when the round begins. Specify it multiple times for multiple bots. Same fields as `SpawnLine` but with no `count`; use `SpawnLine` when you want more than one bot.
  * `x,z,rotation`: required. World position and facing (degrees from North).
  * `faction`: optional. `attacking` (default), `defending`, or a faction name (e.g. `French`). `attacking`/`defending` resolve against the live round at spawn time, so the same config works across maps.
  * `class`: optional. Defaults to `ArmyLineInfantry`.
  * `ai,death`: optional. Default to `botDefaultAi` / `botDefaultDeathPolicy`.
  * `name,regtag,uniformId`: optional identity extras.

  *Examples:*

  ```
  mod_variable_local MDS:SpawnBot:-20,30,90
  mod_variable_local MDS:SpawnBot:12,-4,180,defending,ArmyLineInfantry,Dueling,Replace
  ```

### Line

* **SetLineBotCount**: `count(int)`
* **SetLineSpacing**: `metres(float)`
* **SpawnLine**: `x,z,rotation[,count][,faction][,class][,ai][,death][,name[,regtag[,uniformId]]]`

  Schedules a shoulder‑to‑shoulder bot line to spawn when the round begins. Specify it multiple times for multiple lines (e.g. two opposing lines). Mirrors the `rc spawnLine` grammar, with map‑load defaults instead of a caller:
  * `x,z,rotation`: required. World position and facing (degrees from North).
  * `count`: optional. Defaults to `lineBotCount`.
  * `faction`: optional. `attacking` (default), `defending`, or a faction name (e.g. `French`). `attacking`/`defending` resolve against the live round at spawn time, so the same config works across maps.
  * `class`: optional. Defaults to `ArmyLineInfantry`.
  * `ai,death`: optional. Default to `botDefaultAi` / `botDefaultDeathPolicy`.
  * `name,regtag,uniformId`: optional identity extras.

  *Examples:*

  ```
  mod_variable_local MDS:SpawnLine:-20,30,90
  mod_variable_local MDS:SpawnLine:-20,30,90,French
  mod_variable_local MDS:SpawnLine:20,30,270,10,defending,ArmyLineInfantry,None,Replace,Bot,None,1
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
mod_variable_local MDS:SetBotDefaultAi:Dueling
mod_variable_local MDS:SetBotDefaultDeathPolicy:Replace
mod_variable_local MDS:SetBotKickDelay:2
mod_variable_local MDS:SetBotReplaceDelay:0.5
mod_variable_local MDS:SetGlobalAi:StabbingDummy,stabInterval,2.5
mod_variable_local MDS:SpawnBot:0,0,90,defending,ArmyLineInfantry,Dueling,Replace
mod_variable_local MDS:SpawnLine:-20,30,90,10,attacking,ArmyLineInfantry
mod_variable_local MDS:SpawnLine:20,30,270,10,defending,ArmyLineInfantry,None,Replace,Bot,None,1
```

---

## Future Features

* Automatic repeating drills with user customization
* Modded UI
* Multi-arena support
* More melee AI depth (feints, spins, advanced movement) and difficulty scaling
* Saved preset bundles, config-at-summon, and a supervisor layer for target/session control

---

## Support

Feedback & bugs: open a GitHub issue or DM **@ryanlt** on Discord.
