using UnityEngine;
using UnityEngine.UI;

// The CommandExecutor is solely responsible for finding and executing strings to the F1 console.

namespace MDS.Core
{
    public static class CommandExecutor
    {
        private static InputField consoleInputField;

        // Locate the console input field in the UI
        public static void InitializeConsole()
        {
            Logger.Log("Searching for Game Console Panel...", LogLevel.INFO);
            var canvases = Resources.FindObjectsOfTypeAll<Canvas>();
            foreach (var canvas in canvases)
            {
                if (canvas.name.Equals("Game Console Panel", System.StringComparison.OrdinalIgnoreCase))
                {
                    consoleInputField = canvas.GetComponentInChildren<InputField>(true);
                    if (consoleInputField != null)
                    {
                        Logger.Log("Found Game Console Panel.", LogLevel.INFO);
                    }
                    else
                    {
                        Logger.Log("Could not find the Game Console Panel input field.", LogLevel.ERROR);
                    }
                    break;
                }
            }
        }

        // Execute a command in the game console
        public static void ExecuteCommand(string command)
        {
            if (consoleInputField == null)
            {
                Logger.Log("Cannot execute command - Console Input Field is null.", LogLevel.WARNING);
                return;
            }

            consoleInputField.onEndEdit.Invoke(command);
            Logger.Log($"Executed Console Command: '{command}'", LogLevel.DEBUG);
        }

        public static void SendClientLog(int playerId, string message)
        {
            // Add a standard prefix for filtering on the client
            string formatted = $"serverAdmin quietPrivateMessage {playerId} [MDS-CLIENT-LOG] {message}";
            ExecuteCommand(formatted);
        }
    }
}