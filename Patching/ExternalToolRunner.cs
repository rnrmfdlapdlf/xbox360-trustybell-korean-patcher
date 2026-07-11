using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace TrustyBellKoreanPatcher.Patching
{
    internal static class ExternalToolRunner
    {
        public static string Run(
            string fileName,
            string arguments,
            string workingDirectory,
            string displayName,
            IDictionary<string, string> environmentVariables)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = fileName;
            startInfo.Arguments = arguments;
            startInfo.WorkingDirectory = workingDirectory;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            if (environmentVariables != null)
            {
                foreach (KeyValuePair<string, string> item in environmentVariables)
                {
                    startInfo.EnvironmentVariables[item.Key] = item.Value;
                }
            }

            StringBuilder output = new StringBuilder();
            object outputLock = new object();
            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                    {
                        lock (outputLock)
                        {
                            output.AppendLine(e.Data);
                        }
                    }
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                    {
                        lock (outputLock)
                        {
                            output.AppendLine(e.Data);
                        }
                    }
                };

                try
                {
                    process.Start();
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(displayName + " 실행 파일을 시작할 수 없습니다: " + fileName, ex);
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    string message;
                    lock (outputLock)
                    {
                        message = output.ToString().Trim();
                    }

                    if (message.Length == 0)
                    {
                        message = "exit code " + process.ExitCode.ToString();
                    }
                    else if (message.Length > 6000)
                    {
                        message = message.Substring(message.Length - 6000);
                    }

                    throw new InvalidOperationException(displayName + " 실행 실패: " + message);
                }
            }

            lock (outputLock)
            {
                return output.ToString();
            }
        }

        public static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
