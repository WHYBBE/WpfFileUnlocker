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

        public LockInfo WithLockedFile(string lockedFile) => new()
        {
            ProcessId = ProcessId,
            AppName = AppName,
            ProcessName = ProcessName,
            ExePath = ExePath,
            LockedFile = lockedFile
        };
    }

    public static List<LockInfo> GetLockingProcesses(string path)
    {
        var results = new Dictionary<int, LockInfo>();
        int selfPid = Environment.ProcessId;

        try
        {
            var rmLocks = QueryRM([path]);
            foreach (var l in rmLocks)
                if (l.ProcessId != selfPid)
                    results[l.ProcessId] = l;
        }
        catch { }

        try
        {
            var pids = QueryFilePids(path);
            foreach (var pid in pids)
            {
                if (pid == selfPid || results.ContainsKey(pid)) continue;
                results[pid] = BuildLockInfo(pid, "", path);
            }
        }
        catch { }

        return results.Values.ToList();
    }

    public static List<LockInfo> GetLockingProcessesForFolder(string folderPath)
    {
        var allLocks = new Dictionary<int, LockInfo>();
        int selfPid = Environment.ProcessId;

        try
        {
            var rmLocks = QueryRM([folderPath]);
            foreach (var l in rmLocks)
                if (l.ProcessId != selfPid)
                    allLocks[l.ProcessId] = l;
        }
        catch { }

        try
        {
            var files = Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly).Take(500).ToArray();
            const int batchSize = 100;
            for (int i = 0; i < files.Length; i += batchSize)
            {
                var batch = files[i..Math.Min(i + batchSize, files.Length)];
                var batchLocks = QueryRM(batch);
                foreach (var l in batchLocks)
                    if (l.ProcessId != selfPid && !allLocks.ContainsKey(l.ProcessId))
                        allLocks[l.ProcessId] = l;
            }
        }
        catch { }

        try
        {
            var pids = QueryFilePids(folderPath);
            foreach (var pid in pids)
            {
                if (pid == selfPid || allLocks.ContainsKey(pid)) continue;
                allLocks[pid] = BuildLockInfo(pid, "", folderPath);
            }
        }
        catch { }

        return allLocks.Values.ToList();
    }

    private static List<LockInfo> QueryRM(string[] paths)
    {
        var result = new List<LockInfo>();

        int res = RmStartSession(out int sessionHandle, 0, Guid.NewGuid().ToString());
        if (res != 0) return result;

        try
        {
            res = RmRegisterResources(sessionHandle, (uint)paths.Length, paths, 0, IntPtr.Zero, 0, IntPtr.Zero);
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
                {
                    for (int i = 0; i < (int)pnProcInfo; i++)
                    {
                        var pi = processInfo[i];
                        result.Add(BuildLockInfo(pi.Process.dwProcessId, pi.strAppName, paths.Length == 1 ? paths[0] : ""));
                    }
                }
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

    private static LockInfo BuildLockInfo(int pid, string appName, string lockedFile)
    {
        string procName;
        string exePath;
        string displayName;
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

        var effectiveName = !string.IsNullOrWhiteSpace(displayName) ? displayName
            : !string.IsNullOrWhiteSpace(appName) ? appName
            : procName;

        return new LockInfo
        {
            ProcessId = pid,
            AppName = effectiveName,
            ProcessName = procName,
            ExePath = exePath,
            LockedFile = lockedFile
        };
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

    private static class Imports
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref uint lpdwSize);
    }
}
