using System;
using System.IO;
using UnityEngine;

namespace AROthello.Experiment
{
    /// <summary>録るもの。</summary>
    public enum RecordingSource
    {
        /// <summary>見えている景色 (グラスのカメラ映像に AR 表示を重ねる)</summary>
        SeenView = 0,
        /// <summary>AR 表示だけ</summary>
        VirtualOnly = 1,
        /// <summary>カメラ映像だけ</summary>
        CameraOnly = 2
    }

    /// <summary>録画の画質。</summary>
    public enum RecordingQuality
    {
        /// <summary>1280x720, 30fps</summary>
        Standard720 = 0,
        /// <summary>1920x1080, 30fps</summary>
        High1080 = 1
    }

    /// <summary>
    /// グラスの画面を録画する仕組み。実機では XREAL SDK の XREALVideoCapture を使うアダプター
    /// (Assets/Othello/Scripts/Experiment/XrealGlassesRecorder) が実装する。
    /// </summary>
    public interface IGlassesRecorder
    {
        /// <summary>この端末で録画できるか (エディタでは false)。</summary>
        bool IsAvailable { get; }
        bool IsRecording { get; }
        /// <summary>
        /// path (絶対パスの .mp4) へ録画を始める。onStarted(成功, 実際に録っているもの, エラー)。
        /// 合成を頼んでもカメラを開けないときは、実際に録っているものが AR 表示だけになる。
        /// </summary>
        void StartRecording(string path, RecordingSource source, RecordingQuality quality, Action<bool, RecordingSource, string> onStarted);
        /// <summary>録画を止めてファイルを閉じる。onStopped(成功, エラー)。</summary>
        void StopRecording(Action<bool, string> onStopped);
    }

    /// <summary>録画の設定 (実験者画面の設定シートで変える)。</summary>
    [Serializable]
    public class RecordingSettings
    {
        public bool enabled = true;
        public RecordingSource source = RecordingSource.SeenView;
        public RecordingQuality quality = RecordingQuality.Standard720;
    }

    /// <summary>録画 1 本分の結果。</summary>
    public class RecordingResult
    {
        public string ExperimentId = "";
        public string FileName = "";
        public string Path = "";
        public double DurationSec;
        public RecordingSource Requested;
        public RecordingSource Actual;
        /// <summary>「最初からやり直す」などで中断した試行の録画 (ファイル名に _reset が付く)</summary>
        public bool WasReset;
        public bool Success;
        public string Error = "";

        /// <summary>合成を頼んだが AR 表示だけになった、など。</summary>
        public bool FellBack => Success && Actual != Requested;
    }

    /// <summary>録画の状態。</summary>
    public enum RecordingStatus
    {
        Off,
        Starting,
        Recording,
        Stopping,
        Failed
    }

    /// <summary>
    /// 試行に合わせて録画を始めて止める (Unity に依存しない)。
    /// 試行開始 (TrialStarted) で ExperimentLogs/videos/{実験ID}.mp4 に録画を始め、試行完了で止める。
    /// 1 手戻る・認識を待たず次へでは止めない。試行を中断したとき (やり直し・パターンの変更) は止めて、
    /// 途中までの動画を {実験ID}_reset.mp4 として残す。
    /// </summary>
    public class ExperimentRecordingController : IDisposable
    {
        public const string VideoFolder = "videos";
        /// <summary>XREAL SDK の既定のビットレート (10.24 Mbps) から見積もる 1 秒あたりのバイト数。</summary>
        public const double BytesPerSecond = 10240000 / 8.0;

        private readonly ExperimentStateMachine _sm;
        private readonly IGlassesRecorder _recorder;
        private readonly string _directory;
        private readonly Func<TrialMetadata> _metadata;
        private readonly Func<double> _clock;
        private RecordingResult _current;
        private double _startedAt;

        public RecordingSettings Settings { get; set; } = new RecordingSettings();
        public RecordingStatus Status { get; private set; } = RecordingStatus.Off;
        /// <summary>完了した (または中断した) 直前の録画。</summary>
        public RecordingResult LastResult { get; private set; }
        public string LastError { get; private set; }
        public bool IsAvailable => _recorder != null && _recorder.IsAvailable;
        public string VideoDirectory => Path.Combine(_directory, VideoFolder);
        /// <summary>いま録っているもの (合成できずに AR 表示だけになったときはそれ)。</summary>
        public RecordingSource CurrentSource => _current != null ? _current.Actual : Settings.source;
        public double ElapsedSec => Status == RecordingStatus.Recording ? Math.Max(0, _clock() - _startedAt) : 0;

        /// <summary>状態が変わったとき。</summary>
        public event Action Changed;

        public ExperimentRecordingController(ExperimentStateMachine stateMachine, IGlassesRecorder recorder, string directory, Func<TrialMetadata> metadata, Func<double> clock)
        {
            _sm = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _recorder = recorder;
            _directory = directory ?? "";
            _metadata = metadata ?? (() => null);
            _clock = clock ?? (() => 0);
            _sm.TrialStarted += OnTrialStarted;
            _sm.TrialCompleted += OnTrialCompleted;
            _sm.StateChanged += OnStateChanged;
        }

        public void Dispose()
        {
            _sm.TrialStarted -= OnTrialStarted;
            _sm.TrialCompleted -= OnTrialCompleted;
            _sm.StateChanged -= OnStateChanged;
            if (Status == RecordingStatus.Recording || Status == RecordingStatus.Starting) Stop(true);
        }

