using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FileUnlocker;

internal static class RestartManager
{
    private const int ERROR_MORE_DATA = 234;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    #region Restart Manager

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string strServiceShortName;
        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out int pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(int pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(int pSessionHandle, uint nFiles, string[] rgsFileNames, uint nApplications, IntPtr rgApplications, uint nServices, IntPtr rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(int dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo, [In, Out] RM_PROCESS_INFO[] rgAffectedApps, ref uint lpdwRebootReasons);

    #endregion

    #region NtQueryInformationFile

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_STATUS_BLOCK
    {
        public IntPtr Status;
        public IntPtr Information;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationFile(IntPtr FileHandle, out IO_STATUS_BLOCK IoStatusBlock, IntPtr FileInformation, uint Length, int FileInformationClass);

    private const int FileProcessIdsUsingFileInformation = 47;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    private const uint FILE_READ_ATTRIBUTES = 0x80;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    #endregion

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    public sealed class LockInfo
    {
        public int ProcessId { get; init; }
        public string AppName { get; init; } = "";
        public string ProcessName { get; init; } = "";
        public string ExePath { get; init; } = "";
        public string LockedFile { get; init; } = "";
    }

    public static List<LockInfo> GetLockingProcesses(string path)
    {
        var pidFiles = new ConcurrentDictionary<int, ConcurrentBag<string>>();
        int selfPid = Environment.ProcessId;

        foreach (var pid in QueryFilePids(path))
            if (pid != selfPid)
                pidFiles.GetOrAdd(pid, _ => []).Add(path);

        foreach (var kv in QueryRMSingle(path))
            if (kv.Key != selfPid && !pidFiles.ContainsKey(kv.Key))
                pidFiles.GetOrAdd(kv.Key, _ => []).Add(path);

        return BuildResults(pidFiles);
    }

    public enum ScanDepth
    {
        CurrentOnly,
        OneLevel,
        Recursive
    }

    public static List<LockInfo> GetLockingProcessesForFolder(string folderPath, ScanDepth scanDepth, bool ignoreGit)
    {
        var pidFiles = new ConcurrentDictionary<int, ConcurrentBag<string>>();
        int selfPid = Environment.ProcessId;

        var allPaths = new List<string> { folderPath };

        if (scanDepth == ScanDepth.CurrentOnly)
        {
            // 只检测文件夹自身
        }
        else if (scanDepth == ScanDepth.OneLevel)
        {
            // 当前目录下的文件和子目录（不进入子目录内部）
            try
            {
                var files = Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly);
                if (ignoreGit) files = files.Where(f => !IsUnderGit(f));
                allPaths.AddRange(files.Take(2000));
            }
            catch { }

            try
            {
                var dirs = Directory.EnumerateDirectories(folderPath, "*", SearchOption.TopDirectoryOnly);
                if (ignoreGit) dirs = dirs.Where(d => !d.EndsWith(".git", StringComparison.OrdinalIgnoreCase));
                allPaths.AddRange(dirs.Take(500));
            }
            catch { }
        }
        else
        {
            try
            {
                var dirs = Directory.EnumerateDirectories(folderPath, "*", SearchOption.AllDirectories);
                if (ignoreGit) dirs = dirs.Where(d => !IsUnderGit(d));
                allPaths.AddRange(dirs.Take(500));
            }
            catch { }

            try
            {
                var files = Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories);
                if (ignoreGit) files = files.Where(f => !IsUnderGit(f));
                allPaths.AddRange(files.Take(2000));
            }
            catch { }
        }

        Parallel.ForEach(allPaths, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            path =>
            {
                try
                {
                    foreach (var pid in QueryFilePids(path))
                        if (pid != selfPid)
                            pidFiles.GetOrAdd(pid, _ => []).Add(path);
                }
                catch { }
            });

        try
        {
            foreach (var kv in QueryRMSingle(folderPath))
                if (kv.Key != selfPid && !pidFiles.ContainsKey(kv.Key))
                    pidFiles.GetOrAdd(kv.Key, _ => []).Add(folderPath);
        }
        catch { }

        return BuildResults(pidFiles);
    }

    private static Dictionary<int, string> QueryRMSingle(string path)
    {
        var result = new Dictionary<int, string>();

        int res = RmStartSession(out int sessionHandle, 0, Guid.NewGuid().ToString());
        if (res != 0) return result;

        try
        {
            res = RmRegisterResources(sessionHandle, 1, [path], 0, IntPtr.Zero, 0, IntPtr.Zero);
            if (res != 0) return result;

            uint pnProcInfo = 0;
            uint lpdwRebootReasons = 0;

            res = RmGetList(sessionHandle, out uint pnProcInfoNeeded, ref pnProcInfo, null!, ref lpdwRebootReasons);

            if (res == ERROR_MORE_DATA && pnProcInfoNeeded > 0)
            {
                var processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
                pnProcInfo = pnProcInfoNeeded;
                res = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, ref lpdwRebootReasons);

                if (res == 0)
                    for (int i = 0; i < (int)pnProcInfo; i++)
                        result[processInfo[i].Process.dwProcessId] = processInfo[i].strAppName;
            }
        }
        finally
        {
            try { RmEndSession(sessionHandle); } catch { }
        }

        return result;
    }

