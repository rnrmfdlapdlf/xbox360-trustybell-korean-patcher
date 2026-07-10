using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace TrystybellKoreanPatcher.Patching
{
    internal sealed class PatchBundleEntry
    {
        public string Mode { get; set; }
        public string Identifier { get; set; }
        public string PatchFile { get; set; }
    }

    internal sealed class PatchBundleApplyResult
    {
        public int ResourceFiles { get; set; }
        public bool XexApplied { get; set; }
        public bool XexSkipped { get; set; }

        public int TotalFiles
        {
            get { return ResourceFiles + (XexApplied ? 1 : 0); }
        }
    }

    internal static class BinaryPatchBundle
    {
        private const string ManifestName = "manifest.tsv";
        private const string ManifestHeader = "TBPBUNDLE2";
        private const string DecodedMode = "D";
        private const string IndexMode = "I";
        private const string XexMode = "X";
        private const string IndexPath = "index.vmtoc";
        private const string XexPath = "default.xex";

        public static PatchBundleApplyResult Apply(
            string gameRoot,
            string bundleRoot,
            int workers,
            string xexToolPath,
            string workRoot)
        {
            gameRoot = Path.GetFullPath(gameRoot);
            bundleRoot = Path.GetFullPath(bundleRoot);
            workRoot = Path.GetFullPath(workRoot);
            if (!String.IsNullOrWhiteSpace(xexToolPath))
            {
                xexToolPath = Path.GetFullPath(xexToolPath);
            }

            List<PatchBundleEntry> entries = LoadManifest(bundleRoot);
            Dictionary<string, VmtocEntry> vmtoc = TrustyBellResourceDecoder.ReadVmtoc(gameRoot);
            Dictionary<string, VmtocEntry> resourcesByIdentifier = BuildResourceIdentifierMap(vmtoc);
            List<PatchBundleEntry> indexEntries = entries
                .Where(item => String.Equals(item.Mode, IndexMode, StringComparison.Ordinal))
                .ToList();
            List<PatchBundleEntry> fileEntries = entries
                .Where(item => String.Equals(item.Mode, DecodedMode, StringComparison.Ordinal))
                .ToList();
            List<PatchBundleEntry> xexEntries = entries
                .Where(item => String.Equals(item.Mode, XexMode, StringComparison.Ordinal))
                .ToList();

            if (indexEntries.Count != 1 || xexEntries.Count > 1)
            {
                throw new InvalidDataException("패치 번들의 필수 항목 구성이 올바르지 않습니다.");
            }

            ParallelOptions parallelOptions = new ParallelOptions();
            parallelOptions.MaxDegreeOfParallelism = Math.Max(1, workers);
            try
            {
                Parallel.ForEach(
                    fileEntries,
                    parallelOptions,
                    delegate(PatchBundleEntry entry)
                    {
                        ApplyDecodedEntry(gameRoot, bundleRoot, entry, vmtoc, resourcesByIdentifier);
                    });
            }
            catch (AggregateException ex)
            {
                Exception first = ex.Flatten().InnerExceptions.FirstOrDefault();
                throw first ?? ex;
            }

            foreach (PatchBundleEntry entry in indexEntries)
            {
                ApplyRawEntry(gameRoot, bundleRoot, entry, IndexPath);
            }

            PatchBundleApplyResult result = new PatchBundleApplyResult();
            result.ResourceFiles = fileEntries.Count + indexEntries.Count;
            if (xexEntries.Count == 1)
            {
                if (String.IsNullOrWhiteSpace(xexToolPath))
                {
                    result.XexSkipped = true;
                }
                else
                {
                    ApplyXexEntry(gameRoot, bundleRoot, xexEntries[0], xexToolPath, workRoot);
                    result.XexApplied = true;
                }
            }

            return result;
        }

        private static void ApplyDecodedEntry(
            string gameRoot,
            string bundleRoot,
            PatchBundleEntry entry,
            IDictionary<string, VmtocEntry> vmtoc,
            IDictionary<string, VmtocEntry> resourcesByIdentifier)
        {
            VmtocEntry resource;
            if (!resourcesByIdentifier.TryGetValue(entry.Identifier, out resource))
            {
                throw new InvalidDataException("선택한 ISO에서 패치 대상 리소스 식별자를 찾을 수 없습니다: " + entry.Identifier);
            }

            byte[] source = TrustyBellResourceDecoder.DecodeFile(gameRoot, vmtoc, resource.Name);
            string patchPath = Path.Combine(bundleRoot, entry.PatchFile.Replace('/', Path.DirectorySeparatorChar));
            byte[] target = BinaryDelta.Apply(source, patchPath);
            WriteAtomically(ToGamePath(gameRoot, resource.Name), target);
        }

        private static void ApplyRawEntry(
            string gameRoot,
            string bundleRoot,
            PatchBundleEntry entry,
            string relativePath)
        {
            if (!String.Equals(entry.Identifier, ComputeIdentifier(relativePath), StringComparison.Ordinal))
            {
                throw new InvalidDataException("패치 번들의 고정 파일 식별자가 올바르지 않습니다.");
            }

            string targetPath = ToGamePath(gameRoot, relativePath);
            byte[] source = File.ReadAllBytes(targetPath);
            string patchPath = Path.Combine(bundleRoot, entry.PatchFile.Replace('/', Path.DirectorySeparatorChar));
            WriteAtomically(targetPath, BinaryDelta.Apply(source, patchPath));
        }

        private static void ApplyXexEntry(
            string gameRoot,
            string bundleRoot,
            PatchBundleEntry entry,
            string xexToolPath,
            string workRoot)
        {
            if (!String.Equals(entry.Identifier, ComputeIdentifier(XexPath), StringComparison.Ordinal))
            {
                throw new InvalidDataException("패치 번들의 XEX 식별자가 올바르지 않습니다.");
            }

            PatchRuntime.RequireFile(xexToolPath, "사용자가 제공한 xextool.exe");
            string sourcePath = ToGamePath(gameRoot, XexPath);
            long originalLength64 = new FileInfo(sourcePath).Length;
            if (originalLength64 < 0 || originalLength64 > Int32.MaxValue)
            {
                throw new InvalidDataException("원본 XEX 크기를 지원할 수 없습니다.");
            }
            string convertedPath = Path.Combine(workRoot, "default_xex_unencrypted_uncompressed.tmp");
            if (File.Exists(convertedPath))
            {
                File.Delete(convertedPath);
            }

            ExternalToolRunner.Run(
                xexToolPath,
                "-e d -c u -o " + ExternalToolRunner.QuoteArgument(convertedPath) + " " + ExternalToolRunner.QuoteArgument(sourcePath),
                Path.GetDirectoryName(xexToolPath),
                "XEX 변환",
                null);
            PatchRuntime.RequireFile(convertedPath, "변환된 XEX");

            byte[] source = File.ReadAllBytes(convertedPath);
            int originalLength = (int)originalLength64;
            if (source.Length < originalLength)
            {
                byte[] padded = new byte[originalLength];
                Buffer.BlockCopy(source, 0, padded, 0, source.Length);
                source = padded;
            }
            string patchPath = Path.Combine(bundleRoot, entry.PatchFile.Replace('/', Path.DirectorySeparatorChar));
            WriteAtomically(sourcePath, BinaryDelta.Apply(source, patchPath));
            File.Delete(convertedPath);
        }

        private static void WriteAtomically(string targetPath, byte[] target)
        {
            string temporaryPath = targetPath + ".tbpatch.tmp";
            File.WriteAllBytes(temporaryPath, target);
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            File.Move(temporaryPath, targetPath);
        }

        private static Dictionary<string, VmtocEntry> BuildResourceIdentifierMap(
            IDictionary<string, VmtocEntry> vmtoc)
        {
            Dictionary<string, VmtocEntry> result = new Dictionary<string, VmtocEntry>(StringComparer.Ordinal);
            foreach (VmtocEntry entry in vmtoc.Values)
            {
                string identifier = ComputeIdentifier(entry.Name);
                if (result.ContainsKey(identifier))
                {
                    throw new InvalidDataException("리소스 식별자 충돌이 발생했습니다.");
                }
                result[identifier] = entry;
            }
            return result;
        }

        private static List<PatchBundleEntry> LoadManifest(string bundleRoot)
        {
            string manifestPath = Path.Combine(bundleRoot, ManifestName);
            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException("C# 패치 번들 manifest를 찾을 수 없습니다.", manifestPath);
            }

            string[] lines = File.ReadAllLines(manifestPath, Encoding.UTF8);
            if (lines.Length < 2 || !String.Equals(lines[0], ManifestHeader, StringComparison.Ordinal))
            {
                throw new InvalidDataException("C# 패치 번들 manifest 형식이 올바르지 않습니다.");
            }

            List<PatchBundleEntry> entries = new List<PatchBundleEntry>();
            for (int index = 1; index < lines.Length; index++)
            {
                if (String.IsNullOrWhiteSpace(lines[index]) || lines[index].StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] fields = lines[index].Split('\t');
                if (fields.Length != 3)
                {
                    throw new InvalidDataException("manifest 행 형식이 올바르지 않습니다: " + lines[index]);
                }

                PatchBundleEntry entry = new PatchBundleEntry();
                entry.Mode = fields[0];
                entry.Identifier = fields[1];
                entry.PatchFile = fields[2];
                if (!IsSha256Identifier(entry.Identifier))
                {
                    throw new InvalidDataException("manifest 리소스 식별자가 SHA-256 형식이 아닙니다.");
                }
                entries.Add(entry);
            }

            if (entries.Count == 0)
            {
                throw new InvalidDataException("C# 패치 번들이 비어 있습니다.");
            }

            return entries;
        }

        private static string ToGamePath(string root, string relativePath)
        {
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        internal static string ComputeIdentifier(string relativePath)
        {
            string normalized = relativePath.Replace('\\', '/').ToLowerInvariant();
            byte[] bytes = Encoding.UTF8.GetBytes(normalized);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    builder.Append(value.ToString("x2"));
                }
                return builder.ToString();
            }
        }

        private static bool IsSha256Identifier(string value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }
            foreach (char ch in value)
            {
                if (!((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f')))
                {
                    return false;
                }
            }
            return true;
        }

        internal static class Builder
        {
            public static int Build(
                string sourceRoot,
                string patchedRoot,
                string outputRoot,
                string xexSourcePath,
                string xexTargetPath)
            {
                sourceRoot = Path.GetFullPath(sourceRoot);
                patchedRoot = Path.GetFullPath(patchedRoot);
                outputRoot = Path.GetFullPath(outputRoot);
                if (Directory.Exists(outputRoot) || File.Exists(outputRoot))
                {
                    throw new IOException("패치 번들 출력 경로가 이미 존재합니다: " + outputRoot);
                }

                Dictionary<string, VmtocEntry> sourceEntries = TrustyBellResourceDecoder.ReadVmtoc(sourceRoot);
                Dictionary<string, VmtocEntry> patchedEntries = TrustyBellResourceDecoder.ReadVmtoc(patchedRoot);
                Directory.CreateDirectory(outputRoot);
                Directory.CreateDirectory(Path.Combine(outputRoot, "patches"));

                List<PatchBundleEntry> manifest = new List<PatchBundleEntry>();
                List<VmtocEntry> changedResources = sourceEntries.Values
                    .Where(
                        entry => patchedEntries.ContainsKey(entry.Name)
                            && (entry.Flags != patchedEntries[entry.Name].Flags
                                || entry.DecodedSize != patchedEntries[entry.Name].DecodedSize))
                    .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                int patchIndex = 0;
                foreach (VmtocEntry entry in changedResources)
                {
                    byte[] source = TrustyBellResourceDecoder.DecodeFile(sourceRoot, sourceEntries, entry.Name);
                    byte[] target = File.ReadAllBytes(ToGamePath(patchedRoot, entry.Name));
                    AddPatch(outputRoot, manifest, patchIndex++, DecodedMode, entry.Name, source, target);
                }

                AddRawPatch(sourceRoot, patchedRoot, outputRoot, manifest, ref patchIndex, IndexMode, IndexPath);
                if (!String.IsNullOrWhiteSpace(xexSourcePath) && !String.IsNullOrWhiteSpace(xexTargetPath))
                {
                    AddPatch(
                        outputRoot,
                        manifest,
                        patchIndex++,
                        XexMode,
                        XexPath,
                        File.ReadAllBytes(Path.GetFullPath(xexSourcePath)),
                        File.ReadAllBytes(Path.GetFullPath(xexTargetPath)));
                }

                List<string> lines = new List<string>();
                lines.Add(ManifestHeader);
                lines.AddRange(
                    manifest.Select(
                        entry => entry.Mode + "\t" + entry.Identifier + "\t" + entry.PatchFile.Replace('\\', '/')));
                File.WriteAllLines(Path.Combine(outputRoot, ManifestName), lines.ToArray(), new UTF8Encoding(false));
                return manifest.Count;
            }

            private static void AddRawPatch(
                string sourceRoot,
                string patchedRoot,
                string outputRoot,
                List<PatchBundleEntry> manifest,
                ref int patchIndex,
                string mode,
                string relativePath)
            {
                byte[] source = File.ReadAllBytes(ToGamePath(sourceRoot, relativePath));
                byte[] target = File.ReadAllBytes(ToGamePath(patchedRoot, relativePath));
                AddPatch(outputRoot, manifest, patchIndex++, mode, relativePath, source, target);
            }

            private static void AddPatch(
                string outputRoot,
                List<PatchBundleEntry> manifest,
                int index,
                string mode,
                string relativePath,
                byte[] source,
                byte[] target)
            {
                string patchFile = Path.Combine("patches", index.ToString("D4") + ".tbp.gz");
                BinaryDelta.Build(source, target, Path.Combine(outputRoot, patchFile));

                PatchBundleEntry manifestEntry = new PatchBundleEntry();
                manifestEntry.Mode = mode;
                manifestEntry.Identifier = ComputeIdentifier(relativePath);
                manifestEntry.PatchFile = patchFile.Replace('\\', '/');
                manifest.Add(manifestEntry);
            }
        }
    }

    internal static class BinaryDelta
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("TBP1");
        private const int MatchBlockSize = 64;
        private const int SourceSampleStep = 32;
        private const ulong RollingBase = 257UL;

        public static void Build(byte[] source, byte[] target, string outputPath)
        {
            List<DeltaOperation> operations = BuildOperations(source, target);
            byte[] sourceHash = ComputeSha256(source);
            byte[] targetHash = ComputeSha256(target);

            using (FileStream file = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (GZipStream gzip = new GZipStream(file, CompressionMode.Compress))
            using (BinaryWriter writer = new BinaryWriter(gzip, Encoding.UTF8))
            {
                writer.Write(Magic);
                writer.Write((long)target.Length);
                writer.Write(sourceHash);
                writer.Write(targetHash);
                writer.Write(operations.Count);
                foreach (DeltaOperation operation in operations)
                {
                    writer.Write(operation.Kind);
                    if (operation.Kind == 0)
                    {
                        writer.Write(operation.SourceOffset);
                        writer.Write(operation.Length);
                    }
                    else
                    {
                        writer.Write(operation.Data.Length);
                        writer.Write(operation.Data);
                    }
                }
            }
        }

        public static byte[] Apply(byte[] source, string patchPath)
        {
            using (FileStream file = File.OpenRead(patchPath))
            using (GZipStream gzip = new GZipStream(file, CompressionMode.Decompress))
            using (BinaryReader reader = new BinaryReader(gzip, Encoding.UTF8))
            {
                byte[] magic = reader.ReadBytes(Magic.Length);
                if (!magic.SequenceEqual(Magic))
                {
                    throw new InvalidDataException("패치 파일 magic이 올바르지 않습니다: " + patchPath);
                }

                long targetLength64 = reader.ReadInt64();
                if (targetLength64 < 0 || targetLength64 > Int32.MaxValue)
                {
                    throw new InvalidDataException("패치 결과 크기가 지원 범위를 벗어났습니다.");
                }

                byte[] expectedSourceHash = reader.ReadBytes(32);
                byte[] expectedTargetHash = reader.ReadBytes(32);
                if (!ComputeSha256(source).SequenceEqual(expectedSourceHash))
                {
                    throw new InvalidDataException(
                        "선택한 ISO의 원본 리소스가 지원되는 일본판과 다릅니다: " + Path.GetFileName(patchPath));
                }

                byte[] target = new byte[(int)targetLength64];
                int targetPosition = 0;
                int operationCount = reader.ReadInt32();
                if (operationCount < 0 || operationCount > 10000000)
                {
                    throw new InvalidDataException("패치 operation 수가 올바르지 않습니다.");
                }

                for (int index = 0; index < operationCount; index++)
                {
                    byte kind = reader.ReadByte();
                    if (kind == 0)
                    {
                        long sourceOffset64 = reader.ReadInt64();
                        int length = reader.ReadInt32();
                        if (sourceOffset64 < 0 || sourceOffset64 > Int32.MaxValue || length < 0)
                        {
                            throw new InvalidDataException("COPY operation 범위가 올바르지 않습니다.");
                        }

                        int sourceOffset = (int)sourceOffset64;
                        if (sourceOffset > source.Length - length || targetPosition > target.Length - length)
                        {
                            throw new InvalidDataException("COPY operation이 파일 범위를 벗어났습니다.");
                        }

                        Buffer.BlockCopy(source, sourceOffset, target, targetPosition, length);
                        targetPosition += length;
                    }
                    else if (kind == 1)
                    {
                        int length = reader.ReadInt32();
                        if (length < 0 || targetPosition > target.Length - length)
                        {
                            throw new InvalidDataException("ADD operation이 파일 범위를 벗어났습니다.");
                        }

                        byte[] data = reader.ReadBytes(length);
                        if (data.Length != length)
                        {
                            throw new EndOfStreamException("ADD operation 데이터가 잘렸습니다.");
                        }

                        Buffer.BlockCopy(data, 0, target, targetPosition, length);
                        targetPosition += length;
                    }
                    else
                    {
                        throw new InvalidDataException("알 수 없는 패치 operation입니다.");
                    }
                }

                if (targetPosition != target.Length)
                {
                    throw new InvalidDataException("패치 결과 크기가 header와 다릅니다.");
                }

                if (!ComputeSha256(target).SequenceEqual(expectedTargetHash))
                {
                    throw new InvalidDataException("패치 결과 SHA-256 검증에 실패했습니다.");
                }

                return target;
            }
        }

        private static List<DeltaOperation> BuildOperations(byte[] source, byte[] target)
        {
            if (source.Length == target.Length)
            {
                return BuildAlignedOperations(source, target);
            }

            List<DeltaOperation> operations = new List<DeltaOperation>();
            if (target.Length == 0)
            {
                return operations;
            }

            if (source.Length < MatchBlockSize || target.Length < MatchBlockSize)
            {
                AppendAdd(operations, target, 0, target.Length);
                return operations;
            }

            Dictionary<ulong, int> sourceBlocks = BuildSourceBlockMap(source);
            ulong highestPower = 1;
            for (int index = 1; index < MatchBlockSize; index++)
            {
                highestPower = unchecked(highestPower * RollingBase);
            }

            int targetPosition = 0;
            int pendingStart = 0;
            ulong targetHash = RollingHash(target, 0, MatchBlockSize);
            while (targetPosition <= target.Length - MatchBlockSize)
            {
                int sourcePosition = -1;
                if (targetPosition <= source.Length - MatchBlockSize
                    && BytesEqual(source, targetPosition, target, targetPosition, MatchBlockSize))
                {
                    sourcePosition = targetPosition;
                }
                else
                {
                    int candidate;
                    if (sourceBlocks.TryGetValue(targetHash, out candidate)
                        && BytesEqual(source, candidate, target, targetPosition, MatchBlockSize))
                    {
                        sourcePosition = candidate;
                    }
                }

                if (sourcePosition >= 0)
                {
                    int backward = 0;
                    int backwardLimit = Math.Min(targetPosition - pendingStart, sourcePosition);
                    while (backward < backwardLimit
                        && target[targetPosition - backward - 1] == source[sourcePosition - backward - 1])
                    {
                        backward++;
                    }

                    int copyTargetStart = targetPosition - backward;
                    int copySourceStart = sourcePosition - backward;
                    AppendAdd(operations, target, pendingStart, copyTargetStart - pendingStart);

                    int sourceEnd = sourcePosition + MatchBlockSize;
                    int targetEnd = targetPosition + MatchBlockSize;
                    while (sourceEnd < source.Length
                        && targetEnd < target.Length
                        && source[sourceEnd] == target[targetEnd])
                    {
                        sourceEnd++;
                        targetEnd++;
                    }

                    AppendCopy(operations, copySourceStart, targetEnd - copyTargetStart);
                    targetPosition = targetEnd;
                    pendingStart = targetEnd;
                    if (targetPosition <= target.Length - MatchBlockSize)
                    {
                        targetHash = RollingHash(target, targetPosition, MatchBlockSize);
                    }
                    continue;
                }

                if (targetPosition == target.Length - MatchBlockSize)
                {
                    break;
                }

                targetHash = RollHash(
                    targetHash,
                    target[targetPosition],
                    target[targetPosition + MatchBlockSize],
                    highestPower);
                targetPosition++;
            }

            AppendAdd(operations, target, pendingStart, target.Length - pendingStart);
            return operations;
        }

        private static List<DeltaOperation> BuildAlignedOperations(byte[] source, byte[] target)
        {
            List<DeltaOperation> operations = new List<DeltaOperation>();
            int position = 0;
            while (position < target.Length)
            {
                int start = position;
                bool equal = source[position] == target[position];
                position++;
                while (position < target.Length && (source[position] == target[position]) == equal)
                {
                    position++;
                }

                if (equal)
                {
                    AppendCopy(operations, start, position - start);
                }
                else
                {
                    AppendAdd(operations, target, start, position - start);
                }
            }
            return operations;
        }

        private static Dictionary<ulong, int> BuildSourceBlockMap(byte[] source)
        {
            Dictionary<ulong, int> result = new Dictionary<ulong, int>();
            for (int position = 0; position <= source.Length - MatchBlockSize; position += SourceSampleStep)
            {
                ulong hash = RollingHash(source, position, MatchBlockSize);
                if (!result.ContainsKey(hash))
                {
                    result[hash] = position;
                }
            }
            return result;
        }

        private static ulong RollingHash(byte[] data, int offset, int length)
        {
            ulong hash = 0;
            int end = offset + length;
            for (int index = offset; index < end; index++)
            {
                hash = unchecked(hash * RollingBase + data[index]);
            }
            return hash;
        }

        private static ulong RollHash(ulong hash, byte outgoing, byte incoming, ulong highestPower)
        {
            return unchecked((hash - outgoing * highestPower) * RollingBase + incoming);
        }

        private static void AppendCopy(List<DeltaOperation> operations, int sourceOffset, int length)
        {
            if (length <= 0)
            {
                return;
            }
            if (operations.Count > 0)
            {
                DeltaOperation previous = operations[operations.Count - 1];
                if (previous.Kind == 0 && previous.SourceOffset + previous.Length == sourceOffset)
                {
                    previous.Length += length;
                    return;
                }
            }
            operations.Add(DeltaOperation.Copy(sourceOffset, length));
        }

        private static void AppendAdd(List<DeltaOperation> operations, byte[] data, int offset, int length)
        {
            if (length <= 0)
            {
                return;
            }
            byte[] added = new byte[length];
            Buffer.BlockCopy(data, offset, added, 0, length);
            operations.Add(DeltaOperation.Add(added));
        }

        private static bool BytesEqual(byte[] left, int leftOffset, byte[] right, int rightOffset, int length)
        {
            for (int index = 0; index < length; index++)
            {
                if (left[leftOffset + index] != right[rightOffset + index])
                {
                    return false;
                }
            }
            return true;
        }

        private static byte[] ComputeSha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return sha.ComputeHash(data);
            }
        }

        private sealed class DeltaOperation
        {
            public byte Kind { get; private set; }
            public long SourceOffset { get; private set; }
            public int Length { get; set; }
            public byte[] Data { get; set; }

            public static DeltaOperation Copy(long sourceOffset, int length)
            {
                DeltaOperation operation = new DeltaOperation();
                operation.Kind = 0;
                operation.SourceOffset = sourceOffset;
                operation.Length = length;
                return operation;
            }

            public static DeltaOperation Add(byte[] data)
            {
                DeltaOperation operation = new DeltaOperation();
                operation.Kind = 1;
                operation.Data = data;
                operation.Length = data.Length;
                return operation;
            }
        }
    }
}
