using System;
using System.IO;
using AROthello.Experiment;
using Unity.XR.XREAL;
using UnityEngine;

namespace AROthello
{
    /// <summary>
    /// XREAL SDK の XREALVideoCapture でグラスの画面を MP4 に録画する (IGlassesRecorder の実機用の実装)。
    /// 試行ごとに「録画の準備 → 開始 → 停止 → 片付け」を行う。音声は録らない (権限の確認を出さないため)。
    /// 合成 (Blend) を頼んでもグラスのカメラを開けないときは、SDK が自動で AR 表示だけ (VirtualOnly) にする。
    /// エディタでは録画できない (IsAvailable = false)。
    /// </summary>
    public class XrealGlassesRecorder : MonoBehaviour, IGlassesRecorder
    {
        private XREALVideoCapture _capture;
        private bool _starting;

        public bool IsAvailable => !Application.isEditor && Application.platform == RuntimePlatform.Android;
        public bool IsRecording => _capture != null && _capture.IsRecording;

        public void StartRecording(string path, RecordingSource source, RecordingQuality quality, Action<bool, RecordingSource, string> onStarted)
        {
            if (!IsAvailable) { onStarted?.Invoke(false, source, "この端末では録画できません"); return; }
            if (_capture != null || _starting) { onStarted?.Invoke(false, source, "前の録画がまだ終わっていません"); return; }

            _starting = true;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                XREALVideoCaptureUtility.CreateAsync(false, capture =>
                {
                    _capture = capture;
                    var p = new CameraParameters(CamMode.VideoMode, ToBlendMode(source))
                    {
                        hologramOpacity = 1f,
                        frameRate = NativeConstants.RECORD_FPS_DEFAULT,
                        cameraResolutionWidth = quality == RecordingQuality.High1080 ? 1920 : 1280,
                        cameraResolutionHeight = quality == RecordingQuality.High1080 ? 1080 : 720,
                        audioState = AudioState.None
                    };
                    capture.StartVideoModeAsync(p, result =>
                    {
                        if (!result.success)
                        {
                            Cleanup();
                            onStarted?.Invoke(false, source, "録画の準備に失敗しました");
                            return;
                        }
                        RecordingSource actual = source;
                        BlenderBase blender = capture.GetContext()?.GetBlender();
                        if (blender != null) actual = FromBlendMode(blender.BlendMode);
                        capture.StartRecordingAsync(path, started =>
                        {
                            _starting = false;
                            if (!started.success)
                            {
                                Cleanup();
                                onStarted?.Invoke(false, source, "録画を始められませんでした");
                                return;
                            }
                            Debug.Log($"[XrealGlassesRecorder] recording {actual} -> {path}");
                            onStarted?.Invoke(true, actual, null);
                        });
                    });
                });
            }
            catch (Exception ex)
            {
                Cleanup();
                Debug.LogWarning($"[XrealGlassesRecorder] start failed: {ex.Message}");
                onStarted?.Invoke(false, source, ex.Message);
            }
        }

        public void StopRecording(Action<bool, string> onStopped)
        {
            if (_capture == null) { onStopped?.Invoke(false, "録画していません"); return; }
            XREALVideoCapture capture = _capture;
            try
            {
                capture.StopRecordingAsync(stopped =>
                {
                    try { capture.StopVideoModeAsync(_ => { }); }
                    catch (Exception ex) { Debug.LogWarning($"[XrealGlassesRecorder] stop video mode: {ex.Message}"); }
                    Cleanup();
                    onStopped?.Invoke(stopped.success, stopped.success ? null : "録画を保存できませんでした");
                });
            }
            catch (Exception ex)
            {
                Cleanup();
                Debug.LogWarning($"[XrealGlassesRecorder] stop failed: {ex.Message}");
                onStopped?.Invoke(false, ex.Message);
            }
        }

        private void Cleanup()
        {
            _starting = false;
            XREALVideoCapture c = _capture;
            _capture = null;
            if (c == null) return;
            try { c.Dispose(); }
            catch (Exception ex) { Debug.LogWarning($"[XrealGlassesRecorder] dispose: {ex.Message}"); }
        }

        private void OnApplicationPause(bool paused)
        {
            // アプリが裏に回ったら録画を閉じて、ファイルが壊れないようにする
            if (paused && IsRecording) StopRecording(null);
        }

        private void OnDestroy()
        {
            if (IsRecording) StopRecording(null);
            else Cleanup();
        }

        private static BlendMode ToBlendMode(RecordingSource s)
        {
            switch (s)
            {
                case RecordingSource.VirtualOnly: return BlendMode.VirtualOnly;
                case RecordingSource.CameraOnly: return BlendMode.CameraOnly;
                default: return BlendMode.Blend;
            }
        }

        private static RecordingSource FromBlendMode(BlendMode m)
        {
            switch (m)
            {
                case BlendMode.VirtualOnly: return RecordingSource.VirtualOnly;
                case BlendMode.CameraOnly: return RecordingSource.CameraOnly;
                default: return RecordingSource.SeenView;
            }
        }
    }
}
