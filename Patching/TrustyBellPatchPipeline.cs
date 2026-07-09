using System;
using System.Collections.Generic;

namespace TrystybellKoreanPatcher.Patching
{
    public sealed class TrustyBellPatchPipeline
    {
        private readonly List<ITrustyBellPatchStep> steps;

        public TrustyBellPatchPipeline()
        {
            steps = new List<ITrustyBellPatchStep>();
            steps.Add(new ValidateInputStep());
            steps.Add(new PrepareWorkspaceStep());
            steps.Add(new PendingPatchStep(PatchStage.ExtractIso, "XDVDFS/XISO 추출기를 C# 서비스로 분리할 예정입니다."));
            steps.Add(new PendingPatchStep(PatchStage.PatchExecutableText, "default.xex의 확정 델타 패치를 C# 구현으로 이식할 예정입니다."));
            steps.Add(new PendingPatchStep(PatchStage.PatchBmdTables, "BMD/STBL 문자열 테이블 재구성기를 C# 구현으로 이식할 예정입니다."));
            steps.Add(new PendingPatchStep(PatchStage.PatchResourceText, ".e 리소스 BTX fixed/reallocate 패처를 C# 구현으로 이식할 예정입니다."));
            steps.Add(new PendingPatchStep(PatchStage.PatchFonts, "p1.fnt/p1_g.fnt donor glyph 렌더링과 BC/DDS 갱신 로직을 C# 구현으로 이식할 예정입니다."));
            steps.Add(new PendingPatchStep(PatchStage.PatchImages, "AppKeep.bmd 같은 NTEX/DDS 이미지 텍스트 패치를 C# 구현으로 이식할 예정입니다."));
            steps.Add(new PendingPatchStep(PatchStage.RepackIso, "패치된 XISO 폴더를 ISO로 재패킹하는 C# 서비스를 연결할 예정입니다."));
        }

        public PatchResult Run(PatchJobOptions options, Action<PatchProgressInfo> report)
        {
            PatchContext context = new PatchContext(options, report);

            foreach (ITrustyBellPatchStep step in steps)
            {
                PatchStepResult result = step.Run(context);
                if (result.Status == PatchStepStatus.PendingImplementation)
                {
                    return PatchResult.Pending(step.Stage, options.OutputIsoPath, result.Message);
                }
            }

            if (report != null)
            {
                report(new PatchProgressInfo(PatchStage.Complete, PatchStageState.Complete, 100, "패치 완료"));
            }

            return PatchResult.Complete(options.OutputIsoPath);
        }
    }
}

