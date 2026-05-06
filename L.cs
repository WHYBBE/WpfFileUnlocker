namespace FileUnlocker;

internal static class L
{
    private static bool IsEn => Settings.Lang == Settings.Language.En;

    public static string AppTitle => IsEn ? "FileUnlocker - File Lock Detector" : "FileUnlocker - 文件占用检测";
    public static string BrowseFile => IsEn ? "Select File" : "选择文件";
    public static string BrowseFolder => IsEn ? "Select Folder" : "选择文件夹";
    public static string Detect => IsEn ? "Detect" : "检测";
    public static string DragHint => IsEn ? "Drag & drop file/folder here, or enter path and click Detect" : "拖拽文件或文件夹到此处，或输入路径后点击检测";
    public static string EnterPath => IsEn ? "Please enter or select a file/folder path" : "请输入或选择文件/文件夹路径";
    public static string PathNotExist => IsEn ? "Path does not exist: " : "路径不存在: ";
    public static string Detecting => IsEn ? "Detecting..." : "正在检测...";
    public static string DetectingStatus => IsEn ? "Detecting..." : "检测中...";
    public static string NotOccupiedFile => IsEn ? "This file is not locked by any process" : "该文件未被任何进程占用";
    public static string NotOccupiedFolder => IsEn ? "This folder is not locked by any process" : "该文件夹未被任何进程占用";
    public static string Idle => IsEn ? "Idle, no locks" : "空闲，无占用";
    public static string FoundLocks(int count) => IsEn ? $"Found {count} process(es) locking this" : $"发现 {count} 个进程占用该";
    public static string TotalLocks(int count) => IsEn ? $"{count} process(es) locking" : $"共 {count} 个进程占用";
    public static string DetectError(string msg) => IsEn ? $"Detection error: {msg}" : $"检测出错: {msg}";
    public static string DetectFailed => IsEn ? "Detection failed" : "检测失败";
    public static string File => IsEn ? "file" : "文件";
    public static string Folder => IsEn ? "folder" : "文件夹";
    public static string Ready => IsEn ? "Ready" : "就绪";
    public static string PathNotExistShort => IsEn ? "Path not found" : "路径不存在";
    public static string SelectFileTitle => IsEn ? "Select file to detect" : "选择要检测的文件";
    public static string SelectFolderTitle => IsEn ? "Select folder to detect" : "选择要检测的文件夹";
    public static string KilledProcess(string name, int pid) => IsEn ? $"Killed process {name} (PID: {pid})" : $"已结束进程 {name} (PID: {pid})";
    public static string KillFailed(string msg) => IsEn ? $"Failed to kill process: {msg}" : $"结束进程失败: {msg}";
    public static string SelfProcess => IsEn ? " (Current)" : " (当前进程)";
    public static string MoreFiles(int count) => IsEn ? $"\n...and {count} more" : $"\n...等{count}个文件";

    public static string ScanDepthLabel => IsEn ? "Folder scan depth: " : "文件夹扫描深度：";
    public static string ScanCurrent => IsEn ? "Current only" : "仅当前目录";
    public static string ScanOneLevel => IsEn ? "One level deep" : "含一层子目录";
    public static string ScanRecursive => IsEn ? "Recursive" : "递归所有子目录";
    public static string SkipGit => IsEn ? "Skip .git dirs" : "跳过 .git 目录";
    public static string Language => IsEn ? "Language" : "语言";

    public static string ColPID => IsEn ? "PID" : "PID";
    public static string ColProcess => IsEn ? "Process" : "进程名";
    public static string ColApp => IsEn ? "Application" : "应用名";
    public static string ColLockedFile => IsEn ? "Locked File" : "占用文件";
    public static string ColAction => IsEn ? "Action" : "操作";
    public static string Kill => IsEn ? "Kill" : "结束";
}
