using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace AROthello
{
    /// <summary>
    /// 実機 (Android / XREAL) 画面デバッグ用ロガークラス
    /// 画面上の UI Text に最大20行のログをリングバッファで表示するシングルトン
    /// </summary>
    public class CustomScreenLogger : MonoBehaviour
    {
        public static CustomScreenLogger Instance { get; private set; }

        [Header("UI References")]
        [Tooltip("ログテキストを表示する UI Text (XR グラス HUD 用)")]
        [SerializeField] private Text logText;

        [Tooltip("手元の Beam Pro 画面にログを表示する UI Text (任意)")]
        [SerializeField] private Text handheldLogText;

        [Header("Logger Settings")]
        [Tooltip("保持する最大行数 (リングバッファ)")]
        [SerializeField] private int maxLines = 20;

        [Tooltip("Unity の Debug.Log を自動キャプチャするかどうか")]
        [SerializeField] private bool captureUnityLogs = true;

        private readonly Queue<string> _logLines = new Queue<string>();
        private readonly StringBuilder _sb = new StringBuilder(2048);
        private bool _isDirty = false;

        public Text LogTextComponent
        {
            get => logText;
            set
            {
                logText = value;
                _isDirty = true;
            }
        }

        public Text HandheldLogTextComponent
        {
            get => handheldLogText;
            set
            {
                handheldLogText = value;
                _isDirty = true;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnEnable()
        {
            if (captureUnityLogs)
            {
                Application.logMessageReceivedThreaded += HandleUnityLog;
            }
        }

        private void OnDisable()
        {
            Application.logMessageReceivedThreaded -= HandleUnityLog;
        }

        private void Update()
        {
            if (_isDirty)
            {
                _isDirty = false;
                FlushToText();
            }
        }

        /// <summary>
        /// 外部からログを追加する静的メソッド
        /// </summary>
        public static void Log(string message)
        {
            if (Instance != null)
            {
                Instance.AddLog(message, LogType.Log);
            }
            else
            {
                Debug.Log($"[ScreenLog] {message}");
            }
        }

        public static void LogWarning(string message)
        {
            if (Instance != null)
            {
                Instance.AddLog(message, LogType.Warning);
            }
            else
            {
                Debug.LogWarning($"[ScreenLog] {message}");
            }
        }

        public static void LogError(string message)
        {
            if (Instance != null)
            {
                Instance.AddLog(message, LogType.Error);
            }
            else
            {
                Debug.LogError($"[ScreenLog] {message}");
            }
        }

        public static void Clear()
        {
            if (Instance != null)
            {
                lock (Instance._logLines)
                {
                    Instance._logLines.Clear();
                    Instance._isDirty = true;
                }
            }
        }

        private void HandleUnityLog(string condition, string stackTrace, LogType type)
        {
            // 画面ロガー自身の更新に関するログはループ防止のためスキップ
            if (condition.StartsWith("[ScreenLog]")) return;

            AddLog(condition, type);
        }

        public void AddLog(string message, LogType type = LogType.Log)
        {
            string timeStr = DateTime.Now.ToString("HH:mm:ss");
            string colorPrefix;

            switch (type)
            {
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    colorPrefix = "<color=#FF5555>[ERR] ";
                    break;
                case LogType.Warning:
                    colorPrefix = "<color=#FFDD33>[WARN] ";
                    break;
                default:
                    colorPrefix = "<color=#E8E8E8>[LOG] ";
                    break;
            }

            string formatted = $"{colorPrefix}{timeStr} {message}</color>";

            lock (_logLines)
            {
                _logLines.Enqueue(formatted);
                while (_logLines.Count > maxLines)
                {
                    _logLines.Dequeue();
                }
                _isDirty = true;
            }
        }

        private void FlushToText()
        {
            if (logText == null && handheldLogText == null) return;

            _sb.Clear();
            lock (_logLines)
            {
                foreach (var line in _logLines)
                {
                    _sb.AppendLine(line);
                }
            }

            string content = _sb.ToString();
            if (logText != null) logText.text = content;
            if (handheldLogText != null) handheldLogText.text = content;
        }
    }
}
