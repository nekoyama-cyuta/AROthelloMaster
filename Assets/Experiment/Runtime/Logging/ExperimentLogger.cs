using System;
using System.IO;
using UnityEngine;

namespace AROthello.Experiment
{
    /// <summary>
    /// 試行が完了するたびに実験ログ CSV を Application.persistentDataPath/{outputFolder} へ自動で書き出す。
    /// - {subject}_{condition}_o{order}_{pattern}_{日時}.csv : 1 行 = 1 手順
    /// - trial_summary.csv : 1 行 = 1 試行 (総作業時間 TCT、誤り数、平均 FPS など)
    /// </summary>
    public class ExperimentLogger : MonoBehaviour
    {
        [SerializeField] private ExperimentController controller;

        [Header("Trial Metadata (次の試行に使う)")]
        [SerializeField] private TrialMetadata metadata = new TrialMetadata();

        [Header("Output")]
        [Tooltip("persistentDataPath 配下の出力フォルダ名")]
        [SerializeField] private string outputFolder = "ExperimentLogs";

        private ExperimentLogRecorder _recorder;

        /// <summary>次の試行に使う被験者情報。実験者 UI から書き換える。</summary>
        public TrialMetadata Metadata
        {
            get => metadata;
            set
            {
                metadata = value ?? new TrialMetadata();
                if (_recorder != null) _recorder.Metadata = metadata;
            }
        }

        public string OutputDirectory => Path.Combine(Application.persistentDataPath, outputFolder);
        public ExperimentLogRecorder Recorder => _recorder;

        /// <summary>書き出しに成功したとき (ログ, パス)。</summary>
        public event Action<TrialLog, string> TrialLogged;

        private void OnEnable()
        {
            if (controller == null) controller = FindFirstObjectByType<ExperimentController>();
            if (controller == null)
            {
                Debug.LogWarning("[ExperimentLogger] ExperimentController was not found.");
                return;
            }
            _recorder = new ExperimentLogRecorder(controller.StateMachine, new FileTrialLogSink(OutputDirectory))
            {
                Metadata = metadata
            };
            _recorder.TrialLogged += OnTrialLogged;
            _recorder.TrialLogFailed += OnTrialLogFailed;
        }

        private void OnDisable()
        {
            if (_recorder == null) return;
            _recorder.TrialLogged -= OnTrialLogged;
            _recorder.TrialLogFailed -= OnTrialLogFailed;
            _recorder.Dispose();
            _recorder = null;
        }

        private void Update()
        {
            _recorder?.AddFrame(Time.unscaledDeltaTime);
        }

        private void OnTrialLogged(TrialLog log, string path)
        {
            Debug.Log($"{log.SummaryText} -> {path}");
            TrialLogged?.Invoke(log, path);
        }

        private void OnTrialLogFailed(TrialLog log, string error)
        {
            Debug.LogError($"[ExperimentLogger] Failed to write log: {error}\n{log.SummaryText}");
        }
    }
}
