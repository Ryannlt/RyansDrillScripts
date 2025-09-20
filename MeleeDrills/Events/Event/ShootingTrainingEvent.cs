using MDS.Core;

namespace MDS.Events
{
    public class ShootingTrainingEvent : IEvent
    {
        public EventEnum EventName => EventEnum.ShootingTraining;

        public bool Validate(object[] parameters, out string errorMessage)
        {
            errorMessage = string.Empty;
            return true; // Always valid regardless of parameter count or type. We assume input is already validated.
        }

        public void Trigger(object[] parameters)
        {
            Logger.Log($"Executing {EventName} as '{parameters[0]}'", LogLevel.DEBUG);
            CommandExecutor.ExecuteCommand($"set characterInfiniteFirearmAmmo {parameters[0]}; set drawFirearmTrajectories {parameters[0]}");
        }
    }
}