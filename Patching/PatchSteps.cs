using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TrustyBellKoreanPatcher.Patching
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
            context.Report(Stage, PatchStageState.Running, 2, "원본 ISO를 확인하는 중...");

            string isoPath = context.Options.InputIsoPath;
            if (String.IsNullOrWhiteSpace(isoPath))
            {
                throw new InvalidOperationException("패치할 ISO 파일을 먼저 선택해 주세요.");
            }

            isoPath = Path.GetFullPath(isoPath);
            if (!File.Exists(isoPath))
            {
                throw new FileNotFoundException("선택한 ISO 파일을 찾을 수 없습니다.", isoPath);
            }

            if (!String.Equals(Path.GetExtension(isoPath), ".iso", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("ISO 파일만 선택할 수 있습니다.");
            }

            context.Options.InputIsoPath = isoPath;
            context.Options.OutputIsoPath = PatchStageCatalog.BuildDefaultOutputIsoPath(isoPath);
            context.Options.WorkRoot = PatchStageCatalog.BuildDefaultWorkRoot(isoPath);
            context.Options.ExtractRoot = Path.Combine(context.Options.WorkRoot, "xiso_root");
            context.Options.Workers = Math.Max(1, Math.Min(10, Environment.ProcessorCount));

            if (String.Equals(
                Path.GetFullPath(context.Options.OutputIsoPath),
                isoPath,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("출력 ISO 경로가 원본 ISO와 같습니다.");
            }

            PatchRuntime.ValidateFreeSpace(isoPath);
            context.Report(Stage, PatchStageState.Complete, PatchStageCatalog.GetPercent(Stage), "원본 ISO 확인 완료");
            return PatchStepResult.Complete("원본 ISO 확인 완료");
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
            context.Report(Stage, PatchStageState.Running, 7, "패치 도구와 작업 폴더를 준비하는 중...");

            PatchJobOptions options = context.Options;
            options.PatchBundleRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Path.Combine("Assets", "PatchBundle"));
            options.ExisoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Path.Combine("Assets", Path.Combine("Tools", "exiso.exe")));
            if (String.IsNullOrWhiteSpace(options.XexToolPath))
            {
                options.XexToolPath = PatchRuntime.FindUserXexTool();
            }
            else
            {
                options.XexToolPath = Path.GetFullPath(options.XexToolPath);
                if (!String.Equals(Path.GetFileName(options.XexToolPath), "xextool.exe", StringComparison.OrdinalIgnoreCase)
                    || !PatchRuntime.IsSupportedXexTool(options.XexToolPath))
                {
                    options.XexToolPath = null;
                }
            }

            PatchRuntime.RequireFile(options.ExisoPath, "XISO 도구");
            PatchRuntime.RequirePatchBundle(options.PatchBundleRoot);

            PatchRuntime.DeleteWorkspace(options);
            Directory.CreateDirectory(options.ExtractRoot);

            string outputDirectory = Path.GetDirectoryName(options.OutputIsoPath);
            if (String.IsNullOrEmpty(outputDirectory) || !Directory.Exists(outputDirectory))
            {
                throw new DirectoryNotFoundException("원본 ISO 폴더를 찾을 수 없습니다: " + outputDirectory);
            }

            if (File.Exists(options.OutputIsoPath))
            {
                File.Delete(options.OutputIsoPath);
            }

            string xexToolStatus = String.IsNullOrWhiteSpace(options.XexToolPath)
                ? " (xextool.exe 없음: XEX 추가 번역 생략)"
                : " (xextool.exe 확인: XEX 추가 번역 포함)";
            context.Report(Stage, PatchStageState.Complete, PatchStageCatalog.GetPercent(Stage), "작업 폴더 준비 완료" + xexToolStatus);
            return PatchStepResult.Complete("작업 폴더 준비 완료");
        }
    }

    public sealed class ExtractIsoStep : ITrustyBellPatchStep
    {
        public PatchStage Stage
        {
            get { return PatchStage.ExtractIso; }
        }

        public string DisplayName
        {
            get { return PatchStageCatalog.GetDisplayName(Stage); }
        }

        public PatchStepResult Run(PatchContext context)
        {
            PatchJobOptions options = context.Options;
            context.Report(Stage, PatchStageState.Running, 15, "원본 ISO에서 게임 파일을 추출하는 중...");

            string arguments = "-q -x -d "
                + ExternalToolRunner.QuoteArgument(options.ExtractRoot)
                + " "
                + ExternalToolRunner.QuoteArgument(options.InputIsoPath);
            ExternalToolRunner.Run(
                options.ExisoPath,
                arguments,
                Path.GetDirectoryName(options.ExisoPath),
                "ISO 추출",
                null);

            PatchRuntime.ValidateExtractedGame(options.ExtractRoot);
            context.Report(Stage, PatchStageState.Complete, PatchStageCatalog.GetPercent(Stage), "ISO 추출 완료");
            return PatchStepResult.Complete("ISO 추출 완료");
        }
    }

    public sealed class ApplyKoreanPatchStep : ITrustyBellPatchStep
    {
        public PatchStage Stage
        {
            get { return PatchStage.ApplyKoreanPatch; }
        }

        public string DisplayName
        {
            get { return PatchStageCatalog.GetDisplayName(Stage); }
        }

        public PatchStepResult Run(PatchContext context)
        {
            PatchJobOptions options = context.Options;
            context.Report(Stage, PatchStageState.Running, 42, "텍스트·폰트·이미지 한글 패치를 적용하는 중...");
            PatchBundleApplyResult applyResult = BinaryPatchBundle.Apply(
                options.ExtractRoot,
                options.PatchBundleRoot,
                options.Workers,
                options.XexToolPath,
                options.WorkRoot);
            PatchRuntime.ValidatePatchedGame(options.ExtractRoot);
            string xexStatus = applyResult.XexApplied
                ? ", XEX 패치 포함"
                : (applyResult.XexSkipped
                    ? ", XEX 추가 번역 생략"
                        + (String.IsNullOrWhiteSpace(applyResult.XexSkipReason)
                            ? String.Empty
                            : " (" + applyResult.XexSkipReason + ")")
                    : String.Empty);
            context.Report(
                Stage,
                PatchStageState.Complete,
                PatchStageCatalog.GetPercent(Stage),
                "한글 패치 적용 완료 (" + applyResult.TotalFiles.ToString("N0") + "개 파일" + xexStatus + ")");
            return PatchStepResult.Complete("한글 패치 적용 완료");
        }
    }

    public sealed class RepackIsoStep : ITrustyBellPatchStep
    {
        public PatchStage Stage
        {
            get { return PatchStage.RepackIso; }
        }

        public string DisplayName
        {
            get { return PatchStageCatalog.GetDisplayName(Stage); }
        }

        public PatchStepResult Run(PatchContext context)
        {
            PatchJobOptions options = context.Options;
            context.Report(Stage, PatchStageState.Running, 86, "패치된 게임 파일을 ISO로 재패킹하는 중...");

            if (File.Exists(options.OutputIsoPath))
            {
                File.Delete(options.OutputIsoPath);
            }

            string arguments = "-q -c "
                + ExternalToolRunner.QuoteArgument(options.ExtractRoot)
                + " "
                + ExternalToolRunner.QuoteArgument(options.OutputIsoPath);
            ExternalToolRunner.Run(
                options.ExisoPath,
                arguments,
                Path.GetDirectoryName(options.ExisoPath),
                "ISO 재패킹",
                null);

            FileInfo output = new FileInfo(options.OutputIsoPath);
            if (!output.Exists || output.Length == 0)
            {
                throw new InvalidOperationException("재패킹된 ISO가 생성되지 않았습니다.");
            }

            context.Report(Stage, PatchStageState.Complete, PatchStageCatalog.GetPercent(Stage), "ISO 재패킹 완료");
            return PatchStepResult.Complete("ISO 재패킹 완료");
        }
    }

    internal static class PatchRuntime
    {
        private const string XexTool63Sha256 = "d93c1b814ad6ff124834f4235bf8aac9f09dba8d69c335ebecc8d6efe8d5a062";

        public static bool IsSupportedXexTool(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    builder.Append(value.ToString("x2"));
                }
                return String.Equals(builder.ToString(), XexTool63Sha256, StringComparison.Ordinal);
            }
        }

        public static string FindUserXexTool()
        {
            string configured = Environment.GetEnvironmentVariable("TRUSTYBELL_XEXTOOL");
            if (!String.IsNullOrWhiteSpace(configured))
            {
                string fullPath = Path.GetFullPath(configured);
                if (IsSupportedXexTool(fullPath))
                {
                    return fullPath;
                }
            }

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = new string[]
            {
                Path.Combine(baseDirectory, "xextool.exe"),
                Path.Combine(baseDirectory, Path.Combine("Assets", Path.Combine("Tools", "xextool.exe")))
            };
            foreach (string candidate in candidates)
            {
                if (IsSupportedXexTool(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }

        public static void RequirePatchBundle(string bundleRoot)
        {
            RequireFile(Path.Combine(bundleRoot, "manifest.tsv"), "C# 패치 번들 manifest");
            string patchRoot = Path.Combine(bundleRoot, "patches");
            if (!Directory.Exists(patchRoot) || Directory.GetFiles(patchRoot, "*.tbp.gz").Length == 0)
            {
                throw new DirectoryNotFoundException("C# 패치 번들 파일을 찾을 수 없습니다: " + patchRoot);
            }
        }

        public static void RequireFile(string path, string displayName)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(displayName + "을(를) 찾을 수 없습니다.", path);
            }
        }

        public static void ValidateExtractedGame(string root)
        {
            string[] required = new string[]
            {
                "default.xex",
                "index.vmtoc"
            };
            foreach (string relativePath in required)
            {
                RequireFile(Path.Combine(root, relativePath), "추출된 게임 파일");
            }
        }

        public static void ValidatePatchedGame(string root)
        {
            ValidateExtractedGame(root);
        }

        public static void ValidateFreeSpace(string isoPath)
        {
            try
            {
                FileInfo input = new FileInfo(isoPath);
                string root = Path.GetPathRoot(isoPath);
                DriveInfo drive = new DriveInfo(root);
                long required = checked(input.Length * 2L + 2L * 1024L * 1024L * 1024L);
                if (drive.AvailableFreeSpace < required)
                {
                    throw new IOException(
                        "ISO 추출과 재패킹에 필요한 여유 공간이 부족합니다. 필요: 약 "
                        + (required / (1024L * 1024L * 1024L)).ToString()
                        + " GiB");
                }
            }
            catch (ArgumentException)
            {
                // UNC 및 특수 경로에서는 DriveInfo 검사를 생략하고 실제 I/O 오류를 사용한다.
            }
        }

        public static void DeleteWorkspace(PatchJobOptions options)
        {
            if (!Directory.Exists(options.WorkRoot))
            {
                return;
            }

            string expected = Path.GetFullPath(PatchStageCatalog.BuildDefaultWorkRoot(options.InputIsoPath));
            string actual = Path.GetFullPath(options.WorkRoot);
            if (!String.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("안전하지 않은 작업 폴더 삭제를 거부했습니다: " + actual);
            }

            Directory.Delete(actual, true);
        }
    }
}
