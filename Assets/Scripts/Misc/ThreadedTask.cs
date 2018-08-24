using System;
using System.Threading;
using System.Collections;

public class ThreadedTask
{
    private readonly Action action;
    private Thread thread;

    // It should be noted that OnComplete will run in the main Unity thread
    public Action OnComplete;

    public bool IsDone { get; private set; }

    public ThreadedTask(Action action)
    {
        this.action = action;
    }

    public ThreadedTask(Action action, Action onComplete)
    {
        this.action = action;
        this.OnComplete = onComplete;
    }

    // Call to begin the action in a new thread
    public void Start()
    {
        IsDone = false;

        ThreadStart ts = new ThreadStart(DoDelegatedMethod);
        thread = new Thread(ts);
        thread.Start();
    }
        
    public IEnumerator WaitFor()
    {
        while (!IsDone) yield return null;
    }

    private void DoDelegatedMethod()
    {
        if (action != null)
            action.Invoke();

        IsDone = true;

        UnityMainThreadDispatcher.Instance.Enqueue(OnComplete);
    }
}