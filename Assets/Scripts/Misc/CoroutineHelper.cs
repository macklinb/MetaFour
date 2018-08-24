using UnityEngine;
using System;
using System.Collections;

public class CoroutineHelper : MonoBehaviour
{
    static CoroutineHelper instance;
    public static CoroutineHelper Instance
    {
        get
        {
            if (instance == null)
            {
                var obj = new GameObject("CoroutineHelper");
                instance = obj.AddComponent<CoroutineHelper>();
            }

            return instance;
        }
    }

    public static void InvokeDelayed(float time, Action OnComplete)
    {
        Instance.StartCoroutine(InvokeDelayedInternal(time, OnComplete));
    }

    static IEnumerator InvokeDelayedInternal(float time, Action OnComplete)
    {
        yield return new WaitForSeconds(time);

        if (OnComplete != null)
            OnComplete.Invoke();
    }

    public static void InvokeDelayed(int frames, Action OnComplete)
    {
        Instance.StartCoroutine(InvokeDelayedInternal(frames, OnComplete));
    }

    static IEnumerator InvokeDelayedInternal(int frames, Action OnComplete)
    {
        for (int i = 0; i < frames; i++)
            yield return new WaitForEndOfFrame();

        if (OnComplete != null)
            OnComplete.Invoke();
    }

    public static void InvokeCondition(System.Func<bool> predicate, Action OnComplete)
    {
        Instance.StartCoroutine(InvokeConditionInternal(predicate, OnComplete));
    }

    static IEnumerator InvokeConditionInternal(System.Func<bool> predicate, Action OnComplete)
    {
        yield return new WaitUntil(predicate);

        if (OnComplete != null)
            OnComplete.Invoke();
    }
}