using UnityEngine;

namespace MDS.Core
{
    public class MonoBehaviourRunner : MonoBehaviour
    {
        private static MonoBehaviourRunner _instance;
        private static bool _clearedPreviousLoad;

        private const string RunnerName = "MonoBehaviourRunner";

        // The mod is loaded fresh on every map change, so our statics start over, but this object is
        // DontDestroyOnLoad and survives the change along with every coroutine the previous load left running.
        // Those keep driving the same bots next to the new load's own tick loop, and the two streams of input
        // commands overwrite each other, which leaves bots shuffling on the spot instead of moving.
        //
        // So the first time a load needs the runner, clear out the one the previous load left behind: its
        // coroutines die with it. It is found by name rather than by type, because the previous load's
        // MonoBehaviourRunner is a different type identity from this one's and would not match.
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
