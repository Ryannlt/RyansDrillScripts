using HoldfastBridge;
using System;
using UnityEngine;

// The CommandExecutor is solely responsible for finding and executing strings to the F1 console.

namespace MDS.Core
{
    public static class CommandExecutor
    {
        private static IHoldfastGameMethods _gameMethods;

        public static void InitializeConsole(IHoldfastGameMethods holdfastGameMethods)
        {
            _gameMethods = holdfastGameMethods;
            if (_gameMethods == null)
            {
                Debug.LogError("[MDS] Console not found.");
                return;
            }

            Debug.Log("[MDS] Console found.");
        }

        public static void ExecuteCommand(string command)
        {
            if (_gameMethods == null)
            {
                Logger.Log("Cannot execute command - Console Input Field is null.", LogLevel.ERROR);
                return;
            }

            bool success = _gameMethods.ExecuteConsoleCommand(command, out var output, out Exception exception);

            if (exception != null)
            {
                Logger.Log($"Failed to execute command '{command}': {exception}", LogLevel.ERROR);
                return;
            }

            // DEBUG-only so it's silent in normal play; shows whether each command (e.g. spawnSpecific)
            // succeeded, what the game returned, and - via the log timestamp - when.
            Logger.Log($"Executed '{command}' -> success={success}, output='{output}'", LogLevel.DEBUG);
        }

        public static void SendClientLog(int playerId, string message)
        {
            // Add a standard prefix for filtering on the client
            string formatted = $"serverAdmin quietPrivateMessage {playerId} [MDS-CLIENT-LOG] {message}";
            ExecuteCommand(formatted);
        }
    }
}