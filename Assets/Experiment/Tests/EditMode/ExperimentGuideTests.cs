using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace AROthello.Experiment.Tests
{
    /// <summary>Phase 3: 次工程ガイド (表示内容の組み立てと AR 盤上への描画)。</summary>
    public class ExperimentGuideTests
    {
        private const double Dt = 0.1;

        private ExperimentStateMachine _sm;
        private BoardState _board;
        private ExperimentGuidePresenter _presenter;
        private readonly List<Object> _created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _sm = new ExperimentStateMachine();
            _board = new BoardState();
            _presenter = new ExperimentGuidePresenter(_sm);
        }

        [TearDown]
        public void TearDown()
        {
            _presenter.Dispose();
            foreach (Object o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        // ---------------- ヘルパー ----------------

        private static InstructionPatternData Pattern(params InstructionStone[] initialStones)
        {
            return new InstructionPatternData
            {
                patternId = "GUIDE",
                difficulty = initialStones.Length > 0 ? 3 : 1,
                stepCount = 3,
                initialStones = initialStones,
                steps = new[]
                {
                    new InstructionStep { step = 1, col = 3, row = 2, coord = "D3", color = "B" },
                    new InstructionStep { step = 2, col = 2, row = 3, coord = "C4", color = "W" },
                    new InstructionStep { step = 3, col = 5, row = 4, coord = "F5", color = "B" }
                }
            };
        }

        private void Feed(double from, double to)
        {
            int n = (int)System.Math.Round((to - from) / Dt);
            for (int i = 0; i <= n; i++) _sm.ProcessBoard(_board, from + i * Dt);
        }

        private void StartRunning(InstructionPatternData p)
        {
            Assert.IsTrue(_sm.LoadPattern(p));
            _sm.BeginCalibration();
            _sm.CompleteCalibration(0);
            foreach (InstructionStone s in p.initialStones)
            {
                BoardCoord.TryParseColor(s.color, out StoneColor c);
                _board.Set(s.col, s.row, c);
            }
            Feed(0, 0.4);
            Assert.AreEqual(ExperimentState.Running, _sm.State);
        }

        private void Place(int col, int row, StoneColor c, double t)
        {
            _board.Set(col, row, c);
            Feed(t, t + 0.4);
        }

        private T Track<T>(T obj) where T : Object
        {
            _created.Add(obj);
            return obj;
        }

        private (ExperimentGuideView view, Transform board, Text text) CreateView()
        {
            var board = Track(new GameObject("Board")).transform;
            board.localScale = new Vector3(0.228f, 0.018f, 0.228f);
            var textGo = Track(new GameObject("ProgressText"));
            Text text = textGo.AddComponent<Text>();
            ExperimentGuideView view = Track(new GameObject("GuideView")).AddComponent<ExperimentGuideView>();
            view.BoardRoot = board;
            view.ProgressText = text;
            return (view, board, text);
        }

        private static GuideMarker Single(GuideViewState v, GuideMarkerKind kind) => v.Markers.Single(m => m.Kind == kind);

        // ---------------- 表示内容 (Presenter) ----------------

        [Test]
        public void Presenter_Idle_ShowsNothing()
        {
            Assert.AreEqual(ExperimentState.Idle, _presenter.View.State);
            Assert.AreEqual(0, _presenter.View.Markers.Count);
            Assert.AreEqual("", _presenter.View.ProgressText);
        }

        [Test]
        public void Presenter_Running_ShowsOnlyCurrentTargetAndProgress()
        {
            StartRunning(Pattern());

            GuideViewState v = _presenter.View;
            Assert.AreEqual(1, v.Markers.Count, "low clutter: exactly one guide");
            GuideMarker t = Single(v, GuideMarkerKind.Target);
            Assert.AreEqual(3, t.Col);
            Assert.AreEqual(2, t.Row);
            Assert.AreEqual(StoneColor.Black, t.Color);
            Assert.AreEqual("1 / 3", v.ProgressText);

            Place(3, 2, StoneColor.Black, 1.0);
            t = Single(_presenter.View, GuideMarkerKind.Target);
            Assert.AreEqual(2, t.Col);
            Assert.AreEqual(3, t.Row);
            Assert.AreEqual(StoneColor.White, t.Color);
            Assert.AreEqual("2 / 3", _presenter.View.ProgressText);
        }

        [Test]
        public void Presenter_Error_ShowsErrorCellsUntilCleared()
        {
            StartRunning(Pattern());

            Place(0, 0, StoneColor.White, 1.0); // 指定外
            GuideViewState v = _presenter.View;
            Assert.IsTrue(v.IsErrorActive);
            GuideMarker e = Single(v, GuideMarkerKind.Error);
            Assert.AreEqual(0, e.Col);
            Assert.AreEqual(0, e.Row);
            Assert.IsTrue(v.HasTarget, "target guide stays visible during an error");
            Assert.AreEqual("1 / 3", v.ProgressText);

            _board.Set(0, 0, StoneColor.None);
            Feed(2.0, 2.0);
            Assert.IsFalse(_presenter.View.IsErrorActive);
            Assert.AreEqual(0, _presenter.View.Markers.Count(m => m.Kind == GuideMarkerKind.Error));
        }

        [Test]
        public void Presenter_WrongColor_MarksTargetCellAsError()
        {
            StartRunning(Pattern());
            Place(3, 2, StoneColor.White, 1.0);

            GuideMarker e = Single(_presenter.View, GuideMarkerKind.Error);
            Assert.AreEqual(3, e.Col);
            Assert.AreEqual(2, e.Row);
        }

        [Test]
        public void Presenter_Completed_ShowsCompletionOnly()
        {
            StartRunning(Pattern());
            Place(3, 2, StoneColor.Black, 1.0);
            Place(2, 3, StoneColor.White, 2.0);
            Place(5, 4, StoneColor.Black, 3.0);

            GuideViewState v = _presenter.View;
            Assert.AreEqual(ExperimentState.Completed, v.State);
            Assert.IsTrue(v.IsCompleted);
            Assert.AreEqual(0, v.Markers.Count);
            Assert.AreEqual("完了", v.ProgressText);
        }

        [Test]
        public void Presenter_InitialCheck_ShowsStonesToPlaceAndRemove()
        {
            Assert.IsTrue(_sm.LoadPattern(Pattern(
                new InstructionStone { col = 0, row = 0, coord = "A1", color = "W" },
                new InstructionStone { col = 7, row = 7, coord = "H8", color = "B" })));
            _sm.BeginCalibration();
            _sm.CompleteCalibration(0);

            _board.Set(4, 4, StoneColor.Black); // 余計な石
            Feed(0, 0);

            GuideViewState v = _presenter.View;
            Assert.AreEqual(ExperimentState.InitialBoardCheck, v.State);
            Assert.AreEqual(3, v.Markers.Count);
            Assert.AreEqual(2, v.Markers.Count(m => m.Kind == GuideMarkerKind.SetupPlace));
            Assert.AreEqual(StoneColor.White, v.Markers.Single(m => m.Col == 0 && m.Row == 0).Color);
            Assert.AreEqual(StoneColor.Black, v.Markers.Single(m => m.Col == 7 && m.Row == 7).Color);
            Assert.AreEqual(GuideMarkerKind.SetupRemove, v.Markers.Single(m => m.Col == 4 && m.Row == 4).Kind);
            Assert.AreEqual("初期配置 あと 3", v.ProgressText);

            _board.Set(4, 4, StoneColor.None);
            _board.Set(0, 0, StoneColor.White);
            _board.Set(7, 7, StoneColor.Black);
            Feed(0.1, 0.1);
            Assert.AreEqual(0, _presenter.View.Markers.Count);
            Assert.AreEqual("初期配置 OK", _presenter.View.ProgressText);

            Feed(0.2, 0.5);
            Assert.AreEqual(ExperimentState.Running, _presenter.View.State);
            Assert.AreEqual(1, _presenter.View.Markers.Count, "setup markers are cleared when the trial starts");
        }

        [Test]
        public void Presenter_Reset_ClearsEverything()
        {
            StartRunning(Pattern());
            Place(0, 0, StoneColor.White, 1.0);
            _sm.ResetTrial();

            Assert.AreEqual(ExperimentState.Idle, _presenter.View.State);
            Assert.AreEqual(0, _presenter.View.Markers.Count);
            Assert.AreEqual("", _presenter.View.ProgressText);
            Assert.IsFalse(_presenter.View.IsErrorActive);
        }

        [Test]
        public void Presenter_NotifiesChanges_AndStopsAfterDispose()
        {
            int count = 0;
            _presenter.Changed += v => count++;
            StartRunning(Pattern());
            Assert.Greater(count, 0);

            _presenter.Dispose();
            int before = count;
            Place(3, 2, StoneColor.Black, 1.0);
            Assert.AreEqual(before, count);
        }

        // ---------------- 描画 (View) ----------------

        [Test]
        public void View_RendersMarkersAtCellCentersUnderBoard()
        {
            var (view, board, text) = CreateView();
            var state = new GuideViewState { ProgressText = "2 / 10" };
            state.Markers.Add(new GuideMarker { Col = 0, Row = 0, Kind = GuideMarkerKind.Target, Color = StoneColor.White });
            state.Markers.Add(new GuideMarker { Col = 7, Row = 7, Kind = GuideMarkerKind.Error });

            view.Render(state);

            Assert.AreEqual(2, view.ActiveMarkerCount);
            Assert.AreEqual("2 / 10", text.text);
            Assert.IsTrue(text.enabled);

            Transform container = board.Find("ExperimentGuide_Container");
            Assert.IsNotNull(container, "markers are children of the board so they follow AR tracking");
            Transform target = container.Cast<Transform>().First(t => t.name.StartsWith("Guide_Target_A1"));
            Vector3 expected = BoardGeometry.CellCenterLocal(0, 0);
            Assert.AreEqual(expected.x, target.localPosition.x, 1e-5);
            Assert.AreEqual(expected.z, target.localPosition.z, 1e-5);
            Assert.Greater(target.localPosition.y, BoardGeometry.TopSurfaceY, "slightly above the top surface");
            Assert.IsNotNull(container.Cast<Transform>().FirstOrDefault(t => t.name == "Guide_Error_H8"));
        }

        [Test]
        public void View_RingAndDotStyle_UsesStoneColor()
        {
            var (view, board, _) = CreateView();
            var state = new GuideViewState();
            state.Markers.Add(new GuideMarker { Col = 3, Row = 2, Kind = GuideMarkerKind.Target, Color = StoneColor.Black });
            view.Render(state);

            Transform marker = board.Find("ExperimentGuide_Container").Cast<Transform>().First(t => t.gameObject.activeSelf);
            MeshRenderer ring = marker.Find("Ring").GetComponent<MeshRenderer>();
            MeshRenderer center = marker.Find("Center").GetComponent<MeshRenderer>();
            Assert.IsTrue(ring.enabled);
            Assert.IsTrue(center.enabled);

            var mpb = new MaterialPropertyBlock();
            ring.GetPropertyBlock(mpb);
            Assert.AreEqual(view.BlackStoneColor, mpb.GetColor("_BaseColor"));
        }

        [Test]
        public void View_GhostStoneStyle_ShowsDiscOnly()
        {
            var (view, board, _) = CreateView();
            view.Style = ExperimentGuideView.TargetStyle.GhostStone;
            var state = new GuideViewState();
            state.Markers.Add(new GuideMarker { Col = 3, Row = 2, Kind = GuideMarkerKind.Target, Color = StoneColor.White });
            view.Render(state);

            Transform marker = board.Find("ExperimentGuide_Container").Cast<Transform>().First(t => t.gameObject.activeSelf);
            Assert.IsFalse(marker.Find("Ring").GetComponent<MeshRenderer>().enabled);
            Assert.IsTrue(marker.Find("Center").GetComponent<MeshRenderer>().enabled);
        }

        [Test]
        public void View_ErrorMarkerBlinks_TargetDoesNot()
        {
            var (view, board, _) = CreateView();
            var state = new GuideViewState();
            state.Markers.Add(new GuideMarker { Col = 3, Row = 2, Kind = GuideMarkerKind.Target, Color = StoneColor.Black });
            state.Markers.Add(new GuideMarker { Col = 0, Row = 0, Kind = GuideMarkerKind.Error });
            view.Render(state);

            Transform container = board.Find("ExperimentGuide_Container");
            MeshRenderer errorRing = container.Find("Guide_Error_A1/Ring").GetComponent<MeshRenderer>();
            MeshRenderer targetRing = container.Find("Guide_Target_D3/Ring").GetComponent<MeshRenderer>();

            view.SetBlinkPhase(false);
            Assert.IsFalse(errorRing.enabled);
            Assert.IsTrue(targetRing.enabled);
            view.SetBlinkPhase(true);
            Assert.IsTrue(errorRing.enabled);
        }

        [Test]
        public void View_FewerMarkers_HidesUnusedOnes_AndReusesObjects()
        {
            var (view, board, _) = CreateView();
            var many = new GuideViewState();
            for (int i = 0; i < 5; i++) many.Markers.Add(new GuideMarker { Col = i, Row = 0, Kind = GuideMarkerKind.Error });
            view.Render(many);
            Assert.AreEqual(5, view.ActiveMarkerCount);
            int childCount = board.Find("ExperimentGuide_Container").childCount;

            var one = new GuideViewState();
            one.Markers.Add(new GuideMarker { Col = 1, Row = 1, Kind = GuideMarkerKind.Target, Color = StoneColor.White });
            view.Render(one);
            Assert.AreEqual(1, view.ActiveMarkerCount);
            Assert.AreEqual(childCount, board.Find("ExperimentGuide_Container").childCount, "objects are pooled");
        }

        [Test]
        public void View_OutOfRangeMarkers_AreIgnoredSafely()
        {
            var (view, _, _) = CreateView();
            var state = new GuideViewState();
            state.Markers.Add(new GuideMarker { Col = 8, Row = 0, Kind = GuideMarkerKind.Target, Color = StoneColor.Black });
            state.Markers.Add(new GuideMarker { Col = 0, Row = -1, Kind = GuideMarkerKind.Error });
            state.Markers.Add(new GuideMarker { Col = int.MaxValue, Row = int.MinValue, Kind = GuideMarkerKind.SetupPlace });
            state.Markers.Add(new GuideMarker { Col = 2, Row = 2, Kind = GuideMarkerKind.SetupRemove });

            Assert.DoesNotThrow(() => view.Render(state));
            Assert.AreEqual(1, view.ActiveMarkerCount);
        }

        [Test]
        public void View_NullStateAndMissingReferences_AreSafe()
        {
            ExperimentGuideView bare = Track(new GameObject("BareView")).AddComponent<ExperimentGuideView>();
            var state = new GuideViewState { ProgressText = "1 / 5" };
            state.Markers.Add(new GuideMarker { Col = 1, Row = 1, Kind = GuideMarkerKind.Target, Color = StoneColor.White });

            Assert.DoesNotThrow(() => bare.Render(state), "no board root, no text");
            Assert.DoesNotThrow(() => bare.Render(null));
            Assert.DoesNotThrow(() => bare.SetBlinkPhase(false));
            Assert.DoesNotThrow(() => bare.HideBoardChildren());
            Assert.DoesNotThrow(() => bare.RestoreBoardChildren());
            Assert.DoesNotThrow(() => bare.Bind(null));
            Assert.AreEqual(0, bare.ActiveMarkerCount);

            var (view, _, text) = CreateView();
            view.Render(state);
            view.Render(null);
            Assert.AreEqual(0, view.ActiveMarkerCount);
            Assert.AreEqual("", text.text);
            Assert.IsFalse(text.enabled, "empty text is hidden");
        }

        [Test]
        public void View_HidesAndRestoresExistingBoardVisuals()
        {
            var (view, board, _) = CreateView();
            var highlights = new GameObject("Highlights_Container");
            highlights.transform.SetParent(board, false);
            var cells = new GameObject("Cells_Container");
            cells.transform.SetParent(board, false);

            view.HideBoardChildren();
            Assert.IsFalse(highlights.activeSelf, "legal-move highlights would clutter the experiment");
            Assert.IsTrue(cells.activeSelf, "only the configured children are hidden");

            view.RestoreBoardChildren();
            Assert.IsTrue(highlights.activeSelf);
        }

        [Test]
        public void View_BoundToStateMachine_FollowsTrial()
        {
            var (view, board, text) = CreateView();
            view.Bind(_sm);
            Assert.AreEqual(0, view.ActiveMarkerCount);

            StartRunning(Pattern());
            Assert.AreEqual(1, view.ActiveMarkerCount);
            Assert.AreEqual("1 / 3", text.text);
            Assert.IsNotNull(board.Find("ExperimentGuide_Container/Guide_Target_D3"));

            Place(3, 2, StoneColor.Black, 1.0);
            Assert.AreEqual("2 / 3", text.text);
            Assert.IsNotNull(board.Find("ExperimentGuide_Container/Guide_Target_C4"));

            view.Unbind();
            Place(2, 3, StoneColor.White, 2.0);
            Assert.AreEqual("2 / 3", text.text, "no updates after unbind");
        }

        // ---------------- 盤面ジオメトリ ----------------

        [Test]
        public void Geometry_CellCentersMatchExistingBoardLayout()
        {
            Vector3 a1 = BoardGeometry.CellCenterLocal(0, 0);
            Vector3 h8 = BoardGeometry.CellCenterLocal(7, 7);
            Assert.AreEqual(-0.4375f, a1.x, 1e-6);
            Assert.AreEqual(0.4375f, a1.z, 1e-6, "row 0 is the far side (+Z)");
            Assert.AreEqual(0.5f, a1.y, 1e-6);
            Assert.AreEqual(0.4375f, h8.x, 1e-6);
            Assert.AreEqual(-0.4375f, h8.z, 1e-6);
            Assert.AreEqual(0.6f, BoardGeometry.CellCenterLocal(3, 3, 0.1f).y, 1e-6);
        }

        [Test]
        public void Geometry_RingAndDiscMeshesAreFlatAndWithinRadius()
        {
            Mesh ring = Track(BoardGeometry.BuildRingMesh(0.03f, 0.05f, 32));
            Assert.AreEqual(66, ring.vertexCount);
            Assert.AreEqual(32 * 6, ring.triangles.Length);
            foreach (Vector3 v in ring.vertices)
            {
                Assert.AreEqual(0f, v.y, 1e-6);
                float r = new Vector2(v.x, v.z).magnitude;
                Assert.IsTrue(r >= 0.03f - 1e-5 && r <= 0.05f + 1e-5, $"radius {r}");
            }
            Assert.IsTrue(ring.normals.All(n => n == Vector3.up));

            Mesh disc = Track(BoardGeometry.BuildRingMesh(0f, 0.04f, 16));
            Assert.AreEqual(18, disc.vertexCount);
            Assert.AreEqual(16 * 3, disc.triangles.Length);

            Mesh degenerate = Track(BoardGeometry.BuildRingMesh(0.05f, 0.01f, 1));
            Assert.Greater(degenerate.vertexCount, 0, "invalid parameters are clamped");
        }
    }
}
