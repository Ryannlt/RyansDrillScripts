using System.Collections.Generic;
using MDS.ConfigVariables;

// The lever half of MeleeAi: the tunable state, the per-preset defaults, and the IConfigurableAi plumbing that
// reads and writes them. Split out because MeleeAi.cs is the interesting file - the per-tick decision - and this
// is bookkeeping that grew to a third of its length. Presets are lever bundles, so nearly every behaviour added
// to the AI lands here as well, and keeping the two apart stops that traffic from burying Decide().
//
// A partial class rather than a separate MeleeLevers type on purpose: the behaviour reads these fields on almost
// every line, so extracting a type would mean rewriting all of that for an encapsulation boundary nothing else
// would use. Nothing outside MeleeAi consumes the levers, and MeleeDummy has its own.

namespace MDS.Systems
{
    public partial class MeleeAi
    {
        // Tuning levers (IConfigurableAi). Defaults per preset in DefaultLeversFor.
        private float _offensiveBase, _offensiveVar;   // close spacing to press and attack from (base plus jitter)
        private float _defensiveBase, _defensiveVar;   // further spacing to guard and read from (base plus jitter)
        private float _attackRange;                    // within this distance a stab can land, so we throw
        private float _attackReadBeat;                 // extra randomized beat on the attack cooldown
        private float _riposteReactionMin, _riposteReactionMax; // reaction beat between a block landing and the counter
        private float _riposteWindow;                  // how long the post-block counter stays available
        private float _blockReactionMin, _blockReactionMax;     // reaction beat between reading an attack and raising the guard (0 = instant)

        // The same beat, but in every posture except fighting: waiting to be provoked, backing off to re-form, and
        // withdrawing. In all three the bot has stopped swinging, and a bot that cannot defend itself while doing
        // so is just free kills - a walk-up stab would end a 2v1 before it starts, and chasing a retreating bot
        // would always pay. Defaults to instant so difficulty lives in the fight; raise it for a station that
        // punishes a sloppy approach. Not randomised: jitter here only decides whether the drill happens at all.
        private float _passiveBlockReaction;

        // Capability toggles (levers).
        private bool _press;      // throw the first blow when the enemy isn't threatening
        private bool _riposte;    // counter after our guard absorbs an attack
        private bool _move;       // drive melee spacing (false = stand our ground)
        private bool _pursue;     // advance toward a target that's too far (false = hold ground, let it walk away)
        private bool _engageOnAttack; // start passive (block only), only fight a player who attacks us, and only until they die

        // Targeting levers (see ResolveTarget).
        private float _targetRange;   // only acquire candidates within this range (<= 0 = unlimited)
        private bool _ignoreTeam;     // target any player regardless of faction (else enemies only)
        private bool _ignoreBots;     // target only human players (skip bots)
        private bool _stickyTarget;   // keep the current target while valid (else re-pick the closest each tick)
        private float _passiveRange;  // engageOnAttack: hold distance while waiting (engaged uses defensiveRange)

        // Guard levers (see the Guardian preset). A guarded player turns the bot into an escort: it holds station
        // near them and only fights once something threatens them.
        // The summon commands hand every bot a guard target, so this toggle is what decides whether the bot acts
        // on it. Without it every summoned bot would escort whoever summoned it and refuse to fight them.
        private bool _guard;              // act as an escort for the guard target, rather than ignoring it
        private float _guardRange;        // an enemy this close to the guarded player pulls the bot into the fight
        private float _guardFollowRange;  // distance the bot holds from the guarded player while nothing is happening
        private float _separationRange;   // push apart from other bots within this, 0 to disable

        // Squad levers. When enabled and another bot from the same spawn batch is present, SquadCoordinator hands
        // this bot a slot on the arc around the enemy and says whether its swing line is clear of squadmates.
        private bool _squad;

        // Imperfection levers: each is the WORST the bot may be, not how bad it is. Every roll runs from zero up
        // to the lever, so a bot can always come out correct by chance and a lower tier simply does so less
        // often. A pair reliably half a metre too wide is a puzzle solved once and exploited forever; a pair
        // usually too wide has to be read every time, and now and then punishes an assumption.
        private float _slotError;         // furthest it may stand from its place on the ring, metres
        private float _formationLag;      // longest it may work from a stale slot, seconds

