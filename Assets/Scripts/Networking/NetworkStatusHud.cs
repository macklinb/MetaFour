using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;
using DG.Tweening;

public class NetworkStatusHud : MonoBehaviour
{
    public TMPro.TextMeshProUGUI text;
    public CanvasGroup canvasGroup;

    Queue<StatusMessage> msgQueue = new Queue<StatusMessage>();

    public struct StatusMessage
    {
        public string value;
        public float time;
        public bool hold;
    }

    void Awake()
    {
        StartCoroutine(UpdateRoutine());
    }

    IEnumerator UpdateRoutine()
    {
        while (true)
        {
            while (msgQueue.Count > 0)
            {
                var msg = msgQueue.Dequeue();

                // Fade out, waiting
                yield return canvasGroup.DOFade(0.0f, 0.2f).WaitForCompletion();

                // Set text
                text.text = msg.value;

                // Fade in, waiting
                yield return canvasGroup.DOFade(1.0f, 0.2f).WaitForCompletion();

                // Start timer
                yield return new WaitForSeconds(msg.time);

                // If there are no more messages, and we don't have to hold, fade out
                if (msgQueue.Count == 0 && !msg.hold)
                    yield return canvasGroup.DOFade(0.0f, 0.2f).WaitForCompletion();

                yield return null;
            }

            yield return null;
        }
    }

    // Sets the status to the string "value", holding it there for at least "minDisplayTime" seconds. If hold is true, the text will not be faded out after minDisplayTime, until there is a new message
	public void SetText(string value, float minDisplayTime = 2.0f)
    {
        msgQueue.Enqueue(new StatusMessage(){ value = value, time = minDisplayTime, hold = false });
    }

    // Same as SetText, but sets hold to true, meaning the text will not disappear until a new message comes in
    public void SetTextPersist(string value, float minDisplayTime = 2.0f)
    {
        msgQueue.Enqueue(new StatusMessage(){ value = value, time = minDisplayTime, hold = true });
    }

    public void Hide()
    {
        msgQueue.Enqueue(new StatusMessage() { value = string.Empty, time = 0.0f, hold = false });
    }
}
