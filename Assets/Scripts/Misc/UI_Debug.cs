using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using DG.Tweening;
using TMPro;

public class UI_Debug : MonoBehaviour
{
    // Info Debug

    public bool showOnStart;
    public RectTransform consoleRoot;
    public RectTransform consoleLayout;
    public GameObject entryPrefab;

    CanvasGroup consoleCanvas;
    bool consoleShowing;
    bool showStackTraces = false;

    List<LogEntry> consoleHistory;

    const string HEX_COLOUR_WHITE = "#FFFFFF";
    const string HEX_COLOUR_GREY = "#BDBDBD";
    const string HEX_COLOUR_RED = "#F44336";
    const string HEX_COLOUR_YELLOW = "#FFEB3B";

    [System.Serializable]
    public struct LogEntry
    {
        public string logString;
        public string stackTraceString;
        public LogType logType;

        public TMPro.TextMeshProUGUI textMesh;

        public bool IsStackTraceShowing
        {
            get { return textMesh.text.Length > logString.Length; }
        }

        public void SetStackTraceVisible(bool value)
        {
            // Add stack trace to text
            if (value == true && !IsStackTraceShowing)
                textMesh.text = logString + stackTraceString;

            // Remove stack trace from text
            else if (value == false && IsStackTraceShowing)
                textMesh.text = logString;
        }
    }

    void Awake()
    {
        consoleHistory = new List<LogEntry>();
        Application.logMessageReceivedThreaded += OnLogMessage;
    }

    void OnDestroy()
    {
        Application.logMessageReceivedThreaded -= OnLogMessage;
    }

    void Start()
    {
        consoleCanvas = consoleRoot.GetComponent<CanvasGroup>();
        consoleShowing = true;
        
        if (!showOnStart)
            ToggleLog(false, true);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.BackQuote))
            ToggleLog(!consoleShowing);
    }

    void OnLogMessage(string message, string stackTrace, LogType type)
    {
        string colour = HEX_COLOUR_WHITE;

        if (type == LogType.Warning)
            colour = HEX_COLOUR_YELLOW;
        else if (type == LogType.Error)
            colour = HEX_COLOUR_RED;
        
        var entryObj = GameObject.Instantiate(entryPrefab);
        entryObj.name = entryPrefab.name + "_" + (consoleHistory.Count - 1);

        var entryRectTransform = (RectTransform)entryObj.transform;
        entryRectTransform.SetParent(consoleLayout);
        entryObj.transform.localScale = Vector3.one;

        // Create a new LogEntry for this message
        var logEntry = new LogEntry()
        {
            logString = string.Format("\n<color={0}>{1}</color>\n", colour, message),
            stackTraceString = string.Format("<color={0}>{1}</color>", HEX_COLOUR_GREY, stackTrace),
            logType = type,
            textMesh = entryObj.GetComponent<TMPro.TextMeshProUGUI>()
        };

        logEntry.textMesh.text = (showStackTraces) ? logEntry.logString : logEntry.logString + logEntry.stackTraceString;
        logEntry.SetStackTraceVisible(showStackTraces);

        consoleHistory.Add(logEntry);

        // Hook up the button after the entry is added
        int lastIndex = consoleHistory.Count - 1;
        entryObj.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(() => ToggleStackTraces(lastIndex));
    }

    public void ToggleLog(bool value, bool immediate = false)
    {
        if (consoleShowing == false && value == false)
            return;

        consoleShowing = value;
        Vector2 canvasSize = ((RectTransform)transform).sizeDelta;
        
        consoleCanvas.DOFade(consoleShowing ? 1.0f : 0.0f, immediate ? 0.0f : 0.2f)
            .SetEase(Ease.OutQuad).SetUpdate(true);
        consoleRoot.DOAnchorPosY(consoleShowing ? 0.0f : canvasSize.y / 2f, immediate ? 0.0f : 0.2f)
            .SetEase(Ease.OutQuad).SetUpdate(true);
    }

    // Called by DebugEntry, and toggles showing the stack trace for a specific log
    public void ToggleStackTraces(int index)
    {
        consoleHistory[index].SetStackTraceVisible(!consoleHistory[index].IsStackTraceShowing);
    }

    string GetExternalStoragePath()
    {
        try
        {
            var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            var currentActivity = player.GetStatic<AndroidJavaObject>("currentActivity");
            return currentActivity.Call<AndroidJavaObject>("getFilesDir")
                                  .Call<string>("getAbsolutePath");
        }
        catch (System.Exception e)
        {
            return string.Empty;
        }
    }

    void SaveDebugLogToFile(string filePath)
    {
        try
        {
            if (consoleHistory != null && !string.IsNullOrEmpty(filePath))
            {
                System.IO.File.WriteAllLines(filePath, consoleHistory.Select(x => x + "\n").ToArray());
                Debug.Log("UI_Debug : SaveDebugLogToFile - Wrote " + consoleHistory.Count + " logs to " + filePath);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("UI_Debug : SaveDebugLogToFile - An exception occurred\n" + e.Message);
        }
    }
}
