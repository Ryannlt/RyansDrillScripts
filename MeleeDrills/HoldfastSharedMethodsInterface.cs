using HoldfastSharedMethods;
using UnityEngine;
using MDS.Core;
using MDS.Systems;
using MDS.Client;
using MDS.ConfigVariables;
using MDS.ConsoleCommands;

namespace MDS
{
    public class HoldfastSharedMethodsInterface : IHoldfastSharedMethods, IHoldfastSharedMethods2, IHoldfastSharedMethods3
    {
        private static bool isServer;

        public static bool getIsServer()
        {
            return isServer;
        }

        public void OnSyncValueState(int value) { }

        public void OnUpdateSyncedTime(double time) { }

        public void OnUpdateElapsedTime(float time) { }

        public void OnUpdateTimeRemaining(float time) { }

        public void OnIsServer(bool server)
        {
            isServer = server;
            CommandExecutor.InitializeConsole();
            StateTracker.OnIsServer(server); //We initialize the StateTracker server here due to unity static class weirdness.
            if (server)
            {
                Logger.Log("Running on Server.", LogLevel.INFO);
            }
            else
            {
                Logger.Log("Running on Client.", LogLevel.INFO);
            }
        }

        public void OnIsClient(bool client, ulong steamId) { }

        public void OnDamageableObjectDamaged(GameObject damageableObject, int damageableObjectId, int shipId, int oldHp, int newHp) { }

        public void OnPlayerHurt(int playerId, byte oldHp, byte newHp, EntityHealthChangedReason reason) { }

        public void OnPlayerKilledPlayer(int killerPlayerId, int victimPlayerId, EntityHealthChangedReason reason, string additionalDetails) { }

        public void OnPlayerShoot(int playerId, bool dryShot) { }

        public void OnPlayerJoined(int playerId, ulong steamId, string playerName, string regimentTag, bool isBot)
        {
            if (isServer) StateTracker.OnPlayerJoined(playerId, steamId, playerName, regimentTag, isBot);
        }

        public void OnPlayerLeft(int playerId) { }

        public void OnPlayerSpawned(int playerId, int spawnSectionId, FactionCountry playerFaction, PlayerClass playerClass, int uniformId, GameObject playerObject)
        {
            if (isServer) StateTracker.OnPlayerSpawned(playerId, spawnSectionId, playerFaction, playerClass, uniformId, playerObject);
            if (!isServer) ClientAdminDetection.TrySpawnAdminCheck();
        }

        public void OnScorableAction(int playerId, int score, ScorableActionType reason) { }

        public void OnTextMessage(int playerId, TextChatChannel channel, string text)
        {
            // Only process system 'quiet' messages with our MDS marker
            // If it gets more complicated make a helper decode function

            if (!HoldfastSharedMethodsInterface.getIsServer() && channel == TextChatChannel.None && text.StartsWith("[MDS-CLIENT-LOG]"))
            {
                string logText = text.Substring("[MDS-CLIENT-LOG]".Length).Trim();
                Logger.Log(logText, LogLevel.INFO); // Print to client log
            }
        }

        public void OnRoundDetails(int roundId, string serverName, string mapName, FactionCountry attackingFaction, FactionCountry defendingFaction, GameplayMode gameplayMode, GameType gameType)
        {
            if (isServer) StateTracker.OnRoundDetails(roundId, serverName, mapName, attackingFaction, defendingFaction, gameplayMode, gameType);
        }

        public void OnPlayerBlock(int attackingPlayerId, int defendingPlayerId) { }

        public void OnPlayerMeleeStartSecondaryAttack(int playerId) { }

        public void OnPlayerWeaponSwitch(int playerId, string weapon) { }

        public void OnCapturePointCaptured(int capturePoint) { }

        public void OnCapturePointOwnerChanged(int capturePoint, FactionCountry factionCountry) { }

        public void OnCapturePointDataUpdated(int capturePoint, int defendingPlayerCount, int attackingPlayerCount) { }

        public void OnRoundEndFactionWinner(FactionCountry factionCountry, FactionRoundWinnerReason reason) { }

        public void OnRoundEndPlayerWinner(int playerId) { }

        public void OnPlayerStartCarry(int playerId, CarryableObjectType carryableObject) { }

        public void OnPlayerEndCarry(int playerId) { }

        public void OnPlayerShout(int playerId, CharacterVoicePhrase voicePhrase) { }

        public void OnInteractableObjectInteraction(int playerId, int interactableObjectId, GameObject interactableObject, InteractionActivationType interactionActivationType, int nextActivationStateTransitionIndex) { }

