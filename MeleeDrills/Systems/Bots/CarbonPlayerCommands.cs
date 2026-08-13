using System.Globalization;
using UnityEngine;
using MDS.Core;

// The one place that knows the Holdfast 'carbonPlayers' console-command vocabulary and quirks. All bot control
// funnels through here so the undocumented quirks stay quarantined to one file. Commands are issued without the
// 'rc' prefix because CommandExecutor.ExecuteConsoleCommand runs the command that would follow 'rc' (matching
// existing usage, e.g. ShootingTrainingEvent's "set ...").

namespace MDS.Systems
{
    public static class CarbonPlayerCommands
    {
        private const string Prefix = "carbonPlayers";

        // Placeholder for an empty name/regtag slot, so a following positional arg (e.g. uniformId) can
        // still be passed. The value only needs to be some non-empty string; it just occupies the slot.
        private const string EmptyArgPlaceholder = "none";

        // Spawn <count> bots with random class/faction across spawn points.
        public static void Spawn(int count)
        {
            CommandExecutor.ExecuteCommand($"{Prefix} spawn {count}");
        }

        // Spawn one bot with an explicit spec. Faction/Class serialize via FactionCountry/PlayerClass:
        // named values send their enum name (e.g. French, ArmyLineInfantry); an extension faction the SDK
        // enum can't name yet sends its integer (e.g. 11 for ARBritish), which the command accepts.
        // Optional trailing args are positional, so only appended while contiguous.
        public static void SpawnSpecific(BotSpawnSpec spec)
        {
            string cmd = $"{Prefix} spawnSpecific {spec.Faction} {spec.Class}";

            // Positional optional args: name, regtag, uniformId. To reach a later arg the earlier slots
            // must be filled, so substitute a placeholder for an empty name/regtag when a following arg
            // is set - otherwise an inherited uniformId is silently dropped for a bot with no regtag.
            if (!string.IsNullOrEmpty(spec.Name) || !string.IsNullOrEmpty(spec.RegTag) || spec.UniformId.HasValue)
                cmd += $" {Arg(spec.Name)}";

            if (!string.IsNullOrEmpty(spec.RegTag) || spec.UniformId.HasValue)
                cmd += $" {Arg(spec.RegTag)}";

            if (spec.UniformId.HasValue)
                cmd += $" {spec.UniformId.Value}";

            CommandExecutor.ExecuteCommand(cmd);
        }

        // Enable direct input control. Until this is set, the bot ignores inputAxis/inputRotation.
        public static void EnableInputControl(int playerId)
        {
            CommandExecutor.ExecuteCommand($"{Prefix} forceInputAxis true {playerId}");
            CommandExecutor.ExecuteCommand($"{Prefix} forceInputRotation true {playerId}");
        }

        public static void DisableInputControl(int playerId)
        {
            CommandExecutor.ExecuteCommand($"{Prefix} forceInputAxis false {playerId}");
            CommandExecutor.ExecuteCommand($"{Prefix} forceInputRotation false {playerId}");
        }

        // sideways/forwards each in [-1, 1]. Issued every tick per bot, so its result is not logged
        // (logResult: false) - otherwise it floods the debug log. Failures still surface.
        public static void SetInputAxis(int playerId, float sideways, float forwards)
        {
            CommandExecutor.ExecuteCommand($"{Prefix} inputAxis {Fmt(sideways)} {Fmt(forwards)} {playerId}", logResult: false);
        }

        // heading in degrees from North. Per-tick like inputAxis, so likewise not result-logged.
        public static void SetInputRotation(int playerId, float degrees)
        {
            CommandExecutor.ExecuteCommand($"{Prefix} inputRotation {Fmt(degrees)} {playerId}", logResult: false);
        }

        // Vertical aim in degrees, 0 being level. This is a separate channel from inputRotation, which only
        // carries the heading, and forcing input rotation does not pin it, so a bot left alone drifts to
        // looking at the ground. Issued on spawn and re-asserted on a slow cadence, so it isn't result-logged.
        public static void SetPitch(int playerId, float degrees)
        {
            CommandExecutor.ExecuteCommand($"{Prefix} pitch {Fmt(degrees)} {playerId}", logResult: false);
        }

        public static void SetRunning(int playerId, bool enable)
        {
            CommandExecutor.ExecuteCommand($"{Prefix} setRunning {Bool(enable)} {playerId}");
        }

        // Performs a Player Action. Quirk: a held action (e.g. a melee strike direction like
        // MeleeStrikeHigh) stays held until released with ExecuteMeleeWeaponStrike. Some actions
        // also won't fire while the bot is mid-other-action. See:
        // https://wiki.holdfastgame.com/Server_Configuration_Enums#Player_Actions
        public static void PerformAction(int playerId, string action)
        {
            CommandExecutor.ExecuteCommand($"{Prefix} playerAction {action} {playerId}");
        }

        // Actions that take a second argument, e.g. StartGestureAnimation PlayerGestureDancingFunny.
        public static void PerformAction(int playerId, string action, string argument)
        {
            CommandExecutor.ExecuteCommand($"{Prefix} playerAction {action} {argument} {playerId}");
        }

        // Removes a bot from the world. There is no carbonPlayers despawn command, so we kick the bot by id.
        // 'serverAdmin kick' is a serverAdmin command, not carbonPlayers-prefixed. The resulting disconnect fires
        // OnPlayerDisconnected, then BotManager.OnBotDisconnected, which untracks it (untrack is idempotent, so
        // the direct untrack in BotManager is also fine).
        public static void Despawn(int playerId)
        {
            CommandExecutor.ExecuteCommand($"serverAdmin kick {playerId}");
        }

        // Teleports a player or bot to a world position. 'teleport' is a general console command, not
        // carbonPlayers-prefixed. Used for summon and return-to-death placement.
        public static void Teleport(int playerId, Vector3 position)
        {
            CommandExecutor.ExecuteCommand($"teleport {playerId} {Fmt(position.x)},{Fmt(position.y)},{Fmt(position.z)}");
        }

        // A positional name/regtag arg, or the placeholder when empty. Any ASCII space is swapped for an en space
        // (U+2002) so the value can't split spawnSpecific's own space-delimited positional args. This matters for
        // Replace: the game hands player names back with the en-space normalised to a regular space, so a
        // replacement's captured name like "Named Bot" would otherwise split ("Bot" into regtag, "none" into
        // uniformId) and fail to spawn.
        private static string Arg(string value) =>
            string.IsNullOrEmpty(value) ? EmptyArgPlaceholder : value.Replace(' ', NameSpaceChar);

        private const char NameSpaceChar = '\u2002'; // EN SPACE: renders as a space in-game, not an arg delimiter

        // Invariant culture so floats never serialize with a locale comma decimal separator.
        private static string Fmt(float value) => value.ToString(CultureInfo.InvariantCulture);
        private static string Bool(bool value) => value ? "true" : "false";
    }
}
