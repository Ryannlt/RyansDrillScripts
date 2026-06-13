using System.Globalization;
using UnityEngine;
using MDS.Core;

// The ONLY place that knows the Holdfast 'carbonPlayers' console-command vocabulary and quirks.
// All bot control funnels through here so the (undocumented) quirks stay quarantined to one file.
// Commands are issued WITHOUT the 'rc' prefix because CommandExecutor.ExecuteConsoleCommand runs
// the command that would follow 'rc' (matches existing usage, e.g. ShootingTrainingEvent's "set ...").

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

        // sideways/forwards each in [-1, 1].
        public static void SetInputAxis(int playerId, float sideways, float forwards)
        {
            CommandExecutor.ExecuteCommand($"{Prefix} inputAxis {Fmt(sideways)} {Fmt(forwards)} {playerId}");
        }

        // heading in degrees from North.
        public static void SetInputRotation(int playerId, float degrees)
        {
            CommandExecutor.ExecuteCommand($"{Prefix} inputRotation {Fmt(degrees)} {playerId}");
        }

        public static void SetRunning(int playerId, bool enable)
        {
            CommandExecutor.ExecuteCommand($"{Prefix} setRunning {Bool(enable)} {playerId}");
        }

        // Performs a Player Action. QUIRK: a held action (e.g. a melee strike direction like
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

        // Removes a bot from the world. There is no carbonPlayers despawn command, so we kick the
        // bot by id. Note 'serverAdmin kick' is a serverAdmin command, NOT carbonPlayers-prefixed.
        // The resulting disconnect fires OnPlayerDisconnected -> BotManager.OnBotDisconnected, which
        // untracks it (untrack is idempotent, so the direct untrack in BotManager is also fine).
        public static void Despawn(int playerId)
        {
            CommandExecutor.ExecuteCommand($"serverAdmin kick {playerId}");
        }

        // Teleports a player/bot to a world position. NOTE: 'teleport' is a general console command,
        // not carbonPlayers-prefixed. Used for summon and (Slice B) return-to-death placement.
        public static void Teleport(int playerId, Vector3 position)
        {
            CommandExecutor.ExecuteCommand($"teleport {playerId} {Fmt(position.x)},{Fmt(position.y)},{Fmt(position.z)}");
        }

        // A positional name/regtag arg, or the placeholder when empty.
        private static string Arg(string value) => string.IsNullOrEmpty(value) ? EmptyArgPlaceholder : value;

        // Invariant culture so floats never serialize with a locale comma decimal separator.
        private static string Fmt(float value) => value.ToString(CultureInfo.InvariantCulture);
        private static string Bool(bool value) => value ? "true" : "false";
    }
}