        // How often the formation fights as a unit rather than merely standing in one, 0 to 1. Rolled per swing
        // and per counter: it decides whether the bot takes the direction the line assigned it and reads the
        // shared guard, or fights its own fight. At 0 it never does, which is a duellist who has simply stopped
        // crowding its neighbour; at 1 always, which is a drilled pair working someone over together.
        //
        // A chance rather than a switch because the middle is where the practice is. Half-coordinated bots
        // mostly stab the same way and can be blown one at a time, then occasionally land a real opposite pair -
        // which is what stops a player writing the tier off and swinging on autopilot.
        private float _coordinate;

        private float _squadSpacing;      // gap between neighbouring members, the diameter of the pair's circle
        private float _laneHalfWidth;     // how close a squadmate may be to the swing line before it is blocked
        private float _squadStandoff;     // range the formation's point holds from the enemy

        // Station levers. Independent of squad: one bot can hold a post and return to it without ever forming up
        // with anybody, which is what makes these useful on a plain duellist.
        private bool _post;               // wait at the post until provoked, and return to it afterwards
        private bool _breakoff;           // once provoked, re-establish range before throwing anything
        private float _breakoffRange;     // range re-established when breaking off
        private float _resetRange;        // how far the target may get from the post before disengaging (0 = no limit)
        private int _minMembers;          // fewest members it will fight with; below this it breaks off and stays shut
        private bool _holdReplacement;    // a dead member's replacement waits for the bout to end before spawning
        private float _returnDelay;       // seconds it lingers where the bout ended before walking back to the post

        private int? _guardTargetId;      // the friendly being escorted, from the summon or the guardTarget lever

