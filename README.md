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
rc bot summonAt 42 Dueling
rc bot setBotAi all Dueling
rc bot cfg 42 stabInterval 2.5
rc bot move all seek me
rc bot remove all
rc summonLine 10 French ArmyLineInfantry
rc summonLineAt 42 10
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
* Subcommands: `spawn`, `spawnRandom`, `summon`, `summonAt`, `setBotAi`, `setBotDeathPolicy`, `remove`, `list`, `move`, `cfg`
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

### `summonLineAt`

**Usage:** `rc summonLineAt <playerId> [count] [faction] [class] [ai] [death] [name [regtag [uniformId]]]`

* Same as `summonLine`, but the line is **centred on another player**, facing their direction.
* Faction and class default to **that player's**, not yours.
* Works while you're in free roam or spectating, where the server never learns your own position.
* Everything after the id follows the same grammar as `summonLine`. Replies and errors are sent to you, not to the target.
* **Examples:**

  ```
  rc summonLineAt 42
  rc summonLineAt 42 10
  rc summonLineAt 42 8 French ArmyLineInfantry None Replace
  ```
* **Bodyguards:** with the `Guardian` AI the whole line escorts the target, so this is the quickest way to put a detail around someone.

  ```
  rc summonLineAt 42 6 Guardian Replace
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

### `summonAt`

**Usage:** `rc bot summonAt <playerId> [faction] [class] [ai] [death] [name [regtag [uniformId]]]`

* Spawns a single bot **at another player's position**, facing their direction.
* Faction and class default to **that player's**, not yours, so dropping a bot on someone needs nothing but their id.
* Works while you're in free roam or spectating, where the server never learns your own position and plain `summon` has nothing to place the bot at.
* Everything after the id follows the same positional grammar as `summon`. Replies and errors are sent to you, not to the target.
* **Examples:**

  ```
  rc bot summonAt 42
  rc bot summonAt 42 French ArmyLineInfantry
  rc bot summonAt 42 Defending ArmyLineInfantry Dueling Replace
  ```
* **Bodyguards:** summoning the `Guardian` AI this way makes the target the player it guards, so repeating the command builds an escort around them.

  ```
  rc bot summonAt 42 Guardian Replace
  rc bot summonAt 42 Guardian Replace
  ```

### `setBotAi`

**Usage:** `rc bot setBotAi <target> <ai>`

* Sets the AI behaviour for one or more tracked bots immediately.
* **Target:** `all`, `attacking`, `defending`, `<faction>` (e.g. `French`), or `<playerId>`
* **AI types:** `None`, `Manual`, `StabbingDummy`, `RiposteDummy`, `Dueling`, `Group`, `Guardian`, `Test`: see **[Bot AI Types](#bot-ai-types)**.
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

* Sets or lists per-bot AI levers, a granular override for one bot or group on top of the global default. It only affects bots whose AI is configurable (`StabbingDummy`, `RiposteDummy`, and the `Dueling` and `Group` tiers); others are skipped with a message.
* **Target:** `all`, `attacking`, `defending`, `<faction>`, or `<playerId>`
* With `<lever> <value>`: set that lever on the matching bots.
* Without a lever: list the matching bots' current levers and values. Levers that depend on a switched-off lever are listed separately after `| inactive:`, each tagged with what it is waiting on, e.g. `breakoffRange=6(needs breakoff)`. That tail is how you tell at a glance which levers a given preset is actually using: a `RiposteDummy` shows most of the list dormant, a `Group` bot only the guard levers.
* Each tag names the **nearest** thing to switch on, not the root of the chain, so it is always something you can act on now. `breakoffRange` says `needs breakoff` while breakoff is off, then `needs post` once you turn breakoff on.
* **Setting a dormant lever is allowed and still reports what happened.** Order of commands shouldn't matter, so the value is stored either way, but the reply says so: *"Set 'breakoffRange' = '8' on 1 bot(s). Note: it does nothing while 'breakoff' is false."* Turn the dependency on and it takes effect with the value already in place.
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
| `Guardian` | Bodyguard. Escorts the player it was summoned onto, holding station beside them and staying out of trouble until an enemy comes within `guardRange` (10m) of them or they get into melee themselves, then it fights like a duellist. It takes sides from the **guarded player**, not from its own body, so it never turns on them even if the game spawned it on the wrong team. It also keeps its distance from other bots, so a group of guards is less likely to cut each other down. |
| `Test` | Development sandbox, not a finished behaviour. Carries `Dueling`'s levers so whatever is being worked on is measured against a known baseline, plus the feature under construction. Right now that is **squad** fighting without the station cycle: summon two in one command and they hold a fixed point and rotate around it so the enemy always faces the gap between them, and hold fire when a squadmate is in the swing line. Expect it to change. |
| `DuelingEasy` / `DuelingNormal` / `Dueling` | Sentry melee at three difficulty tiers. Each stays passive, reading and blocking the closest player in range, until a player attacks it and it blocks the hit; then it locks onto that attacker and fights to the death, returning to passive when the target dies (a replacement starts passive again). They only respond to human players (`ignoreBots`). The tiers differ only in reaction speed: `DuelingEasy` is sluggish and beatable, `DuelingNormal` is human, and plain `Dueling` is the hardest, with instant blocks and ripostes. Duel bots still **fight as individuals** — they do not share reads or time their stabs together — but several who are *attacked by the same player* will take up a formation so they stop crowding and stop cutting each other down. Spacing, not teamwork, and only once someone actually swings at them. They also hold a post: they wait on the mark you put them on, hold where a bout ended for `returnDelay` so you can go again immediately, then walk back. |
| `GroupEasy` / `GroupNormal` / `Group` | Drill station for practising being outnumbered, at the same three difficulty tiers as the `Dueling` family. See **[Group drill stations](#group-drill-stations)** below: stand a batch up somewhere, and it waits until a player attacks one of them, backs off to re-form, fights as a coordinated formation, and returns to its post afterwards ready for the next player. |

`RiposteDummy` and the `Dueling` and `Group` tiers are presets of one configurable melee AI: the same behaviour with different capability toggles (`press`, `riposte`, `move`, `pursue`, `engageOnAttack`, `squad`, `post`) and tuning. `StabbingDummy` is a separate static-stabber AI. You can tweak any of them per bot with `rc bot cfg`.

### Group drill stations

A `Group` bot is for practising 2v1s and 3v1s on your own. Summon a batch and leave it:

```
rc summonLine 2 Defending ArmyLineInfantry Group Replace
```

**A batch is a station; a formation is who you are fighting beside.** Bots from one `summonLine`, `spawnLine`, or `rc bot summon <count>` share a post, wake together, and regroup together for life — that is the batch, and a `Replace` bot rejoins the one it came from, so a station survives being used.

Standing in a line together is separate. Once batches are actually fighting, any that are engaged on the **same player** merge into a single formation: one line, shared guard, alternating stabs, lane discipline across all of them. So two `Group` bots summoned by two separate commands each keep their own post and wake independently, but the moment they end up on the same target they fight as a proper pair, and they split back to their own posts afterwards. Only engaged batches merge — waiting, backing off and withdrawing are all measured against a batch's own post, and two groups re-forming on different posts have nothing to share.

**Gathering bots is a way to build a group without commands.** Walk three separately summoned `Group` bots onto the same player and they become a real trio: one line, shared guard, alternating stabs, and one lifecycle. Provoking any of them wakes all of them. `minMembers` counts the *assembled* size, so that trio plays as a 3v1 that carries on as a 2v1 and resets when it would become a 1v1, exactly like a summoned three. `holdReplacement` applies to the group as a whole.

They stay together through the bout and through `returnDelay`. Only when the delay runs out and they head home does the formation disband — and then each bot walks back to **its own** post, because a batch never stops owning where its bots came from. Meet again on the same player and they form up fresh.

So `summonLine 3` and three separate summons that converge end up equivalent while they are together. The batch decides where each bot lives; the formation decides how they fight.

**The cycle**, once stood up:

1. **Waiting.** They hold their posts, facing the way they were set up, watching and blocking anyone nearby but never attacking. Bystanders can walk past without setting them off.
2. **Provoked.** Attack any one of them and the whole group wakes onto whoever did it, not just the one you hit.
3. **Backing off.** They retreat together to `breakoffRange` and re-form before throwing anything. This is deliberate: the fight for stab priority out of range is most of the skill, and starting from the activation stab would skip it. It gives up after five seconds if you body-block a bot to stop it reaching its slot. Turn the `breakoff` lever off and they skip this, piling straight in from where you hit them.
4. **Fighting.** The formation holds a point and rotates around it so you always face the gap between them, they share their guard so blocking one frees both to counter, and they throw opposite stabs.
5. **Re-arming.** Die or walk more than `resetRange` from the post and they return to it and go quiet, ready for the next attempt. Dying counts the moment you do, not when your body clears, so respawning near a station does not put you straight back under attack.

By default, killing one of them mid-bout leaves the rest fighting on and the replacement rejoins as soon as it lands, which makes a decent endurance grind. `minMembers` and `holdReplacement` together give the other reading, the one humans use: the drill runs shorthanded until it is no longer the drill, then everyone resets. For a 3v1 that means `minMembers 2` and `holdReplacement true` — kill one and you are in a real 2v1 until either you die or a second one does.

**Breaking off is not the same as switching off.** Whenever a group disengages while you are still alive — because it lost a member, or because you walked past `resetRange` — it *withdraws* rather than going straight back to sleep: it re-forms on the spot, facing you, blocking reliably, but it will not swing and will not pick up a new target. It goes quiet once it is back in formation, you die or leave, or ten seconds pass, so keeping one busy cannot hold the station shut.

**Losing neither sends them home nor gives you ground.** A group that breaks off re-forms exactly where the bout ended, the same as when it wins, and only then does `returnDelay` decide whether it walks back to the post. It deliberately does not retreat first: ground given up on a loss is never taken back, so every win would have walked the drill a few metres further off and you could never hold a spot to fight on. Standing its ground also gets it re-armed sooner, so you can go again immediately.

It keeps whatever bearing it came to rest facing — a formation only realigns to how the station was originally set up if it actually returns to the post — and it turns to face where it is running on the way there rather than shuffling home sideways.

The guard is dependable in every state except an actual fight — waiting, backing off and withdrawing all use `passiveBlockReaction`, instant by default. Difficulty belongs to the fight; the rest of the cycle should not be an exploit window.

A bot also will not swing for a moment after spawning, which stops a replacement from throwing a broken-looking attack the instant it appears next to whoever just killed it.

A group wakes on either a hit its guard absorbs **or** a member being killed, so an opening stab clean enough to drop a bot before its guard comes up still raises the alarm. That matters most at `GroupEasy`, which is slow enough to be killed outright. A bot that kills one of its own group never triggers this — formations exist to cut down friendly fire, and answering it with a revenge order would be the opposite.

**Stations without a group.** The station behaviour is the `post` lever, independent of `squad`, so a single bot of any melee preset can hold one. Both it and `breakoff` are off everywhere except the `Group` tiers, so nothing changes unless you ask for it:

```
rc bot summon Defending ArmyLineInfantry Dueling Replace
rc bot cfg <id> post true
rc bot cfg <id> breakoff true
```

That gives a 1v1 sparring partner that waits on its mark, resets the distance when you attack it, fights you as an ordinary duellist, and walks back to the same spot afterwards whether it won or you killed it. The two levers are separate, so `post` alone is a bot that simply returns to its mark between bouts. A lone bot never forms up, so it keeps its own spacing once the fight is on rather than holding a slot; only the waiting, the back-off and the walk home come from the station. `resetRange` works the same way, ending the bout when you leave.

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

`RiposteDummy` and the `Dueling` and `Group` difficulty tiers are presets of one melee AI and share the same levers. Within each family the tiers are identical except for their reaction speeds, and the `Group` tiers are the `Dueling` tiers plus `squad` and `post`. Booleans are `true`/`false` (`on`/`off` still accepted as input).

**Toggles & targeting** (the `Dueling` and `Group` tiers share one column):

| Setting | `RiposteDummy` | `Dueling*` / `Group*` | Meaning |
| --- | --- | --- | --- |
| `press` | `false` | `true` | Throw the first blow when the enemy isn't threatening. |
| `riposte` | `true` | `true` | Counter after the guard absorbs a hit. |
| `move` | `false` | `true` | Hold/adjust melee spacing vs. stand its ground. |
| `pursue` | `false` | `true` | Advance toward a target that's too far, versus only holding or backing off. `false` lets a player back away and disengage instead of being followed. |
| `stickyTarget` | `false` | `false` | Keep one target while valid, versus re-picking the closest each tick. |
| `targetRange` | `3` | `3` | Only engage players within this many metres (`0` = unlimited); drops the target past it. For the `Dueling` tiers this is the passive read and provoke range. |
| `engageOnAttack` | `false` | `true` | Start passive (block only, with `press`, `riposte`, and `pursue` suppressed) and engage only a player whose attack it blocks, a hit aimed at it rather than anyone swinging nearby, fighting that target until it dies, then returning to passive. |

**Difficulty** comes in two halves. Reaction beats decide how fast a bot answers you; the formation levers above (`coordinate`, `slotError`, `formationLag`) decide how well it holds its place beside another bot. The second half matters more in a group: reactions alone would give you a beatable pair that still stands in a perfect line and throws perfectly opposite stabs, and there is no answer to that.

Every formation lever is **the worst a bot may be, not how bad it is** — each roll runs from zero up to the lever, so a bot can always come out correct by chance and a lower tier simply does so less often. That is deliberate: a pair reliably half a metre too wide is a puzzle you solve once and exploit forever, while a pair *usually* too wide has to be read every time, and occasionally punishes you for assuming the gap was there. `squadSpacing` stays at the correct `0.9` for every tier — the base formation is always the right one, and difficulty is only how far they stray from it.

**Reaction beats**: the levers that separate the tiers (`seconds ≥ 0`). The `Group` tiers use the same three columns as their `Dueling` counterparts:

| Lever | `Guardian` | `RiposteDummy` | `DuelingEasy` / `GroupEasy` | `DuelingNormal` / `GroupNormal` | `Dueling` / `Group` | Meaning |
| --- | --- | --- | --- | --- | --- | --- |
| `blockReactionMin` | `0.5` | `0.1` | `0.3` | `0.1` | `0` | Min delay between reading an attack and raising the guard, where `0` is instant. This is the main difficulty knob. |
| `blockReactionMax` | `0.8` | `0.2` | `0.5` | `0.2` | `0` | Max of that delay. Each block picks a random value in the min to max range. |
| `riposteReactionMin` | `0.4` | `0` | `0.2` | `0` | `0` | Min delay between a block landing and the counter. |
| `riposteReactionMax` | `1.1` | `0.5` | `0.8` | `0.5` | `0` | Max of that delay. |
| `attackReadBeat` | `1.2` | `0.6` | `0.9` | `0.6` | `0.3` | Extra randomised beat added to the attack cooldown (pacing; lower = presses faster). |

`Guardian` sits deliberately below `DuelingEasy`: a guard should buy its ward a moment and lose to a competent player, and a whole detail of them would be miserable to fight otherwise.

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
| `guard` | `false` (`true` for `Guardian`) | Act as an escort for `guardTarget`. Every summon hands the bot a guard target, so this is what decides whether it acts on one: turn it on to make any melee bot escort whoever summoned it. |
| `guardTarget` | `0` | The player id this bot escorts, `0` for none. Set automatically by `summon` / `summonLine` (the caller) and `summonAt` / `summonLineAt` (the target); set it later on a whole squad with `rc bot cfg <target> guardTarget <playerId>`. Only has an effect while `guard` is on. |
| `guardRange` | `10` | `Guardian`: an enemy this close **to the guarded player** pulls the bot into the fight. It returns to escorting once the enemy is clear of this range again. |
| `guardFollowRange` | `3` | `Guardian`: how far from the guarded player the bot holds station while nothing is happening. |
| `separationRange` | `0.8` for `Dueling*` / `Group*` / `Test`, `1.5` for `Guardian`, `0` otherwise | Push apart from other bots within this many metres, `0` to disable. Keeps bots from stacking up and swinging through each other. Kept just under `squadSpacing` for the formation presets so it only resists overlap and does not fight the formation itself; it applies while a bot is moving on its own, not while it is holding a slot. |
| `squad` | `true` for `Dueling*` / `Group*` / `Test`, `false` otherwise | Stand in a formation: with the rest of its spawn batch, and with any other engaged batch that is on the same player, so separately summoned bots still form up when they converge. On its own this buys **spacing and lane discipline only** — bots take a slot instead of crowding, and hold a swing that would go through a squadmate. It does nothing to a bot fighting alone, since a formation of one has no slot to hold. The squad keeps a point that members stand on a circle around, with the line they stand on held square to the enemy so the gap between them always faces the enemy. As the enemy circles, members ride that circle, one giving ground while the other comes forward. |
| `coordinate` | `0`–`1`. `0.5` neutral, and the `Dueling*` default. `Group*`: `0.3` Easy, `0.5` Normal, `1` top | **The updown axis**, decided once per swing as it starts. `1` always throws the opposite direction to its neighbour — unblockable, and the reason a perfect pair cannot be beaten. `0` always throws the *same* direction, deliberately refusing the updown. `0.5` is a free pick. The middle is neutral rather than the bottom on purpose: two bots choosing independently already land an updown about half the time, so *refusing* to updown is its own agreement and needs its own end of the scale. Slide below `0.5` for a pair that is actively worse than chance. Above `0.5` also ramps up sharing their guard, so a stab either of them turns aside frees both to counter; at or below neutral they read only their own fight. Needs `squad`. |
| `slotError` | `0.9` Easy, `0.5` Normal, `0` top tier | The furthest a bot may stand from its place on the ring, in metres. Magnitude and direction are rolled when it forms up and **held for the bout**: steady enough inside a fight that you can read a badly-held line and work the gap, fresh next attempt so no one shape can be memorised. Because it rolls from zero, some bouts they simply line up properly and the gap is not there. Needs `squad`. |
| `formationLag` | `1.2` Easy, `0.6` Normal, `0` top tier | The longest a bot may work from a stale slot, in seconds. It re-checks where it should be on a randomised interval up to this, so it is late reacting when you press in, back off, or run around the formation — sometimes badly late, sometimes barely at all. At `0` it tracks perfectly. Needs `squad`. |
| `post` | `true` for `Dueling*` / `Group*` / `Test`, `false` otherwise | Make it a drill station: remember where it was set up, wait there until provoked, and walk back afterwards, whether it won or was killed and replaced. This is why a duel bot stays on the mark you put it on instead of drifting to wherever its last fight happened to end. Independent of `squad`, so it works on a single bot of any melee preset — on a `RiposteDummy` pair it with `move true`, or there is nothing for it to walk back with. On a group it also means all of them wake together. See **[Group drill stations](#group-drill-stations)**. |
| `breakoff` | `false` (`true` for `Group*`) | Once provoked, retreat to `breakoffRange` and re-form before throwing anything, so the bout starts from a clean approach rather than from wherever the provoking blow landed. The out-of-range fight for stab priority is most of the skill, and this is what makes the drill rehearse it. Needs `post`, since it is the step between waiting and fighting; with `post` on and this off, a provoked bot piles straight in. |
| `breakoffRange` | `6` | How far it retreats when breaking off. The dial on how much approach room the drill gives you: raise it for a longer run-in, lower it to get to blows sooner. Only used while `breakoff` is on. |
| `resetRange` | `0` | How far the target may get from the post before the group gives up on the bout. **`0`, the default, removes the limit** (same convention as `targetRange` and `separationRange`): a bout then ends when it is won or lost, not when someone steps away from it, which is usually what you want — backing off to reset your spacing should not call the fight off. Set it to a distance to fence the drill into an area instead, in which case wandering past that range also ends the bout. Only used while `post` is on. |
| `minMembers` | `0` (`2` for `Group*`) | The fewest members the batch will fight with. Drop below it and the bout is over: the group withdraws, returns to the post, and will not be provoked again until it is back up to strength. `0` means it fights on however few are left. Set it to the smallest count the drill is still *about* — on a trio, `2` keeps a 3v1 running as a 2v1 after you kill one and only calls it when the next death would make it a 1v1; on a pair, `2` ends the bout the moment one dies. **Capped by the batch's own size**, which is what makes one value work at every size: a bot summoned on its own is a batch of one whose drill is a 1v1, so `2` simply does not apply to it. Needs the `Replace` death policy to recover: with `Kick` or `None` nobody comes back and the station stays shut. Only used while `post` is on.<br><br>It also decides **how long a formation lasts.** Above `0` the group is a standing unit: it holds together through the bout and its `returnDelay`, which is what lets bots you gathered by walking them onto one player keep working as a group. At `0` the formation is an ad-hoc line that breaks up the moment the bout ends, so each bot has to be provoked again — which is what keeps duellists from inheriting a group they merely stood next to once. |
| `holdReplacement` | `false` (`true` for `Group*`) | A dead member's replacement waits for the bout to finish instead of walking back into it. This is what makes `minMembers` mean anything: without it a 3v1 is a 2v1 for a few seconds and then a 3v1 again, so the shorthanded fight never really happens. Replacements appear once the group is between bouts. Capped at two minutes, so a drill nobody ever finishes cannot swallow a bot for good. Set it `false` for an endurance grind where bots keep feeding in mid-fight. Only used while `post` is on. |
| `returnDelay` | `30` | Seconds the group holds where the bout ended before walking back to the post. It keeps formation and stays provokable the whole time, so running the same drill repeatedly does not mean following the bots back to a post between attempts, while a station left alone still tidies itself up. Only used while `post` is on. |
| `passiveBlockReaction` | `0` | The block reaction beat used **while waiting to be provoked**, instead of `blockReactionMin`/`Max`. Defaults to instant on purpose: a station's job in that state is to be attacked, not assassinated, and at `DuelingEasy` or `GroupEasy` speeds an ordinary walk-up stab kills the bot before its guard comes up, ending a 2v1 before it starts. Difficulty belongs in the fight, so the tiers only differ once provoked. Raise it for a station that punishes a sloppy approach. Not randomised, unlike the fighting beat. Only used while `engageOnAttack` is on. |
| `squadSpacing` | `0.9` | Gap between neighbouring bots, and the diameter of a pair's circle. Measured as the widest gap a player cannot jump between. A smaller circle also needs less travel per degree of rotation, so lower it if the pair struggles to keep up with someone running around them. |
| `laneHalfWidth` | `0.5` | How close a squadmate may be to the swing line before the shot counts as blocked, roughly a body width. Raise it if bots still clip each other, but keep it under `squadSpacing` or a partner standing alongside will block every stab. |
| `squadStandoff` | `1.5` | Range the formation's point holds from the enemy. It only repositions when the enemy leaves that range, so someone circling at a steady distance leaves the point alone and the pair simply rotates, while someone closing in or backing off tows the formation along. Treat `1.5` as the ceiling: members sit half a spacing off the point, so anything higher puts them at the edge of `attackRange` and gives a player room to walk around one and line the pair up. |
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

  * **args:** `None | Manual | StabbingDummy | RiposteDummy | DuelingEasy | DuelingNormal | Dueling | GroupEasy | GroupNormal | Group | Guardian | Test` (see [Bot AI Types](#bot-ai-types))
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

* **SetBotDefaultAi**: `None | Manual | StabbingDummy | RiposteDummy | DuelingEasy | DuelingNormal | Dueling | GroupEasy | GroupNormal | Group | Guardian | Test`
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
