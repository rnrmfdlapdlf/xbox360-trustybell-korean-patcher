using System;
using System.IO;

namespace TrystybellKoreanPatcher.Patching
{
    public enum PatchStage
    {
        ValidateInput = 0,
        PrepareWorkspace = 1,
        ExtractIso = 2,
        PatchExecutableText = 3,
        PatchBmdTables = 4,
        PatchResourceText = 5,
        PatchFonts = 6,
        PatchImages = 7,
        RepackIso = 8,
        Complete = 9
    }

    public enum PatchStageState
    {
        Waiting,
        Running,
        Complete,
        Blocked
    }

    public enum PatchStepStatus
    {
        Complete,
        PendingImplementation
    }

    public sealed class PatchJobOptions
    {
        public string InputIsoPath { get; set; }
        public string OutputIsoPath { get; set; }
        public string WorkRoot { get; set; }
        public bool KeepWorkspace { get; set; }
    }

    public sealed class PatchProgressInfo
    {
        public PatchProgressInfo(PatchStage stage, PatchStageState state, int percent, string message)
        {
            Stage = stage;
            State = state;
            Percent = percent;
            Message = message;
        }

        public PatchStage Stage { get; private set; }
        public PatchStageState State { get; private set; }
        public int Percent { get; private set; }
        public string Message { get; private set; }
    }

    public sealed class PatchStepResult
    {
        private PatchStepResult(PatchStepStatus status, string message)
        {
            Status = status;
            Message = message;
        }

        public PatchStepStatus Status { get; private set; }
        public string Message { get; private set; }

        public static PatchStepResult Complete(string message)
        {
            return new PatchStepResult(PatchStepStatus.Complete, message);
        }

        public static PatchStepResult PendingImplementation(string message)
        {
            return new PatchStepResult(PatchStepStatus.PendingImplementation, message);
        }
    }

    public sealed class PatchResult
    {
        private PatchResult(bool completed, bool requiresImplementation, PatchStage lastStage, string outputIsoPath, string message)
        {
            Completed = completed;
            RequiresImplementation = requiresImplementation;
            LastStage = lastStage;
            OutputIsoPath = outputIsoPath;
            Message = message;
        }

        public bool Completed { get; private set; }
        public bool RequiresImplementation { get; private set; }
        public PatchStage LastStage { get; private set; }
        public string OutputIsoPath { get; private set; }
        public string Message { get; private set; }

        public static PatchResult Complete(string outputIsoPath)
        {
            return new PatchResult(true, false, PatchStage.Complete, outputIsoPath, "패치가 완료되었습니다.");
        }

        public static PatchResult Pending(PatchStage stage, string outputIsoPath, string message)
        {
            return new PatchResult(false, true, stage, outputIsoPath, message);
        }
    }

    public interface ITrustyBellPatchStep
    {
        PatchStage Stage { get; }
        string DisplayName { get; }
        PatchStepResult Run(PatchContext context);
    }

    public sealed class PatchContext
    {
        private readonly Action<PatchProgressInfo> report;

        public PatchContext(PatchJobOptions options, Action<PatchProgressInfo> report)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            Options = options;
            this.report = report;
        }

        public PatchJobOptions Options { get; private set; }

        public void Report(PatchStage stage, PatchStageState state, int percent, string message)
        {
            if (report != null)
            {
                report(new PatchProgressInfo(stage, state, ClampPercent(percent), message));
            }
        }

        private static int ClampPercent(int percent)
        {
            if (percent < 0)
            {
                return 0;
            }

            if (percent > 100)
            {
                return 100;
            }

            return percent;
        }
    }

    public static class PatchStageCatalog
    {
        private static readonly PatchStage[] Stages = new PatchStage[]
        {
            PatchStage.ValidateInput,
            PatchStage.PrepareWorkspace,
            PatchStage.ExtractIso,
            PatchStage.PatchExecutableText,
            PatchStage.PatchBmdTables,
            PatchStage.PatchResourceText,
            PatchStage.PatchFonts,
            PatchStage.PatchImages,
            PatchStage.RepackIso
        };

        public static PatchStage[] GetStages()
        {
            PatchStage[] copy = new PatchStage[Stages.Length];
            Array.Copy(Stages, copy, Stages.Length);
            return copy;
        }

        public static string GetDisplayName(PatchStage stage)
        {
            switch (stage)
            {
                case PatchStage.ValidateInput:
                    return "ISO 확인";
                case PatchStage.PrepareWorkspace:
                    return "작업 폴더 준비";
                case PatchStage.ExtractIso:
                    return "XISO 추출";
                case PatchStage.PatchExecutableText:
                    return "XEX 텍스트 패치";
                case PatchStage.PatchBmdTables:
                    return "BMD/STBL 패치";
                case PatchStage.PatchResourceText:
                    return ".e BTX 패치";
                case PatchStage.PatchFonts:
                    return "한글 폰트 패치";
                case PatchStage.PatchImages:
                    return "이미지 텍스트 패치";
                case PatchStage.RepackIso:
                    return "ISO 재패킹";
                case PatchStage.Complete:
                    return "완료";
                default:
                    return stage.ToString();
            }
        }

        public static int GetPercent(PatchStage stage)
        {
            switch (stage)
            {
                case PatchStage.ValidateInput:
                    return 8;
                case PatchStage.PrepareWorkspace:
                    return 14;
                case PatchStage.ExtractIso:
                    return 24;
                case PatchStage.PatchExecutableText:
                    return 36;
                case PatchStage.PatchBmdTables:
                    return 48;
                case PatchStage.PatchResourceText:
                    return 62;
                case PatchStage.PatchFonts:
                    return 74;
                case PatchStage.PatchImages:
                    return 84;
                case PatchStage.RepackIso:
                    return 94;
                case PatchStage.Complete:
                    return 100;
                default:
                    return 0;
            }
        }

        public static string BuildDefaultOutputIsoPath(string inputIsoPath)
        {
            string directory = Path.GetDirectoryName(inputIsoPath);
            if (String.IsNullOrEmpty(directory))
            {
                directory = Environment.CurrentDirectory;
            }

            string name = Path.GetFileNameWithoutExtension(inputIsoPath);
            return Path.Combine(directory, name + "_ko.iso");
        }

        public static string BuildDefaultWorkRoot(string inputIsoPath)
        {
            string directory = Path.GetDirectoryName(inputIsoPath);
            if (String.IsNullOrEmpty(directory))
            {
                directory = Environment.CurrentDirectory;
            }

            string name = Path.GetFileNameWithoutExtension(inputIsoPath);
            return Path.Combine(directory, name + "_trustybell_patcher_work");
        }
    }
}