        // Built-in lever defaults per preset: shared spacing values, then the toggles, targeting, and reaction
        // beats that make each preset. GlobalAiConfigurable seeds the global defaults from this so 'rc get globalAI'
        // reports them.
        public static (string name, string value)[] DefaultLeversFor(BotAiEnum aiType)
        {
            var levers = new List<(string, string)>
            {
                ("offensiveRange", "0.7"), ("offensiveRangeVariance", "0.1"),
                ("defensiveRange", "2.0"), ("defensiveRangeVariance", "0.4"),
                ("attackRange", "2.0"), ("riposteWindow", "0.6"),
                ("ignoreTeam", "true"),  // target anyone by default; no need to be on the opposing faction to use a bot
                ("ignoreBots", "true"),  // only respond to human players by default
                ("passiveRange", "0.6"), // engageOnAttack waiting-mode hold distance
                ("guard", "false"),      // summons hand every bot a guard target; only escorts act on it
                ("guardTarget", "0"),    // no one to escort unless a summon or the lever names someone
                ("guardRange", "10"), ("guardFollowRange", "3"),
                ("separationRange", "0"), // off unless a preset wants bots to keep clear of each other
                ("squad", "false"),       // fight as a formation with the rest of the spawn batch
                ("coordinate", "0.5"),    // neutral: each bot picks its own swing, so updowns happen only by luck
                ("slotError", "0"), ("formationLag", "0"), // perfect placement and perfect tracking by default
                // 0.9 is the measured gap a player cannot jump between. laneHalfWidth is about a body width, so
                // at this spacing a partner standing beside the bot does not count as blocking its line, which is
                // the point: once the pair is set, both are meant to be able to stab.
                ("squadSpacing", "0.9"), ("laneHalfWidth", "0.5"),
                // The formation's point holds this range from the enemy, so a circling enemy leaves it alone while
                // one that closes or withdraws tows it along.
                ("squadStandoff", "1.5"),
                ("passiveBlockReaction", "0"), // waiting bots block instantly, so a walk-up stab can't end the drill early
                ("post", "false"),          // only drill stations wait to be provoked and return afterwards
                ("breakoff", "false"),      // and only some of those reset the distance before fighting
                // resetRange 0 = no distance limit: a bout ends when it is won or lost, not when someone steps
                // away from it. Tidying the arena afterwards is returnDelay's job, not this one's.
                ("breakoffRange", "6"), ("resetRange", "0"),
                ("minMembers", "0"),          // 0 = fight on however few are left
                ("holdReplacement", "false"), // only a drill with a group size worth preserving holds one back
                ("returnDelay", "30"),        // hold where the bout ended long enough to be used again straight away
            };
            switch (aiType)
            {
                // Guardian: escorts the player it was summoned onto. It holds station beside them and stays out of
                // it until an enemy comes within guardRange of them or they get into melee themselves, then it
                // fights like a duellist with human reactions. It respects factions, since the whole point is to
                // fight the other side, and keeps its distance from the other bots so a pile of guards is less
                // likely to cut each other down.
                case BotAiEnum.Guardian:
                    levers.Add(("press", "true"));  levers.Add(("riposte", "true"));  levers.Add(("move", "true")); levers.Add(("pursue", "true"));
                    levers.Add(("targetRange", "0")); levers.Add(("stickyTarget", "false")); levers.Add(("engageOnAttack", "false"));
                    levers.Add(("ignoreTeam", "false"));
                    levers.Add(("guard", "true"));
                    levers.Add(("separationRange", "1.5"));
                    // Deliberately poor, a step below DuelingEasy. A guard is meant to buy its ward a moment and
                    // be beatable by any competent player, not to win the duel, and a whole detail of them would
                    // be miserable to fight otherwise. It reads attacks slowly enough that plenty get through.
                    levers.Add(("blockReactionMin", "0.5")); levers.Add(("blockReactionMax", "0.8"));
                    levers.Add(("riposteReactionMin", "0.4")); levers.Add(("riposteReactionMax", "1.1"));
                    levers.Add(("attackReadBeat", "1.2")); break;

                // Dueling family: passive (block only) until a player in range strikes it and it blocks the hit,
                // then it locks that attacker and fights to the death before returning to passive. targetRange is
                // the passive read/provoke range. Within a family the tiers share everything but the reaction
                // beats, and the Group tiers are the Dueling tiers plus the formation and station levers.
                // Test shares the Dueling lever set so whatever is being developed is measured against a known
                // baseline, and carries the behaviour under construction on top. None of this is added to
                // Guardian: it is in use on the linebattle server and is deliberately left alone.
                case BotAiEnum.DuelingEasy:
                case BotAiEnum.DuelingNormal:
                case BotAiEnum.Dueling:
                case BotAiEnum.GroupEasy:
                case BotAiEnum.GroupNormal:
                case BotAiEnum.Group:
                case BotAiEnum.Test:
                    levers.Add(("press", "true"));  levers.Add(("riposte", "true"));  levers.Add(("move", "true")); levers.Add(("pursue", "true"));
                    levers.Add(("targetRange", "3")); levers.Add(("stickyTarget", "false")); levers.Add(("engageOnAttack", "true"));

                    // Every preset in this family holds formation. Two duellists who converge on one player stop
                    // crowding each other and stop swinging through each other, which is all squad buys on its
                    // own - coordinate below is what turns a formation into a team. A bot fighting alone is
                    // untouched either way, since a formation of one has no slot to hold.
                    //
                    // Separation goes with it, kept just under squadSpacing so it only resists bots overlapping
                    // and does not fight the formation itself. Without it a bot repositioning behind its partner
                    // walks into it, which is the same problem the formation exists to solve.
                    levers.Add(("squad", "true")); levers.Add(("separationRange", "0.8"));

                    // They all keep a post too: they wait on the mark they were put on, and after a bout they
                    // hold where it ended long enough to be used again straight away (returnDelay) before walking
                    // back. Without it a bot drifts to wherever its last fight finished and stays there, which
                    // makes running the same drill repeatedly a matter of chasing it around.
                    levers.Add(("post", "true"));

                    // Duel bots keep the neutral 0.5 from the shared defaults: each picks its own swing, so a
                    // pair lands an updown only by luck. Throwing deliberately opposite is a Group trait, set per
                    // tier just below - it must NOT come from the tier switch, which the duel family shares, or a
                    // top-tier duel pair would quietly start drilling updowns at people.
                    if (aiType == BotAiEnum.Test) levers.Add(("coordinate", "1"));

                    // Group family: a drill station. Bots summoned in one batch wait where they were set up,
                    // and provoking any one of them wakes all of them onto whoever did it. They then back off to
                    // breakoffRange and re-form before throwing anything, because the out-of-range fight for stab
                    // priority is most of the skill and starting from the activation stab would skip it. When the
                    // player dies or walks off they return to the post and re-arm for the next one.
                    if (aiType == BotAiEnum.GroupEasy || aiType == BotAiEnum.GroupNormal || aiType == BotAiEnum.Group)
                    {
                        levers.Add(("breakoff", "true"));
                        // Stop one short of a duel: a 3v1 plays on as a 2v1 and only resets when the next death
                        // would make it a 1v1, which is not what these were summoned for. Safe at any size because
                        // the coordinator caps it by the batch's own strength, so a bot summoned alone still duels.
                        levers.Add(("minMembers", "2"));

                        // A shorthanded bout stays shorthanded until it is over, which is what makes minMembers
                        // mean anything. Only the Group family wants this: a duel bot has no group size worth
                        // preserving, so its replacement should simply come back.
                        levers.Add(("holdReplacement", "true"));

                        // The updown axis, which is the Group family's real difficulty knob. The top tier always
                        // throws opposite, which is unblockable and the reason a perfect pair cannot be beaten.
                        // Normal sits at neutral, so a pair lands one only by luck, about half the time - which
                        // is what a genuinely uncoordinated pair does. Easy goes below neutral and starts
                        // deliberately matching, so updowns become rarer than chance and its stabs can be drawn
                        // and blown one at a time.
                        levers.Add(("coordinate",
                            aiType == BotAiEnum.GroupEasy ? "0.3" :
                            aiType == BotAiEnum.GroupNormal ? "0.5" : "1"));
                    }

                    switch (aiType)
                    {
                        // Difficulty is two things: how fast a bot reacts, and how well it holds a formation. The
                        // second matters more in a group - reactions alone would give a beatable pair that still
                        // stands in a perfect line and stabs in perfect opposition, which has no answer. Note the
                        // imperfection levers are ceilings, so an Easy bot still lines up correctly now and
                        // again; the tier decides how often, not whether.
                        case BotAiEnum.DuelingEasy:   // sluggish, and holds a line badly
                        case BotAiEnum.GroupEasy:
                            levers.Add(("blockReactionMin", "0.3")); levers.Add(("blockReactionMax", "0.5"));
                            levers.Add(("riposteReactionMin", "0.2")); levers.Add(("riposteReactionMax", "0.8"));
                            levers.Add(("attackReadBeat", "0.9"));
                            // Barely holds a line: wide enough to walk between, and late enough answering that
                            // the gap stays open long enough to use.
                            levers.Add(("slotError", "0.9")); levers.Add(("formationLag", "1.2")); break;

                        case BotAiEnum.DuelingNormal: // human reactions, and human sloppiness
                        case BotAiEnum.GroupNormal:
                            levers.Add(("blockReactionMin", "0.1")); levers.Add(("blockReactionMax", "0.2"));
                            levers.Add(("riposteReactionMin", "0")); levers.Add(("riposteReactionMax", "0.5"));
                            levers.Add(("attackReadBeat", "0.6"));
                            // What Easy used to be, which turned out to be the honest middle: loose enough to be
                            // split, but not so loose that the gap is simply there for the taking.
                            levers.Add(("slotError", "0.5")); levers.Add(("formationLag", "0.6")); break;

                        default:                      // Dueling, Group and Test: how it is supposed to be done
                            levers.Add(("blockReactionMin", "0")); levers.Add(("blockReactionMax", "0"));
                            levers.Add(("riposteReactionMin", "0")); levers.Add(("riposteReactionMax", "0"));
                            levers.Add(("attackReadBeat", "0.3"));
                            levers.Add(("slotError", "0")); levers.Add(("formationLag", "0")); break;
                    }
                    break;
                default: // RiposteDummy: stands its ground, blocks, counters only, engages the closest in range.
                    levers.Add(("press", "false")); levers.Add(("riposte", "true"));  levers.Add(("move", "false")); levers.Add(("pursue", "false"));
                    levers.Add(("targetRange", "3")); levers.Add(("stickyTarget", "false")); levers.Add(("engageOnAttack", "false"));
                    levers.Add(("blockReactionMin", "0.1")); levers.Add(("blockReactionMax", "0.2"));
                    levers.Add(("riposteReactionMin", "0")); levers.Add(("riposteReactionMax", "0.5"));
                    levers.Add(("attackReadBeat", "0.6")); break;
            }
            return levers.ToArray();
        }

