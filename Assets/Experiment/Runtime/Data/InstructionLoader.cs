using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace AROthello.Experiment
{
    /// <summary>
    /// StreamingAssets から作業指示書 JSON を読み込み、InstructionLibrary として提供する。
    /// Android では StreamingAssets が APK 内にありディレクトリ列挙ができないため、
    /// 読み込むファイルは jsonFiles に StreamingAssets からの相対パスで列挙する。
    /// </summary>
    public class InstructionLoader : MonoBehaviour
    {
        [Tooltip("StreamingAssets からの相対パス。単一パターン JSON とパック JSON のどちらでもよい。")]
        [SerializeField] private string[] jsonFiles = { "Instructions/sample_pack.json" };

        [Tooltip("Start 時に自動で読み込む")]
        [SerializeField] private bool loadOnStart = true;

        [Tooltip("読み込み後に選択する patternId (空なら最初のパターン)")]
        [SerializeField] private string initialPatternId = "";

        private readonly InstructionLibrary _library = new InstructionLibrary();

        public InstructionLibrary Library => _library;
        public bool IsLoaded { get; private set; }

        /// <summary>全ファイルの読み込みが終わったときに呼ばれる。</summary>
        public event Action<InstructionLibrary> Loaded;

        public string[] JsonFiles
        {
            get => jsonFiles;
            set => jsonFiles = value;
        }

        private void Start()
        {
            if (loadOnStart)
            {
                StartCoroutine(LoadAllCoroutine());
            }
        }

        /// <summary>jsonFiles を (再) 読み込みする。</summary>
        public void Reload()
        {
            StopAllCoroutines();
            StartCoroutine(LoadAllCoroutine());
        }

        private IEnumerator LoadAllCoroutine()
        {
            IsLoaded = false;
            _library.Clear();

            if (jsonFiles != null)
            {
                foreach (string relPath in jsonFiles)
                {
                    if (string.IsNullOrWhiteSpace(relPath)) continue;
                    string fullPath = Path.Combine(Application.streamingAssetsPath, relPath);
                    string text = null;
                    string error = null;

                    if (fullPath.Contains("://") || fullPath.Contains(":///"))
                    {
                        // Android (jar:file://...) など、ファイル API で読めない場所
                        using (UnityWebRequest req = UnityWebRequest.Get(fullPath))
                        {
                            yield return req.SendWebRequest();
                            if (req.result == UnityWebRequest.Result.Success) text = req.downloadHandler.text;
                            else error = req.error;
                        }
                    }
                    else
                    {
                        try
                        {
                            text = File.ReadAllText(fullPath);
                        }
                        catch (Exception ex)
                        {
                            error = ex.Message;
                        }
                    }

                    if (error != null)
                    {
                        string msg = $"{relPath}: failed to read ({error})";
                        _library.Add(new InstructionParseResult { SourceName = relPath, Errors = { msg } });
                        continue;
                    }

                    _library.AddJson(text, relPath);
                }
            }

            if (!string.IsNullOrEmpty(initialPatternId) && !_library.Select(initialPatternId))
            {
                Debug.LogWarning($"[InstructionLoader] initialPatternId \"{initialPatternId}\" was not found.");
            }

            foreach (string e in _library.Errors)
            {
                Debug.LogWarning($"[InstructionLoader] {e}");
            }
            Debug.Log($"[InstructionLoader] Loaded {_library.Count} pattern(s). Current: {_library.Current}");

            IsLoaded = true;
            Loaded?.Invoke(_library);
        }
    }
}
