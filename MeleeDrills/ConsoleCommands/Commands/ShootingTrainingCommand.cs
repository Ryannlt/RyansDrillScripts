using System.Linq;
using MDS.Events;
using MDS.Core;

namespace MDS.ConsoleCommands
{
    public class ShootingTrainingCommand : IConsoleCommand
    {
        public ConsoleCommandEnum CommandName => ConsoleCommandEnum.ShootingTraining;

        private static bool _enabled = false;

        public bool Validate(string[] parameters, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (parameters.Length < 1) {
                errorMessage = "Missing argument. Usage: rc shootingtraining <true/false>";
                return false;
            }

            if (!bool.TryParse(parameters[0], out var result))
            {
                errorMessage = $"Invalid Input. '{parameters[0]}' must be 'true' or 'false'.";
                return false;
            }

            _enabled = bool.Parse(parameters[0]);
            return true;
        }

        public void Execute(int playerId, string[] parameters)
        {
            bool success = EventDispatcher.Trigger(EventEnum.ShootingTraining, parameters.Cast<object>().ToArray(), out string errorMessage);

            if (!success)
            {
                Logger.Log($"ShootingTraining failed: {errorMessage}", LogLevel.WARNING);
                CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} {errorMessage}");
                return;
            }

            Logger.Log("ShootingTrainingCommand executed sucessfully", LogLevel.INFO);
            CommandExecutor.ExecuteCommand($"serverAdmin privateMessage {playerId} ShootingTraining set to {_enabled}.");
        }
    }
}