        // Applies the preset's defaults, each overridden by a global default if one is set. Called from the
        // constructor; TrySet does the typed parse, and the advisory it can return is discarded because gate
        // state is meaningless while the levers are still being seeded.
        private void SeedLevers(BotAiEnum aiType)
        {
            foreach (var (name, def) in DefaultLeversFor(aiType))
                if (!TrySet(name, GlobalAiConfigurable.Default(aiType.ToString(), name, def), out _))
                    TrySet(name, def, out _);
        }

        private static readonly string[] LeverNames =
        {
            "offensiveRange", "offensiveRangeVariance", "defensiveRange", "defensiveRangeVariance",
            "attackRange", "attackReadBeat", "riposteReactionMin", "riposteReactionMax", "riposteWindow",
            "blockReactionMin", "blockReactionMax", "press", "riposte", "move", "pursue",
            "targetRange", "ignoreTeam", "ignoreBots", "stickyTarget", "engageOnAttack",
            "passiveRange", "passiveBlockReaction",
            "guard", "guardTarget", "guardRange", "guardFollowRange", "separationRange",
            "squad", "coordinate", "slotError", "formationLag",
            "squadSpacing", "laneHalfWidth", "squadStandoff",
            "post", "breakoff", "breakoffRange", "resetRange",
            "minMembers", "holdReplacement", "returnDelay"
        };

