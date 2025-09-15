using MDS.Core;

namespace MDS.Client
{
    public static class ClientAdminDetection
    {
        private static bool loginAttempt = false;
        public static void TrySpawnAdminCheck()
        {
            if (!loginAttempt)
            {
                loginAttempt = true;
                CommandExecutor.ExecuteCommand($"rc serverAdmin playerlist");
                CommandExecutor.ExecuteCommand($"cls");
                Logger.Log("Spawn login check initiated from client.", LogLevel.INFO);
            }
        }

    }
}
