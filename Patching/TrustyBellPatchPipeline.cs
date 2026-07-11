using System;
using System.Collections.Generic;

namespace TrustyBellKoreanPatcher.Patching
{
    public sealed class TrustyBellPatchPipeline
    {
        private readonly List<ITrustyBellPatchStep> steps;

        public TrustyBellPatchPipeline()
        {
            steps = new List<ITrustyBellPatchStep>();
            steps.Add(new ValidateInputStep());
            steps.Add(new PrepareWorkspaceStep());
            steps.Add(new ExtractIsoStep());
            steps.Add(new ApplyKoreanPatchStep());
            steps.Add(new RepackIsoStep());
        }

        public PatchResult Run(PatchJobOptions options, Action<PatchProgressInfo> report)
        {
            PatchContext context = new PatchContext(options, report);
            foreach (ITrustyBellPatchStep step in steps)
            {
                step.Run(context);
            }

            try
            {
                PatchRuntime.DeleteWorkspace(options);
            }
            catch (Exception ex)
            {
                context.Report(
                    PatchStage.Complete,
                    PatchStageState.Running,
                    99,
                    "패치는 완료됐지만 임시 작업 폴더를 지우지 못했습니다: " + ex.Message);
            }

            context.Report(
                PatchStage.Complete,
                PatchStageState.Complete,
                100,
                "패치 완료: " + options.OutputIsoPath);
            return PatchResult.Complete(options.OutputIsoPath);
        }
    }
}
