using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AROthello.Experiment
{
    /// <summary>登録した被験者 1 人分。subjects.csv の 1 行。</summary>
    public class SubjectInfo
    {
        public string Id = "";
        public string Name = "";
        public string Age = "";
        public string Gender = "";
        public string DominantEye = "";
        public string VisionCorrection = "";
        public string XrExperience = "";
        public string Note = "";
        public DateTime CreatedAt;
        /// <summary>削除済み (一覧から隠すだけで、記録は残す)</summary>
        public bool Deleted;

        /// <summary>一覧の丸に出す頭文字。</summary>
        public string Initial => string.IsNullOrEmpty(Name) ? (string.IsNullOrEmpty(Id) ? "?" : Id.Substring(0, 1)) : Name.Substring(0, 1);

        public string DisplayName => string.IsNullOrEmpty(Name) ? Id : Name;

        public SubjectInfo Clone() => (SubjectInfo)MemberwiseClone();
    }

    /// <summary>
    /// 被験者の一覧を subjects.csv に保存する。追加した被験者は次回の起動でも一覧に出る。
    /// ID は S01, S02 … と自動で振る。削除は一覧から隠すだけで、行は残す (過去の記録と突き合わせられるように)。
    /// </summary>
    public class SubjectRegistry
    {
        public const string FileName = "subjects.csv";
        public const string Header = "subject_id,name,age,gender,dominant_eye,vision_correction,xr_experience,note,created_at,deleted";

        private readonly List<SubjectInfo> _all = new List<SubjectInfo>();
        private readonly Func<DateTime> _clock;

        public string Path { get; }
        public string LastError { get; private set; }

        /// <summary>削除していない被験者 (登録順)。</summary>
        public List<SubjectInfo> Active => _all.FindAll(s => !s.Deleted);
        public IReadOnlyList<SubjectInfo> All => _all;

        public SubjectRegistry(string path, Func<DateTime> clock = null)
        {
            Path = path;
            _clock = clock ?? (() => DateTime.Now);
            Load();
        }

        public void Load()
        {
            _all.Clear();
            if (string.IsNullOrEmpty(Path) || !File.Exists(Path)) return;
            try
            {
                List<string[]> rows = ExperimentCsv.Parse(File.ReadAllText(Path, Encoding.UTF8));
                if (rows.Count == 0) return;
                Dictionary<string, int> col = ExperimentCsv.IndexHeader(rows[0]);
                for (int i = 1; i < rows.Count; i++)
                {
                    string[] r = rows[i];
                    string id = ExperimentCsv.Field(r, col, "subject_id");
                    if (string.IsNullOrEmpty(id)) continue;
                    DateTime.TryParse(ExperimentCsv.Field(r, col, "created_at"), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime created);
                    _all.Add(new SubjectInfo
                    {
                        Id = id,
                        Name = ExperimentCsv.Field(r, col, "name"),
                        Age = ExperimentCsv.Field(r, col, "age"),
                        Gender = ExperimentCsv.Field(r, col, "gender"),
                        DominantEye = ExperimentCsv.Field(r, col, "dominant_eye"),
                        VisionCorrection = ExperimentCsv.Field(r, col, "vision_correction"),
                        XrExperience = ExperimentCsv.Field(r, col, "xr_experience"),
                        Note = ExperimentCsv.Field(r, col, "note"),
                        CreatedAt = created,
                        Deleted = ExperimentCsv.Field(r, col, "deleted") == "1"
                    });
                }
                LastError = null;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        public SubjectInfo Find(string id) => _all.Find(s => s.Id == id);

        /// <summary>次に振る ID (S01, S02 …)。削除済みも含めて番号を重ねない。</summary>
        public string NextId()
        {
            int max = 0;
            foreach (SubjectInfo s in _all)
            {
                if (s.Id.Length > 1 && (s.Id[0] == 'S' || s.Id[0] == 's') && int.TryParse(s.Id.Substring(1), out int n)) max = Math.Max(max, n);
            }
            return "S" + (max + 1).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>被験者を追加して保存する。名前が空なら追加しない。</summary>
        public SubjectInfo Add(string name)
        {
            name = name?.Trim() ?? "";
            if (name.Length == 0) return null;
            var s = new SubjectInfo { Id = NextId(), Name = name, CreatedAt = _clock() };
            _all.Add(s);
            Save();
            return s;
        }

        /// <summary>既存の ID で登録する (以前の版で手入力した ID を一覧に載せるとき)。</summary>
        public SubjectInfo AddWithId(string id, string name)
        {
            id = id?.Trim() ?? "";
            if (id.Length == 0 || Find(id) != null) return Find(id);
            var s = new SubjectInfo { Id = id, Name = name?.Trim() ?? "", CreatedAt = _clock() };
            _all.Add(s);
            Save();
            return s;
        }

        public bool Rename(string id, string name)
        {
            SubjectInfo s = Find(id);
            name = name?.Trim() ?? "";
            if (s == null || name.Length == 0) return false;
            s.Name = name;
            Save();
            return true;
        }

        /// <summary>名前以外の情報 (年齢など) を書き換える。ID と作成日時は変えない。</summary>
        public bool Update(SubjectInfo info)
        {
            SubjectInfo s = info != null ? Find(info.Id) : null;
            if (s == null) return false;
            if (!string.IsNullOrWhiteSpace(info.Name)) s.Name = info.Name.Trim();
            s.Age = info.Age?.Trim() ?? "";
            s.Gender = info.Gender?.Trim() ?? "";
            s.DominantEye = info.DominantEye?.Trim() ?? "";
            s.VisionCorrection = info.VisionCorrection?.Trim() ?? "";
            s.XrExperience = info.XrExperience?.Trim() ?? "";
            s.Note = info.Note?.Trim() ?? "";
            Save();
            return true;
        }

        /// <summary>一覧から隠す (行と過去の記録は残す)。</summary>
        public bool Delete(string id)
        {
            SubjectInfo s = Find(id);
            if (s == null || s.Deleted) return false;
            s.Deleted = true;
            Save();
            return true;
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(Path)) return;
            var sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            foreach (SubjectInfo s in _all)
            {
                sb.Append(string.Join(",", new[]
                {
                    ExperimentCsv.Escape(s.Id), ExperimentCsv.Escape(s.Name), ExperimentCsv.Escape(s.Age), ExperimentCsv.Escape(s.Gender),
                    ExperimentCsv.Escape(s.DominantEye), ExperimentCsv.Escape(s.VisionCorrection), ExperimentCsv.Escape(s.XrExperience),
                    ExperimentCsv.Escape(s.Note), ExperimentCsv.FormatTime(s.CreatedAt), s.Deleted ? "1" : "0"
                })).Append('\n');
            }
            try
            {
                string dir = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(Path, sb.ToString(), new UTF8Encoding(true));
                LastError = null;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }
    }

    /// <summary>実験者がつける試行の評価。</summary>
    public class TrialEvaluation
    {
        /// <summary>true = 成功, false = 失敗, null = まだ選んでいない</summary>
        public bool? Success;
        /// <summary>失敗の理由 (失敗のときだけ。任意)</summary>
        public string FailReason = "";
        public string Memo = "";
        public DateTime EvaluatedAt;

        public string ResultCode => Success == true ? "success" : Success == false ? "fail" : "";
    }

    /// <summary>直後アンケートの 1 問。</summary>
    [Serializable]
    public class QuestionnaireQuestion
    {
        public string id = "Q1";
        /// <summary>短い名前 (例: 精神的労力)</summary>
        public string title = "";
        /// <summary>問いの文。**で囲んだ部分は強調して出す。</summary>
        public string text = "";
        /// <summary>いちばん小さい値 (既定 1)</summary>
        public int min = 1;
        /// <summary>各段の言葉 (min から順に)。段の数 = labels の数。</summary>
        public string[] labels = new string[0];

        public int Max => min + Math.Max(0, (labels?.Length ?? 0) - 1);

        public string LabelOf(int value)
        {
            int i = value - min;
            return labels != null && i >= 0 && i < labels.Length ? labels[i] : "";
        }
    }

    /// <summary>直後アンケートの定義 (JSON から読む)。</summary>
    [Serializable]
    public class QuestionnaireDefinition
    {
        public QuestionnaireQuestion[] questions = new QuestionnaireQuestion[0];

        public int Count => questions?.Length ?? 0;

        /// <summary>JSON が無いときに使う Q1 (精神的労力, 9 段階)。</summary>
        public static QuestionnaireDefinition Default => new QuestionnaireDefinition
        {
            questions = new[]
            {
                new QuestionnaireQuestion
                {
                    id = "Q1",
                    title = "精神的労力",
                    text = "いま行った作業において、**次に石を置く場所を探して配置するために費やした精神的な労力**はどの程度でしたか？",
                    min = 1,
                    labels = new[] { "極めて低い", "非常に低い", "低い", "やや低い", "どちらでもない（普通）", "やや高い", "高い", "非常に高い", "極めて高い" }
                }
            }
        };

        /// <summary>JSON 文字列から作る。読めない・質問が無いときは null。</summary>
        public static QuestionnaireDefinition FromJson(string json, Func<string, QuestionnaireDefinition> parser)
        {
            if (string.IsNullOrWhiteSpace(json) || parser == null) return null;
            try
            {
                QuestionnaireDefinition d = parser(json);
                if (d == null || d.Count == 0) return null;
                foreach (QuestionnaireQuestion q in d.questions)
                {
                    if (q == null || string.IsNullOrEmpty(q.id) || q.labels == null || q.labels.Length < 2) return null;
                }
                return d;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>アンケートの回答 1 問分。</summary>
    public class QuestionnaireAnswer
    {
        public string QuestionId = "";
        public int Value;
        public string Label = "";
        /// <summary>問いを出してから回答を記録するまでの秒数</summary>
        public double AnswerTimeSec;
        public DateTime AnsweredAt;
    }

    /// <summary>「この人の記録を見る」に出す 1 試行分。</summary>
    public class TrialHistoryEntry
    {
        public string ExperimentId = "";
        public string Condition = "";
        public int TrialNo;
        public string PatternId = "";
        public string TctSec = "";
        public string Errors = "";
        public string CompletedAt = "";
        /// <summary>success / fail / 空 (未評価)</summary>
        public string Result = "";
    }

    /// <summary>
    /// 試行の評価 (evaluations.csv) とアンケート (questionnaire.csv) の書き出し、
    /// および試行回数の数え上げ (trial_summary*.csv) を受け持つ。ExperimentLogger と同じフォルダを使う。
    /// </summary>
    public class ExperimentRecordStore
    {
        public const string EvaluationFileName = "evaluations.csv";
        public const string QuestionnaireFileName = "questionnaire.csv";

        public const string EvaluationHeader =
            "experiment_id,subject_id,subject_name,condition,order,pattern_id,difficulty,step_count,tct_sec,total_errors,override_count," +
            "result,fail_reason,memo,completed_at,evaluated_at";

        public const string QuestionnaireHeader =
            "experiment_id,subject_id,condition,order,pattern_id,question_id,answer,answer_label,answer_time_sec,answered_at";

        private static readonly Encoding Utf8Bom = new UTF8Encoding(true);
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public string Directory { get; }
        public SubjectRegistry Subjects { get; }
        public string LastError { get; private set; }

        public ExperimentRecordStore(string directory, Func<DateTime> clock = null)
        {
            Directory = directory ?? throw new ArgumentNullException(nameof(directory));
            Subjects = new SubjectRegistry(Path.Combine(directory, SubjectRegistry.FileName), clock);
        }

        /// <summary>
        /// その被験者がこれまでに終えた試行の数。trial_summary.csv (と列を足す前の古いファイル) の行を数える。
        /// </summary>
        public int CountTrials(string subjectId)
        {
            return string.IsNullOrEmpty(subjectId) ? 0 : ListTrials(subjectId).Count;
        }

        /// <summary>その被験者の試行の一覧 (古い順)。評価があれば結果も付ける。</summary>
        public List<TrialHistoryEntry> ListTrials(string subjectId)
        {
            var list = new List<TrialHistoryEntry>();
            if (string.IsNullOrEmpty(subjectId) || !System.IO.Directory.Exists(Directory)) return list;

            var results = new Dictionary<string, string>();
            foreach (var (r, col) in ReadRows(Path.Combine(Directory, EvaluationFileName)))
            {
                string id = ExperimentCsv.Field(r, col, "experiment_id");
                if (!string.IsNullOrEmpty(id)) results[id] = ExperimentCsv.Field(r, col, "result");
            }

            string[] files = System.IO.Directory.GetFiles(Directory, "trial_summary*.csv");
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string file in files)
            {
                foreach (var (r, col) in ReadRows(file))
                {
                    if (ExperimentCsv.Field(r, col, "subject_id") != subjectId) continue;
                    int.TryParse(ExperimentCsv.Field(r, col, "order"), NumberStyles.Integer, Inv, out int order);
                    var e = new TrialHistoryEntry
                    {
                        ExperimentId = ExperimentCsv.Field(r, col, "experiment_id"),
                        Condition = ExperimentCsv.Field(r, col, "condition"),
                        TrialNo = order,
                        PatternId = ExperimentCsv.Field(r, col, "pattern_id"),
                        TctSec = ExperimentCsv.Field(r, col, "tct_sec"),
                        Errors = ExperimentCsv.Field(r, col, "total_errors"),
                        CompletedAt = ExperimentCsv.Field(r, col, "completed_at")
                    };
                    if (!string.IsNullOrEmpty(e.ExperimentId) && results.TryGetValue(e.ExperimentId, out string res)) e.Result = res;
                    list.Add(e);
                }
            }
            list.Sort((a, b) => string.CompareOrdinal(a.CompletedAt, b.CompletedAt));
            return list;
        }

        /// <summary>評価を evaluations.csv に 1 行追記する。成否どちらでも書く。</summary>
        public bool WriteEvaluation(TrialMetadata meta, TrialResult trial, TrialEvaluation eval, DateTime completedAt)
        {
            meta = meta ?? new TrialMetadata();
            eval = eval ?? new TrialEvaluation();
            int overrides = 0;
            if (trial?.Steps != null) foreach (StepResult s in trial.Steps) if (s.IsOverride) overrides++;
            InstructionPatternData p = trial?.Pattern;
            string row = string.Join(",", new[]
            {
                ExperimentCsv.Escape(meta.experimentId),
                ExperimentCsv.Escape(meta.subjectId),
                ExperimentCsv.Escape(meta.subjectName),
                ExperimentCsv.Escape(meta.condition),
                meta.order.ToString(Inv),
                ExperimentCsv.Escape(p?.patternId),
                (p?.difficulty ?? 0).ToString(Inv),
                (p?.stepCount ?? 0).ToString(Inv),
                ExperimentCsv.FormatNumber(trial?.TotalTimeSec ?? 0),
                (trial?.TotalErrorCount ?? 0).ToString(Inv),
                overrides.ToString(Inv),
                eval.ResultCode,
                ExperimentCsv.Escape(eval.Success == false ? eval.FailReason : ""),
                ExperimentCsv.Escape(eval.Memo),
                ExperimentCsv.FormatTime(completedAt),
                ExperimentCsv.FormatTime(eval.EvaluatedAt)
            });
            return Append(EvaluationFileName, EvaluationHeader, row);
        }

        /// <summary>アンケートの回答を questionnaire.csv に 1 行追記する。</summary>
        public bool WriteAnswer(TrialMetadata meta, InstructionPatternData pattern, QuestionnaireAnswer answer)
        {
            meta = meta ?? new TrialMetadata();
            if (answer == null) return false;
            string row = string.Join(",", new[]
            {
                ExperimentCsv.Escape(meta.experimentId),
                ExperimentCsv.Escape(meta.subjectId),
                ExperimentCsv.Escape(meta.condition),
                meta.order.ToString(Inv),
                ExperimentCsv.Escape(pattern?.patternId),
                ExperimentCsv.Escape(answer.QuestionId),
                answer.Value.ToString(Inv),
                ExperimentCsv.Escape(answer.Label),
                ExperimentCsv.FormatNumber(answer.AnswerTimeSec),
                ExperimentCsv.FormatTime(answer.AnsweredAt)
            });
            return Append(QuestionnaireFileName, QuestionnaireHeader, row);
        }

        private bool Append(string fileName, string header, string row)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                string path = Path.Combine(Directory, fileName);
                FileTrialLogSink.RotateIfHeaderChanged(path, header);
                if (!File.Exists(path)) File.WriteAllText(path, header + "\n", Utf8Bom);
                File.AppendAllText(path, row + "\n", Utf8Bom);
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        private static IEnumerable<(string[] row, Dictionary<string, int> col)> ReadRows(string path)
        {
            if (!File.Exists(path)) yield break;
            List<string[]> rows;
            try { rows = ExperimentCsv.Parse(File.ReadAllText(path, Encoding.UTF8)); }
            catch { yield break; }
            if (rows.Count == 0) yield break;
            Dictionary<string, int> col = ExperimentCsv.IndexHeader(rows[0]);
            for (int i = 1; i < rows.Count; i++) yield return (rows[i], col);
        }
    }
}