        public void PassConfigVariables(string[] value)
        {
            //Logger.Log($"Passing Config Variable: {string.Join(", ", value)}", LogLevel.INFO);
            ConfigManager.ProcessConfigVariables(value);
        }

        public void OnEmplacementPlaced(int itemId, GameObject objectBuilt, EmplacementType emplacementType) { }

        public void OnEmplacementConstructed(int itemId) { }

        public void OnBuffStart(int playerId, BuffType buff) { }

        public void OnBuffStop(int playerId, BuffType buff) { }

        public void OnShotInfo(int playerId, int shotCount, Vector3[][] shotsPointsPositions, float[] trajectileDistances, float[] distanceFromFiringPositions, float[] horizontalDeviationAngles, float[] maxHorizontalDeviationAngles, float[] muzzleVelocities, float[] gravities, float[] damageHitBaseDamages, float[] damageRangeUnitValues, float[] damagePostTraitAndBuffValues, float[] totalDamages, Vector3[] hitPositions, Vector3[] hitDirections, int[] hitPlayerIds, int[] hitDamageableObjectIds, int[] hitShipIds, int[] hitVehicleIds) { }

        public void OnVehicleSpawned(int vehicleId, FactionCountry vehicleFaction, PlayerClass vehicleClass, GameObject vehicleObject, int ownerPlayerId) { }

        public void OnVehicleHurt(int vehicleId, byte oldHp, byte newHp, EntityHealthChangedReason reason) { }

        public void OnPlayerKilledVehicle(int killerPlayerId, int victimVehicleId, EntityHealthChangedReason reason, string details) { }

        public void OnShipSpawned(int shipId, GameObject shipObject, FactionCountry shipfaction, ShipType shipType, int shipNameId) { }

        public void OnShipDamaged(int shipId, int oldHp, int newHp) { }

        public void OnAdminPlayerAction(int playerId, int adminId, ServerAdminAction action, string reason) { }

        public void OnConsoleCommand(string input, string output, bool success){}

        public void OnRCLogin(int playerId, string inputPassword, bool isLoggedIn)
        {
            if (isServer) StateTracker.OnRCLogin(playerId, inputPassword, isLoggedIn);
        }

        public void OnRCCommand(int playerId, string input, string output, bool success)
        {
            if (isServer)
            {
                ConsoleCommandHandler.ProcessConsoleCommand(playerId, input, output, success);
                StateTracker.OnRCCommand(playerId, input, output, success);
            }
        }

        public void OnPlayerPacket(int playerId, byte? instance, Vector3? ownerPosition, double? packetTimestamp, Vector2? ownerInputAxis, float? ownerRotationY, float? ownerPitch, float? ownerYaw, PlayerActions[] actionCollection, Vector3? cameraPosition, Vector3? cameraForward, ushort? shipID, bool swimming)
        {
            PlayerPacketAwaiter.HandlePlayerPacket(
                playerId,
                ownerPosition,
                packetTimestamp,
                ownerInputAxis,
                ownerRotationY,
                ownerPitch,
                ownerYaw,
                actionCollection,
                cameraPosition,
                cameraForward,
                shipID,
                swimming
            );
        }

        public void OnVehiclePacket(int vehicleId, Vector2 inputAxis, bool shift, bool strafe, PlayerVehicleActions[] actionCollection) { }

        public void OnOfficerOrderStart(int officerPlayerId, HighCommandOrderType officerOrderType, Vector3 orderPosition, float orderRotationY, int voicePhraseRandomIndex) { }

        public void OnOfficerOrderStop(int officerPlayerId, HighCommandOrderType officerOrderType) { }

        public void OnStartSpectate(int playerId, int spectatedPlayerId)
        {
            if (isServer) StateTracker.OnPlayerEnterSpectatorMode(playerId);
            if (!isServer) ClientAdminDetection.TrySpawnAdminCheck();
        }

        public void OnStopSpectate(int playerId, int spectatedPlayerId) { }

        public void OnStartFreeflight(int playerId)
        {
            if (isServer) StateTracker.OnPlayerEnterSpectatorMode(playerId); //Spectator mode logic is the same as freeflight mode
            if (!isServer) ClientAdminDetection.TrySpawnAdminCheck();
        }

        public void OnStopFreeflight(int playerId) { }

        public void OnMeleeArenaRoundEndFactionWinner(int roundId, bool attackers) { }

        public void OnPlayerConnected(int playerId, bool isAutoAdmin, string backendId)
        {
            if (isServer) StateTracker.OnPlayerConnected(playerId, isAutoAdmin, backendId);
        }

        public void OnPlayerDisconnected(int playerId)
        {
            if (isServer) StateTracker.OnPlayerDisconnected(playerId);
        }

    }
}