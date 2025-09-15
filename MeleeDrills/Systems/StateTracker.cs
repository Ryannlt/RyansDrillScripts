using System.Collections.Generic;
using System.Linq;
using HoldfastSharedMethods;
using UnityEngine;
using MDS.Core;
using MDS.ConsoleCommands;


namespace MDS.Systems
{
    public class StateTracker
    {
        private static bool _initialized = false;
        private static bool _isServer;
        private static int _roundId;
        private static string _serverName;
        private static string _mapName;
        private static FactionCountry _attackingFaction;
        private static FactionCountry _defendingFaction;
        private static GameplayMode _gameMode;
        private static GameType _gameType;

        private readonly static List<IPlayer> _allPlayers = new();
        private readonly static List<IPlayer> _attackingPlayers = new();
        private readonly static List<IPlayer> _defendingPlayers = new();
        private readonly static List<IPlayer> _spectatorPlayers = new();

        private static readonly Dictionary<int, bool> _connectedPlayers = new();

        // Public readonly accessors
        public static bool IsServer => _isServer;
        public static int RoundId => _roundId;
        public static string ServerName => _serverName;
        public static string MapName => _mapName;
        public static FactionCountry AttackingFaction => _attackingFaction;
        public static FactionCountry DefendingFaction => _defendingFaction;
        public static GameplayMode GameMode => _gameMode;
        public static GameType GameType => _gameType;

        public static IReadOnlyList<IPlayer> AllPlayers => _allPlayers;
        public static IReadOnlyList<IPlayer> AttackingPlayers => _attackingPlayers;
        public static IReadOnlyList<IPlayer> DefendingPlayers => _defendingPlayers;
        public static IReadOnlyList<IPlayer> SpectatorPlayers => _spectatorPlayers;

        public static void OnIsServer(bool server)
        {
            if (!_initialized)
            {
                _initialized = true;
                _isServer = server;
                Logger.Log($"StateTracker running on {(server ? "Server" : "Client")}.", LogLevel.INFO);
            }
        }

        public static void OnRoundDetails(int roundId, string serverName, string mapName, FactionCountry attackingFaction, FactionCountry defendingFaction, GameplayMode gameplayMode, GameType gameType)
        {
            _roundId = roundId;
            _serverName = serverName;
            _mapName = mapName;
            _attackingFaction = attackingFaction;
            _defendingFaction = defendingFaction;
            _gameMode = gameplayMode;
            _gameType = gameType;

            NewRoundCleanup();
            ArenaManager.ApplyStagedArenas();

            Logger.Log($"Round {roundId} on {mapName}. Mode: {gameplayMode}, Type: {gameType}. Attacking: {attackingFaction}, Defending: {defendingFaction}.", LogLevel.INFO);
        }

        private static void NewRoundCleanup()
        {
            Logger.Log("Cleaning up for new round. Clearing bot data and resetting team lists.", LogLevel.INFO);

            _allPlayers.RemoveAll(p => p.IsBot);
            _attackingPlayers.Clear();
            _defendingPlayers.Clear();
            _spectatorPlayers.Clear();

            foreach (var player in _allPlayers)
            {
                _spectatorPlayers.Add(player);
            }

            XvXCommand.Reset();
            GroupfightCommand.Reset();
        }

        public static void OnPlayerConnected(int playerId, bool isAutoAdmin, string backendId)
        {
            _connectedPlayers.Add(playerId, false);
            Logger.Log($"Player connected: {playerId}", LogLevel.DEBUG);
        }

        public static void OnPlayerJoined(int playerId, ulong steamId, string playerName, string regimentTag, bool isBot)
        {
            if (!IsServer) return;

            IPlayer newPlayer;

            if (isBot)
            {
                newPlayer = new Bot(playerId, playerName, regimentTag);
            }
            else
            {
                bool isAdmin = _connectedPlayers.TryGetValue(playerId, out var isCurrentlyAdmin);
                newPlayer = new Player(playerId, steamId, playerName, regimentTag, isAdmin);
            }

            _allPlayers.Add(newPlayer);

            if (!isBot)
                _spectatorPlayers.Add(newPlayer);

            Logger.Log($"Player Joined: {newPlayer}", LogLevel.DEBUG);
        }

        public static void OnPlayerDisconnected(int playerId)
        {
            var player = GetPlayerById(playerId);
            if (player == null) return;

            Logger.Log($"Player Disconnected: {player}", LogLevel.DEBUG);
            _allPlayers.Remove(player);
            _attackingPlayers.Remove(player);
            _defendingPlayers.Remove(player);
            _spectatorPlayers.Remove(player);
            _connectedPlayers.Remove(playerId);
        }

