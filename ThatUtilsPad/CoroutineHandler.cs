using UnityEngine;

namespace ThatUtilsPad;

public class CoroutineHandler : MonoBehaviour
{
    public static CoroutineHandler Instance;

    private void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
}
