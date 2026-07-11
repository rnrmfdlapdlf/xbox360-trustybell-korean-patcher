using System;
using System.IO;
using System.Windows.Forms;
using TrustyBellKoreanPatcher.Patching;

namespace TrustyBellKoreanPatcher
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args != null
                && (args.Length == 4 || args.Length == 6)
                && String.Equals(args[0], "--build-bundle", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    BinaryPatchBundle.Builder.Build(
                        args[1],
                        args[2],
                        args[3],
                        args.Length == 6 ? args[4] : null,
                        args.Length == 6 ? args[5] : null);
                    return 0;
                }
                catch (Exception ex)
                {
                    WriteDiagnostic("build_bundle", ex);
                    return 2;
                }
            }

            if (args != null
                && (args.Length == 3 || args.Length == 4)
                && String.Equals(args[0], "--apply-bundle", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    BinaryPatchBundle.Apply(
                        args[1],
                        args[2],
                        Math.Max(1, Math.Min(10, Environment.ProcessorCount)),
                        args.Length == 4 ? args[3] : null,
                        args[1]);
                    return 0;
                }
                catch (Exception ex)
                {
                    WriteDiagnostic("apply_bundle", ex);
                    return 3;
                }
            }

            if (args != null
                && args.Length == 7
                && String.Equals(args[0], "--replace-resource-patch", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    BinaryPatchBundle.Builder.ReplaceDecodedResource(
                        args[1],
                        args[2],
                        args[3],
                        args[4],
                        args[5],
                        args[6]);
                    return 0;
                }
                catch (Exception ex)
                {
                    WriteDiagnostic("replace_resource_patch", ex);
                    return 5;
                }
            }

            if (args != null
                && args.Length == 4
                && String.Equals(args[0], "--decode-resource", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.WriteAllBytes(
                        args[3],
                        TrustyBellResourceDecoder.DecodeFile(
                            args[1],
                            TrustyBellResourceDecoder.ReadVmtoc(args[1]),
                            args[2]));
                    return 0;
                }
                catch (Exception ex)
                {
                    WriteDiagnostic("decode_resource", ex);
                    return 4;
                }
            }

            if (args != null && args.Length == 1 && String.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string exisoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Path.Combine("Assets", Path.Combine("Tools", "exiso.exe")));
                    PatchRuntime.RequireFile(exisoPath, "XISO 도구");
                    PatchRuntime.RequirePatchBundle(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Path.Combine("Assets", "PatchBundle")));
                    return 0;
                }
                catch
                {
                    return 1;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }

        private static void WriteDiagnostic(string command, Exception exception)
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "TrustyBellKoreanPatcher_" + command + "_error.txt");
                File.WriteAllText(path, exception.ToString());
            }
            catch
            {
                // 개발용 숨은 명령의 진단 기록 실패는 원래 exit code를 유지한다.
            }
        }
    }
}
