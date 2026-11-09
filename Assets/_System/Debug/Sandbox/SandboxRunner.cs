#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using UnityEngine;

/// <summary>Coroutine host of the sandbox commands (scene-de-test): one-per-frame queues, timed measures, deferred
/// preset. Created on demand (DontDestroyOnLoad), dies with the Play session. A routine that throws is reported and
/// stopped: a command never throws at the console.</summary>
public sealed class SandboxRunner : MonoBehaviour
{
    private static SandboxRunner _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => _instance = null;

    public static Coroutine Run(IEnumerator routine)
    {
        if (_instance == null)
        {
            var go = new GameObject("[SandboxRunner]") { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<SandboxRunner>();
        }
        return _instance.StartCoroutine(Guard(routine));
    }

    public static void Report(string line)
    {
        Debug.Log(line);
        DeveloperConsole.Print(line);
    }

    private static IEnumerator Guard(IEnumerator routine)
    {
        while (true)
        {
            object current;
            try
            {
                if (!routine.MoveNext())
                    yield break;
                current = routine.Current;
            }
            catch (Exception ex)
            {
                Report($"[Sandbox] erreur : {ex.GetType().Name}: {ex.Message}");
                yield break;
            }
            yield return current;
        }
    }
}
#endif
