using AROthello;
using AROthello.Experiment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 視覚的探索負荷の被験者実験システムを、開いているシーン (OthelloAutoTrackerScene) に追加・配線する。
/// ビルド時は CreateOthelloAutoTrackerScene.GenerateScene からも呼ばれるので、シーンを作り直しても消えない。
/// 何度実行しても同じ構成になる (既存の Experiment_System は再利用)。
/// </summary>
public static class ExperimentSceneSetup
{
    public const string RootName = "Experiment_System";
    public const string ProgressTextName = "Text_ExperimentProgress";

    [MenuItem("XREAL/Add Experiment System To Scene")]
    public static void AddFromMenu()
    {
        if (AddToActiveScene())
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("<color=#00FF88>[ExperimentSceneSetup] Experiment system added. Save the scene to keep it.</color>");
        }
    }

    /// <summary>実験システムを追加する。必要な既存オブジェクトが無ければ false。</summary>
    public static bool AddToActiveScene()
    {
        GameObject board = GameObject.Find("OthelloBoard_AR_Target");
        OthelloAutoTracker tracker = Object.FindFirstObjectByType<OthelloAutoTracker>();
        if (board == null || tracker == null)
        {
            Debug.LogError("[ExperimentSceneSetup] OthelloBoard_AR_Target / OthelloAutoTracker not found. Run the Othello Auto Tracker setup first.");
            return false;
        }
        GameObject glassesCanvas = GameObject.Find("Canvas_AR_Debug");
        GameObject handheldCanvas = GameObject.Find("Canvas_BeamPro_Handheld");

        GameObject root = GameObject.Find(RootName);
        if (root == null)
        {
            root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create " + RootName);
        }

        InstructionLoader loader = GetOrAdd<InstructionLoader>(root);
        OthelloTrackerBoardStateProvider trackerProvider = GetOrAdd<OthelloTrackerBoardStateProvider>(root);
        MockBoardStateProvider mock = GetOrAdd<MockBoardStateProvider>(root);
        ExperimentController controller = GetOrAdd<ExperimentController>(root);
        ExperimentLogger logger = GetOrAdd<ExperimentLogger>(root);
        ExperimentGuideView guide = GetOrAdd<ExperimentGuideView>(root);
        CalibrationDotsView dots = GetOrAdd<CalibrationDotsView>(root);
        OthelloTrackerOffsetAdjuster adjuster = GetOrAdd<OthelloTrackerOffsetAdjuster>(root);
        ExperimentOperatorPanel panel = GetOrAdd<ExperimentOperatorPanel>(root);

        var material = Resources.Load<Material>(ExperimentGuideView.DefaultMaterialResource);
        Text progressText = glassesCanvas != null ? GetOrCreateProgressText(glassesCanvas.transform) : null;

        Set(trackerProvider, "tracker", tracker);
        Set(adjuster, "tracker", tracker);
        Set(controller, "instructionLoader", loader);
        Set(controller, "boardProvider", trackerProvider);
        Set(controller, "editorBoardProvider", mock);
        Set(logger, "controller", controller);
        Set(guide, "controller", controller);
        Set(guide, "boardRoot", board.transform);
        Set(guide, "progressText", progressText);
        Set(guide, "markerMaterial", material);
        Set(dots, "boardRoot", board.transform);
        Set(dots, "dotMaterial", material);
        Set(panel, "controller", controller);
        Set(panel, "logger", logger);
        Set(panel, "calibrationDots", dots);
        Set(panel, "offsetAdjuster", adjuster);
        Set(panel, "mockProvider", mock);
        Set(panel, "targetCanvas", handheldCanvas != null ? handheldCanvas.GetComponent<Canvas>() : null);

        return true;
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : Undo.AddComponent<T>(go);
    }

    private static void Set(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);
        if (p == null)
        {
            Debug.LogError($"[ExperimentSceneSetup] {target.GetType().Name}.{field} not found");
            return;
        }
        p.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>グラス側 HUD の右上に小さな進捗表示を置く。</summary>
    private static Text GetOrCreateProgressText(Transform canvas)
    {
        Transform existing = canvas.Find(ProgressTextName);
        GameObject go = existing != null ? existing.gameObject : new GameObject(ProgressTextName, typeof(RectTransform), typeof(Text));
        if (existing == null)
        {
            go.transform.SetParent(canvas, false);
            Undo.RegisterCreatedObjectUndo(go, "Create " + ProgressTextName);
        }

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.80f, 0.90f);
        rt.anchorMax = new Vector2(0.98f, 0.98f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Text t = go.GetComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 28;
        t.alignment = TextAnchor.UpperRight;
        t.color = new Color(0.85f, 0.9f, 1f, 0.75f);
        t.raycastTarget = false;
        t.text = "";
        return t;
    }
}
