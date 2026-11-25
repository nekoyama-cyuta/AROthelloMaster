using System;
using System.Collections.Generic;

namespace AROthello.Experiment
{
    /// <summary>
    /// ExperimentStateMachine のイベントを受け取り、ガイド UI に表示する内容 (GuideViewState) を組み立てる。
    /// 描画は ExperimentGuideView が担当する。Unity に依存しないためテストしやすい。
    /// </summary>
    public class ExperimentGuidePresenter : IDisposable
    {
        private readonly ExperimentStateMachine _sm;
        private readonly GuideViewState _view = new GuideViewState();
        private readonly List<GuideMarker> _errorMarkers = new List<GuideMarker>();
        private readonly List<GuideMarker> _setupMarkers = new List<GuideMarker>();

        /// <summary>完了時に表示する文字列</summary>
        public string CompletedText { get; set; } = "完了";
        /// <summary>開始前チェック中の表示 ({0} = 直すべきマス数)</summary>
        public string SetupTextFormat { get; set; } = "初期配置 あと {0}";
        /// <summary>開始前チェックが通ったときの表示</summary>
        public string SetupOkText { get; set; } = "初期配置 OK";

        /// <summary>表示内容が変わったときに呼ばれる。</summary>
        public event Action<GuideViewState> Changed;

        public GuideViewState View => _view;

        public ExperimentGuidePresenter(ExperimentStateMachine stateMachine)
        {
            _sm = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _sm.StateChanged += OnStateChanged;
            _sm.StepStarted += OnStepStarted;
            _sm.StepCompleted += OnStepCompleted;
            _sm.ErrorDetected += OnErrorDetected;
            _sm.ErrorCleared += OnErrorCleared;
            _sm.InitialBoardStatusChanged += OnInitialBoardStatusChanged;
            _sm.TrialCompleted += OnTrialCompleted;
            Rebuild();
        }

        public void Dispose()
        {
            _sm.StateChanged -= OnStateChanged;
            _sm.StepStarted -= OnStepStarted;
            _sm.StepCompleted -= OnStepCompleted;
            _sm.ErrorDetected -= OnErrorDetected;
            _sm.ErrorCleared -= OnErrorCleared;
            _sm.InitialBoardStatusChanged -= OnInitialBoardStatusChanged;
            _sm.TrialCompleted -= OnTrialCompleted;
        }

        private void OnStateChanged(ExperimentState prev, ExperimentState next)
        {
            if (next != ExperimentState.InitialBoardCheck) _setupMarkers.Clear();
            if (next != ExperimentState.Running) _errorMarkers.Clear();
            Rebuild();
        }

        private void OnStepStarted(int index, InstructionStep step)
        {
            _errorMarkers.Clear();
            Rebuild();
        }

        private void OnStepCompleted(StepResult result)
        {
            _errorMarkers.Clear();
            Rebuild();
        }

        private void OnErrorDetected(PlacementError error)
        {
            _errorMarkers.Clear();
            if (error?.Mismatches != null)
            {
                foreach (CellMismatch m in error.Mismatches)
                {
                    if (!BoardCoord.IsInside(m.Col, m.Row)) continue;
                    _errorMarkers.Add(new GuideMarker { Col = m.Col, Row = m.Row, Kind = GuideMarkerKind.Error, Color = m.Actual });
                }
            }
            Rebuild();
        }

        private void OnErrorCleared(int stepIndex)
        {
            _errorMarkers.Clear();
            Rebuild();
        }

        private void OnInitialBoardStatusChanged(bool ok, IReadOnlyList<CellMismatch> mismatches)
        {
            _setupMarkers.Clear();
            if (mismatches != null)
            {
                foreach (CellMismatch m in mismatches)
                {
                    if (!BoardCoord.IsInside(m.Col, m.Row)) continue;
                    bool needsStone = m.Expected != StoneColor.None;
                    _setupMarkers.Add(new GuideMarker
                    {
                        Col = m.Col,
                        Row = m.Row,
                        Kind = needsStone ? GuideMarkerKind.SetupPlace : GuideMarkerKind.SetupRemove,
                        Color = needsStone ? m.Expected : m.Actual
                    });
                }
            }
            Rebuild();
        }

        private void OnTrialCompleted(TrialResult trial)
        {
            Rebuild();
        }

        /// <summary>現在の状態から表示内容を作り直して Changed を通知する。</summary>
        public void Rebuild()
        {
            _view.State = _sm.State;
            _view.Markers.Clear();
            _view.StepTotal = _sm.StepTotal;
            _view.StepNumber = 0;
            _view.IsErrorActive = _sm.State == ExperimentState.Running && _errorMarkers.Count > 0;
            _view.IsCompleted = _sm.State == ExperimentState.Completed;
            _view.ProgressText = "";

            switch (_sm.State)
            {
                case ExperimentState.InitialBoardCheck:
                    _view.Markers.AddRange(_setupMarkers);
                    _view.ProgressText = _setupMarkers.Count > 0 ? string.Format(SetupTextFormat, _setupMarkers.Count) : SetupOkText;
                    break;

                case ExperimentState.Running:
                    InstructionStep step = _sm.CurrentStep;
                    if (step == null) break; // 最終手順の確定直後 (Completed へ遷移する直前)
                    if (BoardCoord.IsInside(step.col, step.row) && BoardCoord.TryParseColor(step.color, out StoneColor color))
                    {
                        _view.Markers.Add(new GuideMarker { Col = step.col, Row = step.row, Kind = GuideMarkerKind.Target, Color = color });
                    }
                    _view.Markers.AddRange(_errorMarkers);
                    _view.StepNumber = _sm.CurrentStepIndex + 1;
                    _view.ProgressText = $"{_view.StepNumber} / {_view.StepTotal}";
                    break;

                case ExperimentState.Completed:
                    _view.ProgressText = CompletedText;
                    break;
            }

            Changed?.Invoke(_view);
        }
    }
}