        // Levers that do nothing unless another lever is on. Setting one whose gate is off is NOT an error - the
        // order you type commands in should not matter - but it silently has no effect, which is exactly how
        // 'post' on a RiposteDummy went unnoticed. One parent each; chains resolve by walking up.
        private static readonly Dictionary<string, string> LeverGates = new()
        {
            { "offensiverange", "move" }, { "offensiverangevariance", "move" },
            { "defensiverange", "move" }, { "defensiverangevariance", "move" },
            { "pursue", "move" },
            { "attackrange", "press" },
            { "ripostereactionmin", "riposte" }, { "ripostereactionmax", "riposte" }, { "ripostewindow", "riposte" },
            // passiveBlockReaction is deliberately ungated: it applies in every posture except fighting, which a
            // bot can reach through engageOnAttack (waiting) or through post (backing off, withdrawing).
            { "passiverange", "engageonattack" },
            { "guardtarget", "guard" }, { "guardrange", "guard" }, { "guardfollowrange", "guard" },
            { "coordinate", "squad" }, { "sloterror", "squad" }, { "formationlag", "squad" },
            { "squadspacing", "squad" }, { "lanehalfwidth", "squad" }, { "squadstandoff", "squad" },
            { "breakoff", "post" }, { "breakoffrange", "breakoff" }, { "resetrange", "post" },
            { "minmembers", "post" }, { "holdreplacement", "post" }, { "returndelay", "post" },
        };

        // Only the levers that appear as gates above need answering here.
        private bool GateOpen(string gate)
        {
            switch (gate)
            {
                case "move":           return _move;
                case "press":          return _press;
                case "riposte":        return _riposte;
                case "guard":          return _guard;
                case "squad":          return _squad;
                case "post":           return _post;
                case "breakoff":       return _breakoff;
                case "engageonattack": return _engageOnAttack;
                default:               return true;
            }
        }

        // The switched-off lever that is stopping this one from doing anything, or null when it is live.
        // Reports the NEAREST closed gate, walking past any that are already on, because the direct dependency is
        // the actionable next step: with post and breakoff both off, breakoffRange says 'breakoff', and once
        // breakoff is on it says 'post'. Naming the root straight away would be advice you cannot act on yet.
        private string BlockingGate(string lever)
        {
            string gate = LeverGates.TryGetValue(lever, out string g) ? g : null;

            while (gate != null)
            {
                if (!GateOpen(gate)) return gate;
                gate = LeverGates.TryGetValue(gate, out string next) ? next : null;
            }

            return null;
        }