        public static void OnPlayerSpawned(int playerId, int spawnSectionId, FactionCountry faction, PlayerClass playerClass, int uniformId, GameObject playerObject)
        {
            var player = GetPlayerById(playerId);
            if (player == null)
            {
                Logger.Log($"An untracked player {player} has spawned", LogLevel.ERROR);
                return;
            }

            player.AssignSpawnDetails(spawnSectionId, faction, playerClass, uniformId, playerObject);

            // If the player is spawning into the attacking team
            if (faction == AttackingFaction)
            {
                if (!_attackingPlayers.Contains(player))
                {
                    _defendingPlayers.Remove(player);
                    _spectatorPlayers.Remove(player);
                    _attackingPlayers.Add(player);
                }
            }
            // If the player is spawning into the defending team
            else if (faction == DefendingFaction)
            {
                if (!_defendingPlayers.Contains(player))
                {
                    _attackingPlayers.Remove(player);
                    _spectatorPlayers.Remove(player);
                    _defendingPlayers.Add(player);
                }
            }
            else
            {
                Logger.Log($"Invalid faction {faction} for player {player.PlayerName}.", LogLevel.WARNING);
            }

            Logger.Log($"Player Spawned: {player}", LogLevel.DEBUG);
        }

        public static void OnPlayerEnterSpectatorMode(int playerId)
        {
            var player = GetPlayerById(playerId);
            if (player == null) return;

            _attackingPlayers.Remove(player);
            _defendingPlayers.Remove(player);

            if (!_spectatorPlayers.Contains(player))
                _spectatorPlayers.Add(player);

            Logger.Log($"Player {player.PlayerName} entered spectator mode.", LogLevel.DEBUG);
        }

        public static void OnRCLogin(int playerId, string inputPassword, bool isLoggedIn)
        {
            // Check in fully joined players
            var player = GetPlayerById(playerId);
            if (player is Player p)
            {
                p.IsAdmin = isLoggedIn;
                Logger.Log($"Player {p.PlayerId} login attempt: {(isLoggedIn ? "Granted" : "Denied")}", LogLevel.DEBUG);
                CommandExecutor.SendClientLog(playerId, "Logged in as admin.");
                return;
            }

            // Check in pre-joined players
            if (isLoggedIn && _connectedPlayers.ContainsKey(playerId))
            {
                _connectedPlayers[playerId] = isLoggedIn;
                Logger.Log($"Pre-joined player {playerId} login attempt: {(isLoggedIn ? "Granted" : "Denied")}", LogLevel.DEBUG);
                CommandExecutor.SendClientLog(playerId, "Logged in as admin.");
                return;
            }

            Logger.Log($"OnRCLogin: No tracked player with ID {playerId} found in either joined or pending lists.", LogLevel.WARNING);
        }

        public static void OnRCCommand(int playerId, string input, string output, bool success)
        {
            if (!IsServer || !success) return;

            // If they are a connected player but not yet known to be admin, update their status
            if (_connectedPlayers.TryGetValue(playerId, out bool isCurrentlyAdmin) && !isCurrentlyAdmin)
            {
                _connectedPlayers[playerId] = true;
                Logger.Log($"PlayerId {playerId} marked as admin via RC command.", LogLevel.DEBUG);
                CommandExecutor.SendClientLog(playerId, "Logged in as admin.");
            }

            // If the player has fully joined and exists in _allPlayers, update their admin flag. (Path should theoretically never be reached)
            var player = _allPlayers.Find(p => p.PlayerId == playerId);
            if (player is Player concretePlayer && !concretePlayer.IsAdmin)
            {
                concretePlayer.IsAdmin = true;
                Logger.Log($"Player {concretePlayer.PlayerName} marked as admin via RC command.", LogLevel.DEBUG);
                CommandExecutor.SendClientLog(playerId, "Logged in as admin.");
            }
        }

        public static IPlayer GetPlayerById(int playerId)
        {
            return _allPlayers.Find(p => p.PlayerId == playerId);
        }

        public static bool IsSpectator(int playerId)
        {
            return _spectatorPlayers.Any(p => p.PlayerId == playerId);
        }

        public static bool IsPlayerAdmin(int playerId)
        {
            return _allPlayers.FirstOrDefault(p => p.PlayerId == playerId) is Player player && player.IsAdmin;
        }

    }
}
