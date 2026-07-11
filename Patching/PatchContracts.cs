using System;
using System.IO;

namespace TrustyBellKoreanPatcher.Patching
{
    public enum PatchStage
    {
        ValidateInput = 0,
        PrepareWorkspace = 1,
        ExtractIso = 2,
        ApplyKoreanPatch = 3,
        RepackIso = 4,
        Complete = 5
    }

    public enum PatchStageState
    {
        Waiting,
        Running,
        Complete,
        Blocked
    }

    public sealed class PatchJobOptions
    {
        public string InputIsoPath { get; set; }
        public string OutputIsoPath { get; set; }
        public string WorkRoot { get; set; }
        public string ExtractRoot { get; set; }
        public string PatchBundleRoot { get; set; }
        public string ExisoPath { get; set; }
        public string XexToolPath { get; set; }
        public int Workers { get; set; }
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
        private PatchStepResult(string message)
        {
            Message = message;
        }

        public string Message { get; private set; }

        public static PatchStepResult Complete(string message)
        {
            return new PatchStepResult(message);
        }
    }

    public sealed class PatchResult
    {
        private PatchResult(bool completed, PatchStage lastStage, string outputIsoPath, string message)
        {
            Completed = completed;
            LastStage = lastStage;
            OutputIsoPath = outputIsoPath;
            Message = message;
        }

        public bool Completed { get; private set; }
        public PatchStage LastStage { get; private set; }
        public string OutputIsoPath { get; private set; }
        public string Message { get; private set; }

        public static PatchResult Complete(string outputIsoPath)
        {
            return new PatchResult(true, PatchStage.Complete, outputIsoPath, "패치가 완료되었습니다.");
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
            PatchStage.ApplyKoreanPatch,
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
                case PatchStage.ApplyKoreanPatch:
                    return "한글 패치 적용";
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
                    return 5;
                case PatchStage.PrepareWorkspace:
                    return 12;
                case PatchStage.ExtractIso:
                    return 35;
                case PatchStage.ApplyKoreanPatch:
                    return 82;
                case PatchStage.RepackIso:
                    return 96;
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
            return Path.Combine(directory, name + "_repacked.iso");
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
