using UnityEngine;

namespace MDS.Core
{
    public class MonoBehaviourRunner : MonoBehaviour
    {
        private static MonoBehaviourRunner _instance;

        public static MonoBehaviourRunner Instance
        {
            get
            {
                if (_instance == null)
                {
                    var runnerObject = new GameObject("MonoBehaviourRunner");
                    _instance = runnerObject.AddComponent<MonoBehaviourRunner>();
                    DontDestroyOnLoad(runnerObject);
                }
                return _instance;
            }
        }
    }
}