        /// <summary>その実験ID の試行を録画したファイル名 (無ければ空)。</summary>
        public string VideoFileFor(string experimentId)
        {
            RecordingResult r = LastResult;
            return r != null && r.Success && !r.WasReset && !string.IsNullOrEmpty(experimentId) && r.ExperimentId == experimentId ? r.FileName : "";
        }

        /// <summary>同じ名前のファイルがあれば _2, _3 … を付ける。</summary>
        public static string UniquePath(string directory, string baseName, string extension = ".mp4")
        {
            string path = Path.Combine(directory, baseName + extension);
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(directory, $"{baseName}_{n}{extension}");
            return path;
        }

        /// <summary>空き容量で今の画質なら何時間録れるか (ビットレートは画質によらず SDK の既定値)。</summary>
        public static double EstimateHours(long availableBytes) => availableBytes <= 0 ? 0 : availableBytes / BytesPerSecond / 3600.0;

        private void OnTrialStarted(InstructionPatternData pattern, double time)
        {
            if (!Settings.enabled || !IsAvailable) return;
            if (Status == RecordingStatus.Recording || Status == RecordingStatus.Starting) Stop(true);

            TrialMetadata meta = _metadata();
            string id = meta != null && !string.IsNullOrEmpty(meta.experimentId) ? meta.experimentId : "trial_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string baseName = ExperimentCsv.SanitizeFileName(id, "trial");
            try
            {
                Directory.CreateDirectory(VideoDirectory);
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
                return;
            }
            string path = UniquePath(VideoDirectory, baseName);
            var current = new RecordingResult
            {
                ExperimentId = meta != null ? meta.experimentId : "",
                Path = path,
                FileName = Path.GetFileName(path),
                Requested = Settings.source,
                Actual = Settings.source
            };
            _current = current;
            Status = RecordingStatus.Starting;
            Changed?.Invoke();
            try
            {
                _recorder.StartRecording(path, Settings.source, Settings.quality, (ok, actual, error) =>
                {
                    if (_current != current) return; // 待っている間に中断された
                    if (!ok)
                    {
                        _current = null;
                        Fail(string.IsNullOrEmpty(error) ? "録画を始められませんでした" : error);
                        return;
                    }
                    current.Actual = actual;
                    _startedAt = _clock();
                    Status = RecordingStatus.Recording;
                    LastError = null;
                    Changed?.Invoke();
                });
            }
            catch (Exception ex)
            {
                _current = null;
                Fail(ex.Message);
            }
        }

        private void OnTrialCompleted(TrialResult trial)
        {
            if (Status == RecordingStatus.Recording || Status == RecordingStatus.Starting) Stop(false);
        }

        private void OnStateChanged(ExperimentState prev, ExperimentState next)
        {
            // 試行中に待機・位置合わせ・初期配置へ戻った = 中断 (完了は OnTrialCompleted で止める)
            if (prev == ExperimentState.Running && next != ExperimentState.Completed &&
                (Status == RecordingStatus.Recording || Status == RecordingStatus.Starting))
            {
                Stop(true);
            }
            if (next == ExperimentState.Idle && Status == RecordingStatus.Failed)
            {
                Status = RecordingStatus.Off;
                Changed?.Invoke();
            }
        }

        private void Stop(bool reset)
        {
            RecordingResult r = _current;
            _current = null;
            if (r == null) return;
            r.WasReset = reset;
            r.DurationSec = Status == RecordingStatus.Recording ? Math.Max(0, _clock() - _startedAt) : 0;
            bool wasRecording = Status == RecordingStatus.Recording;
            Status = RecordingStatus.Stopping;
            Changed?.Invoke();

            void Finish(bool ok, string error)
            {
                r.Success = ok && wasRecording;
                r.Error = error ?? "";
                if (r.Success && reset) RenameAsReset(r);
                LastResult = r;
                if (r.Success) { Status = RecordingStatus.Off; LastError = null; }
                else Fail(string.IsNullOrEmpty(r.Error) ? "録画を保存できませんでした" : r.Error);
                Changed?.Invoke();
            }

            try
            {
                _recorder.StopRecording(Finish);
            }
            catch (Exception ex)
            {
                Finish(false, ex.Message);
            }
        }

        private static void RenameAsReset(RecordingResult r)
        {
            try
            {
                if (!File.Exists(r.Path)) return;
                string dir = Path.GetDirectoryName(r.Path) ?? "";
                string target = UniquePath(dir, Path.GetFileNameWithoutExtension(r.Path) + "_reset");
                File.Move(r.Path, target);
                r.Path = target;
                r.FileName = Path.GetFileName(target);
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
            }
        }

        private void Fail(string error)
        {
            LastError = error;
            Status = RecordingStatus.Failed;
            Changed?.Invoke();
        }
    }

    /// <summary>本体の空き容量。</summary>
    public static class StorageInfo
    {
        /// <summary>path があるドライブの空き容量 (バイト)。分からなければ -1。</summary>
        public static long AvailableBytes(string path)
        {
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var statFs = new AndroidJavaObject("android.os.StatFs", path))
                {
                    return statFs.Call<long>("getAvailableBytes");
                }
#else
                string root = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(string.IsNullOrEmpty(path) ? "." : path));
                return new DriveInfo(root).AvailableFreeSpace;
#endif
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }
}
