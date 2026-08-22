using UnityEngine;

namespace MDS.Core
{
    public class MonoBehaviourRunner : MonoBehaviour
    {
        private static MonoBehaviourRunner _instance;
        private static bool _clearedPreviousLoad;

        private const string RunnerName = "MonoBehaviourRunner";

        // The mod reloads on every map change, so the runner is recreated lazily and kept alive across scenes.
        public static MonoBehaviourRunner Instance
        {
            get
            {
                if (_instance != null) return _instance;

                // Only ever done once per load. If this load's own runner is destroyed later it just makes a new
                // one, so a still-live previous load can never come back and take out the current load's runner.
                if (!_clearedPreviousLoad)
                {
                    _clearedPreviousLoad = true;

                    GameObject stale = GameObject.Find(RunnerName);
                    if (stale != null)
                    {
                        Destroy(stale);
                        Logger.Log("Removed the MonoBehaviourRunner left behind by a previous mod load.", LogLevel.INFO);
                    }
                }

                var runnerObject = new GameObject(RunnerName);
                _instance = runnerObject.AddComponent<MonoBehaviourRunner>();
                DontDestroyOnLoad(runnerObject);
                return _instance;
            }
        }
    }
}