        public bool TrySet(string name, string value, out string message)
        {
            if (!TrySetValue(name, value, out message)) return false;

            // Set it either way, but say so when it will sit dormant. The caller already names the lever, so this
            // only carries the reason.
            string blocked = BlockingGate(name.ToLowerInvariant());
            if (blocked != null)
                message = $"it does nothing while '{blocked}' is false.";

            return true;
        }

        private bool TrySetValue(string name, string value, out string error)
        {
            error = string.Empty;
            switch (name.ToLowerInvariant())
            {
                case "offensiverange":         return SetFloat(value, 0f, v => _offensiveBase = v, "offensiveRange", ref error);
                case "offensiverangevariance": return SetFloat(value, 0f, v => _offensiveVar = v, "offensiveRangeVariance", ref error);
                case "defensiverange":         return SetFloat(value, 0f, v => _defensiveBase = v, "defensiveRange", ref error);
                case "defensiverangevariance": return SetFloat(value, 0f, v => _defensiveVar = v, "defensiveRangeVariance", ref error);
                case "attackrange":            return SetFloat(value, 0f, v => _attackRange = v, "attackRange", ref error);
                case "attackreadbeat":         return SetFloat(value, 0f, v => _attackReadBeat = v, "attackReadBeat", ref error);
                case "ripostereactionmin":     return SetFloat(value, 0f, v => _riposteReactionMin = v, "riposteReactionMin", ref error);
                case "ripostereactionmax":     return SetFloat(value, 0f, v => _riposteReactionMax = v, "riposteReactionMax", ref error);
                case "ripostewindow":          return SetFloat(value, 0f, v => _riposteWindow = v, "riposteWindow", ref error);
                case "blockreactionmin":       return SetFloat(value, 0f, v => _blockReactionMin = v, "blockReactionMin", ref error);
                case "blockreactionmax":       return SetFloat(value, 0f, v => _blockReactionMax = v, "blockReactionMax", ref error);
                case "press":   return SetBool(value, v => _press = v, "press", ref error);
                case "riposte": return SetBool(value, v => _riposte = v, "riposte", ref error);
                case "move":    return SetBool(value, v => _move = v, "move", ref error);
                case "pursue":  return SetBool(value, v => _pursue = v, "pursue", ref error);
                case "targetrange":  return SetFloat(value, 0f, v => _targetRange = v, "targetRange", ref error); // 0 = unlimited
                case "ignoreteam":   return SetBool(value, v => _ignoreTeam = v, "ignoreTeam", ref error);
                case "ignorebots":   return SetBool(value, v => _ignoreBots = v, "ignoreBots", ref error);
                case "stickytarget": return SetBool(value, v => _stickyTarget = v, "stickyTarget", ref error);
                case "engageonattack": return SetBool(value, v => _engageOnAttack = v, "engageOnAttack", ref error);
                case "passiverange": return SetFloat(value, 0f, v => _passiveRange = v, "passiveRange", ref error);
                case "passiveblockreaction": return SetFloat(value, 0f, v => _passiveBlockReaction = v, "passiveBlockReaction", ref error);
                case "guard":            return SetBool(value, v => _guard = v, "guard", ref error);
                case "guardrange":       return SetFloat(value, 0f, v => _guardRange = v, "guardRange", ref error);
                case "guardfollowrange": return SetFloat(value, 0f, v => _guardFollowRange = v, "guardFollowRange", ref error);
                case "separationrange":  return SetFloat(value, 0f, v => _separationRange = v, "separationRange", ref error); // 0 = off
                case "squad":            return SetBool(value, v => _squad = v, "squad", ref error);
                case "coordinate":       return SetFraction(value, v => _coordinate = v, "coordinate", ref error);
                case "sloterror":        return SetFloat(value, 0f, v => _slotError = v, "slotError", ref error);
                case "formationlag":     return SetFloat(value, 0f, v => _formationLag = v, "formationLag", ref error);
                case "squadspacing":     return SetFloat(value, 0f, v => _squadSpacing = v, "squadSpacing", ref error);
                case "lanehalfwidth":    return SetFloat(value, 0f, v => _laneHalfWidth = v, "laneHalfWidth", ref error);
                case "squadstandoff":    return SetFloat(value, 0f, v => _squadStandoff = v, "squadStandoff", ref error);
                case "post":             return SetBool(value, v => _post = v, "post", ref error);
                case "breakoff":         return SetBool(value, v => _breakoff = v, "breakoff", ref error);
                case "breakoffrange":    return SetFloat(value, 0f, v => _breakoffRange = v, "breakoffRange", ref error);
                case "resetrange":       return SetFloat(value, 0f, v => _resetRange = v, "resetRange", ref error); // 0 = no limit
                case "minmembers":       return SetInt(value, 0, v => _minMembers = v, "minMembers", ref error);
                case "holdreplacement":  return SetBool(value, v => _holdReplacement = v, "holdReplacement", ref error);
                case "returndelay":      return SetFloat(value, 0f, v => _returnDelay = v, "returnDelay", ref error);

                case "guardtarget":
                    // A player id to escort. 0 or less clears it, which is also how the presets say "nobody".
                    if (!int.TryParse(value, out int wardId))
                    {
                        error = "guardTarget must be a playerId, or 0 for none.";
                        return false;
                    }
                    _guardTargetId = wardId > 0 ? wardId : (int?)null;
                    return true;

                default:
                    error = $"Unknown lever '{name}'. MeleeAi levers: {string.Join(", ", LeverNames)}.";
                    return false;
            }
        }

