using System;
using System.IO;

namespace TrystybellKoreanPatcher.Patching
{
    public sealed class ValidateInputStep : ITrustyBellPatchStep
    {
        public PatchStage Stage
        {
            get { return PatchStage.ValidateInput; }
        }

        public string DisplayName
        {
            get { return PatchStageCatalog.GetDisplayName(Stage); }
        }

        public PatchStepResult Run(PatchContext context)
        {
            context.Report(Stage, PatchStageState.Running, PatchStageCatalog.GetPercent(Stage), "입력 ISO를 확인하는 중...");

            string isoPath = context.Options.InputIsoPath;
            if (String.IsNullOrWhiteSpace(isoPath))
            {
                throw new InvalidOperationException("패치할 ISO 파일을 먼저 선택해 주세요.");
            }

            if (!File.Exists(isoPath))
            {
                throw new FileNotFoundException("선택한 ISO 파일을 찾을 수 없습니다.", isoPath);
            }

            if (!String.Equals(Path.GetExtension(isoPath), ".iso", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("ISO 파일만 선택할 수 있습니다.");
            }

            if (String.IsNullOrWhiteSpace(context.Options.OutputIsoPath))
            {
                context.Options.OutputIsoPath = PatchStageCatalog.BuildDefaultOutputIsoPath(isoPath);
            }

            if (String.IsNullOrWhiteSpace(context.Options.WorkRoot))
            {
                context.Options.WorkRoot = PatchStageCatalog.BuildDefaultWorkRoot(isoPath);
            }

            context.Report(Stage, PatchStageState.Complete, PatchStageCatalog.GetPercent(Stage), "입력 ISO 확인 완료");
            return PatchStepResult.Complete("입력 ISO 확인 완료");
        }
    }

    public sealed class PrepareWorkspaceStep : ITrustyBellPatchStep
    {
        public PatchStage Stage
        {
            get { return PatchStage.PrepareWorkspace; }
        }

        public string DisplayName
        {
            get { return PatchStageCatalog.GetDisplayName(Stage); }
        }

        public PatchStepResult Run(PatchContext context)
        {
            context.Report(Stage, PatchStageState.Running, PatchStageCatalog.GetPercent(Stage), "작업 경로를 계산하는 중...");

            string outputDirectory = Path.GetDirectoryName(context.Options.OutputIsoPath);
            if (String.IsNullOrEmpty(outputDirectory))
            {
                outputDirectory = Environment.CurrentDirectory;
            }

            if (!Directory.Exists(outputDirectory))
            {
                throw new DirectoryNotFoundException("출력 폴더를 찾을 수 없습니다: " + outputDirectory);
            }

            context.Report(Stage, PatchStageState.Complete, PatchStageCatalog.GetPercent(Stage), "작업 경로 준비 완료");
            return PatchStepResult.Complete("작업 경로 준비 완료");
        }
    }

    public sealed class PendingPatchStep : ITrustyBellPatchStep
    {
        private readonly PatchStage stage;
        private readonly string implementationNote;

        public PendingPatchStep(PatchStage stage, string implementationNote)
        {
            this.stage = stage;
            this.implementationNote = implementationNote;
        }

        public PatchStage Stage
        {
            get { return stage; }
        }

        public string DisplayName
        {
            get { return PatchStageCatalog.GetDisplayName(Stage); }
        }

        public PatchStepResult Run(PatchContext context)
        {
            string message = DisplayName + " 단계는 순수 C# 구현을 연결해야 합니다. " + implementationNote;
            context.Report(Stage, PatchStageState.Blocked, PatchStageCatalog.GetPercent(Stage), message);
            return PatchStepResult.PendingImplementation(message);
        }
    }
}

