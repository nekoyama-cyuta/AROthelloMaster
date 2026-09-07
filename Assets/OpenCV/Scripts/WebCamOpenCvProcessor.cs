using System;
using UnityEngine;
using UnityEngine.Events;
using OpenCvSharp;

namespace OpenCVForUnityCustom
{
    [System.Serializable]
    public class MatProcessEvent : UnityEvent<Mat, Mat> { }

    /// <summary>
    /// Webカメラの映像を継続的にキャプチャし、OpenCV (Mat) を使ったリアルタイム画像処理フレームワークを提供するコンポーネント
    /// </summary>
    public class WebCamOpenCvProcessor : MonoBehaviour
    {
        [Header("Camera Settings")]
        [SerializeField] private int requestedWidth = 640;
        [SerializeField] private int requestedHeight = 480;
        [SerializeField] private int requestedFPS = 30;
        [SerializeField] private int deviceIndex = 0;

        [Header("Events")]
        /// <summary>
        /// フレーム毎に呼び出される画像処理コールバック (引数1: 入力Mat, 引数2: 出力Mat)
        /// </summary>
        public MatProcessEvent onProcessFrame;

        private WebCamTexture _webCamTexture;
        private Texture2D _displayTexture;
        private Mat _inputMat;
        private Mat _outputMat;

        public Texture2D OutputTexture => _displayTexture;
        public bool IsRunning => _webCamTexture != null && _webCamTexture.isPlaying;

        private void Start()
        {
            OpenCvUtils.InitializeNativeDll();
        }

        public void StartCapture()
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices == null || devices.Length == 0)
            {
                Debug.LogWarning("[WebCamOpenCvProcessor] No camera device found.");
                return;
            }

            int index = Mathf.Clamp(deviceIndex, 0, devices.Length - 1);
            _webCamTexture = new WebCamTexture(devices[index].name, requestedWidth, requestedHeight, requestedFPS);
            _webCamTexture.Play();

            _inputMat = new Mat();
            _outputMat = new Mat();
            _displayTexture = new Texture2D(requestedWidth, requestedHeight, TextureFormat.RGBA32, false);
        }

        public void StopCapture()
        {
            if (_webCamTexture != null && _webCamTexture.isPlaying)
            {
                _webCamTexture.Stop();
            }
            DisposeMats();
        }

        private void Update()
        {
            if (_webCamTexture == null || !_webCamTexture.isPlaying || !_webCamTexture.didUpdateThisFrame)
                return;

            OpenCvUtils.WebCamTextureToMat(_webCamTexture, _inputMat);

            if (onProcessFrame != null)
            {
                onProcessFrame.Invoke(_inputMat, _outputMat);
                if (!_outputMat.Empty())
                {
                    OpenCvUtils.MatToTexture2D(_outputMat, _displayTexture);
                }
            }
            else
            {
                OpenCvUtils.MatToTexture2D(_inputMat, _displayTexture);
            }
        }

        private void DisposeMats()
        {
            if (_inputMat != null && !_inputMat.IsDisposed)
            {
                _inputMat.Dispose();
                _inputMat = null;
            }
            if (_outputMat != null && !_outputMat.IsDisposed)
            {
                _outputMat.Dispose();
                _outputMat = null;
            }
        }

        private void OnDestroy()
        {
            StopCapture();
        }
    }
}