        public IEnumerable<(string name, string value, string inactive)> ListParams()
        {
            foreach (var (name, value) in RawParams())
                yield return (name, value, BlockingGate(name.ToLowerInvariant()));
        }

        private IEnumerable<(string name, string value)> RawParams()
        {
            yield return ("offensiveRange", _offensiveBase.ToString("0.##"));
            yield return ("offensiveRangeVariance", _offensiveVar.ToString("0.##"));
            yield return ("defensiveRange", _defensiveBase.ToString("0.##"));
            yield return ("defensiveRangeVariance", _defensiveVar.ToString("0.##"));
            yield return ("attackRange", _attackRange.ToString("0.##"));
            yield return ("attackReadBeat", _attackReadBeat.ToString("0.##"));
            yield return ("riposteReactionMin", _riposteReactionMin.ToString("0.##"));
            yield return ("riposteReactionMax", _riposteReactionMax.ToString("0.##"));
            yield return ("riposteWindow", _riposteWindow.ToString("0.##"));
            yield return ("blockReactionMin", _blockReactionMin.ToString("0.##"));
            yield return ("blockReactionMax", _blockReactionMax.ToString("0.##"));
            yield return ("press", _press ? "true" : "false");
            yield return ("riposte", _riposte ? "true" : "false");
            yield return ("move", _move ? "true" : "false");
            yield return ("pursue", _pursue ? "true" : "false");
            yield return ("targetRange", _targetRange.ToString("0.##"));
            yield return ("ignoreTeam", _ignoreTeam ? "true" : "false");
            yield return ("ignoreBots", _ignoreBots ? "true" : "false");
            yield return ("stickyTarget", _stickyTarget ? "true" : "false");
            yield return ("engageOnAttack", _engageOnAttack ? "true" : "false");
            yield return ("passiveRange", _passiveRange.ToString("0.##"));
            yield return ("passiveBlockReaction", _passiveBlockReaction.ToString("0.##"));
            yield return ("guard", _guard ? "true" : "false");
            yield return ("guardTarget", (_guardTargetId ?? 0).ToString());
            yield return ("guardRange", _guardRange.ToString("0.##"));
            yield return ("guardFollowRange", _guardFollowRange.ToString("0.##"));
            yield return ("separationRange", _separationRange.ToString("0.##"));
            yield return ("squad", _squad ? "true" : "false");
            yield return ("coordinate", _coordinate.ToString("0.##"));
            yield return ("slotError", _slotError.ToString("0.##"));
            yield return ("formationLag", _formationLag.ToString("0.##"));
            yield return ("squadSpacing", _squadSpacing.ToString("0.##"));
            yield return ("laneHalfWidth", _laneHalfWidth.ToString("0.##"));
            yield return ("squadStandoff", _squadStandoff.ToString("0.##"));
            yield return ("post", _post ? "true" : "false");
            yield return ("breakoff", _breakoff ? "true" : "false");
            yield return ("breakoffRange", _breakoffRange.ToString("0.##"));
            yield return ("resetRange", _resetRange.ToString("0.##"));
            yield return ("minMembers", _minMembers.ToString());
            yield return ("holdReplacement", _holdReplacement ? "true" : "false");
            yield return ("returnDelay", _returnDelay.ToString("0.##"));
        }