    private static List<int> QueryFilePids(string path)
    {
        var result = new List<int>();

        uint flags = Directory.Exists(path) ? FILE_FLAG_BACKUP_SEMANTICS : 0;

        IntPtr hFile = CreateFileW(
            path,
            FILE_READ_ATTRIBUTES,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero,
            OPEN_EXISTING,
            flags,
            IntPtr.Zero);

        if (hFile == INVALID_HANDLE_VALUE) return result;

        try
        {
            uint bufferSize = 4096;
            IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
            try
            {
                int status = NtQueryInformationFile(hFile, out _, buffer, bufferSize, FileProcessIdsUsingFileInformation);
                if (status != 0) return result;

                int count = Marshal.ReadInt32(buffer);
                int listOffset = IntPtr.Size;

                for (int i = 0; i < count; i++)
                {
                    long pid = IntPtr.Size == 8
                        ? Marshal.ReadInt64(buffer, listOffset + i * 8)
                        : Marshal.ReadInt32(buffer, listOffset + i * 4);

                    if (pid > 0 && pid <= int.MaxValue)
                        result.Add((int)pid);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(hFile);
        }

        return result;
    }

    private static List<LockInfo> BuildResults(ConcurrentDictionary<int, ConcurrentBag<string>> pidFiles)
    {
        var results = new List<LockInfo>();

        foreach (var kv in pidFiles)
        {
            int pid = kv.Key;
            var files = kv.Value.Distinct().ToList();

            string procName, exePath, displayName;
            try
            {
                using var proc = Process.GetProcessById(pid);
                procName = proc.ProcessName;
                exePath = TryGetProcessPath(proc);
                displayName = GetFileDescription(exePath);
            }
            catch
            {
                procName = $"<PID:{pid}>";
                exePath = "";
                displayName = "";
            }

            var effectiveName = !string.IsNullOrWhiteSpace(displayName) ? displayName : procName;

            var lockedFile = files.Count <= 3
                ? string.Join("\n", files)
                : string.Join("\n", files.Take(3)) + $"\n...等{files.Count}个文件";

            results.Add(new LockInfo
            {
                ProcessId = pid,
                AppName = effectiveName,
                ProcessName = procName,
                ExePath = exePath,
                LockedFile = lockedFile
            });
        }

        return results;
    }

    private static string GetFileDescription(string exePath)
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return "";
        try
        {
            return FileVersionInfo.GetVersionInfo(exePath).FileDescription ?? "";
        }
        catch { return ""; }
    }

    private static string TryGetProcessPath(Process proc)
    {
        try
        {
            return proc.MainModule?.FileName ?? "";
        }
        catch
        {
            try
            {
                var sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                if (Imports.QueryFullProcessImageName(proc.Handle, 0, sb, ref size))
                    return sb.ToString();
            }
            catch { }
            return "";
        }
    }

        private static bool IsUnderGit(string path)
        {
            var span = path.AsSpan();
            for (int i = 0; i < span.Length - 4; i++)
            {
                if ((span[i] == '\\' || span[i] == '/')
                    && span.Slice(i + 1, 4).Equals(".git", StringComparison.OrdinalIgnoreCase)
                    && (i + 5 >= span.Length || span[i + 5] == '\\' || span[i + 5] == '/'))
                    return true;
            }
            return false;
        }

        private static class Imports
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref uint lpdwSize);
    }
}