        // Copies every lever to a Replace replacement, so a bot tuned with 'rc bot cfg' isn't reset to preset
        // defaults on death. Levers only: runtime state stays with the bot that earned it, which is why a
        // replacement starts passive rather than resuming a fight it never had (see InheritFrom).
        private void CopyLeversFrom(MeleeAi p)
        {
            _offensiveBase = p._offensiveBase; _offensiveVar = p._offensiveVar;
            _defensiveBase = p._defensiveBase; _defensiveVar = p._defensiveVar;
            _attackRange = p._attackRange; _attackReadBeat = p._attackReadBeat;
            _riposteReactionMin = p._riposteReactionMin; _riposteReactionMax = p._riposteReactionMax;
            _riposteWindow = p._riposteWindow;
            _blockReactionMin = p._blockReactionMin; _blockReactionMax = p._blockReactionMax;
            _passiveBlockReaction = p._passiveBlockReaction;
            _press = p._press; _riposte = p._riposte; _move = p._move; _pursue = p._pursue;
            _targetRange = p._targetRange; _ignoreTeam = p._ignoreTeam; _ignoreBots = p._ignoreBots; _stickyTarget = p._stickyTarget;
            _passiveRange = p._passiveRange;
            _guard = p._guard; _guardRange = p._guardRange; _guardFollowRange = p._guardFollowRange; _separationRange = p._separationRange;
            _squad = p._squad; _coordinate = p._coordinate;
            _slotError = p._slotError; _formationLag = p._formationLag;
            _squadSpacing = p._squadSpacing; _laneHalfWidth = p._laneHalfWidth;
            _squadStandoff = p._squadStandoff;
            _post = p._post; _breakoff = p._breakoff; _breakoffRange = p._breakoffRange; _resetRange = p._resetRange;
            _minMembers = p._minMembers; _holdReplacement = p._holdReplacement; _returnDelay = p._returnDelay;
            _engageOnAttack = p._engageOnAttack;
            _guardTargetId = p._guardTargetId;   // a replacement guard keeps escorting the same player
        }

        private static bool SetFloat(string value, float min, System.Action<float> set, string name, ref string error)
        {
            if (!float.TryParse(value, out float v) || v < min)
            {
                error = $"{name} must be a number{(min > float.MinValue ? $" >= {min}" : "")}.";
                return false;
            }
            set(v);
            return true;
        }

        // A 0-to-1 chance. Rejected rather than clamped when out of range: silently turning 50 into 1 would read
        // as a working setting that does something quite different from what was asked for.
        private static bool SetFraction(string value, System.Action<float> set, string name, ref string error)
        {
            if (!float.TryParse(value, out float v) || v < 0f || v > 1f)
            {
                error = $"{name} must be a chance between 0 and 1.";
                return false;
            }
            set(v);
            return true;
        }

        private static bool SetInt(string value, int min, System.Action<int> set, string name, ref string error)
        {
            if (!int.TryParse(value, out int v) || v < min)
            {
                error = $"{name} must be a whole number >= {min}.";
                return false;
            }
            set(v);
            return true;
        }

        private static bool SetBool(string value, System.Action<bool> set, string name, ref string error)
        {
            switch (value.ToLowerInvariant())
            {
                case "true": case "on": case "1": case "yes": set(true); return true;
                case "false": case "off": case "0": case "no": set(false); return true;
                default: error = $"{name} must be true or false."; return false;
            }
        }
    }
}
