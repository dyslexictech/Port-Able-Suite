﻿namespace AppsDownloader.Libraries
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Windows.Forms;
    using LangResources;
    using SilDev;
    using SilDev.Forms;
    using SilDev.Investment;

    public class AppTransferor
    {
        public AppTransferor(AppData appData)
        {
            AppData = appData;
            DestPath = default(string);
            SrcData = new List<Tuple<string, string, string, bool>>();
            UserData = Tuple.Create(default(string), default(string));

            var downloadCollection = AppData.DownloadCollection;
            var packageVersion = default(string);
            if (ActionGuid.IsUpdateInstance && AppData?.UpdateCollection?.SelectMany(x => x.Value).All(x => x?.Item1?.StartsWithEx("http") == true) == true)
            {
                var appIniDir = Path.Combine(appData.InstallDir, "App", "AppInfo");
                var appIniPath = Path.Combine(appIniDir, "appinfo.ini");
                if (!File.Exists(appIniPath))
                    appIniPath = Path.Combine(appIniDir, "plugininstaller.ini");
                packageVersion = Ini.Read("Version", nameof(appData.PackageVersion), default(string), appIniPath);
                if (!string.IsNullOrEmpty(packageVersion) && AppData?.UpdateCollection?.ContainsKey(packageVersion) == true)
                    downloadCollection = AppData.UpdateCollection;
            }

            foreach (var pair in downloadCollection)
            {
                if (!pair.Key.EqualsEx(AppData.Settings.ArchiveLang) && (string.IsNullOrEmpty(packageVersion) || !pair.Key.EqualsEx(packageVersion)))
                    continue;

                foreach (var tuple in pair.Value)
                {
                    if (DestPath == default(string))
                    {
                        if (!DirectoryEx.Create(Settings.TransferDir))
                            continue;
                        var fileName = Path.GetFileName(tuple.Item1);
                        if (string.IsNullOrEmpty(fileName))
                            continue;
                        DestPath = PathEx.Combine(Settings.TransferDir, fileName);
                    }

                    var shortHost = NetEx.GetShortHost(tuple.Item1);
                    var redirect = Settings.ForceTransferRedirection || !NetEx.IPv4IsAvalaible && !string.IsNullOrWhiteSpace(shortHost) && !shortHost.EqualsEx(AppSupplierHosts.Internal);
                    string userAgent;
                    List<string> mirrors;
                    switch (shortHost)
                    {
                        case AppSupplierHosts.Internal:
                            userAgent = UserAgents.Internal;
                            mirrors = AppSupply.GetMirrors(AppSuppliers.Internal);
                            break;
                        case AppSupplierHosts.PortableApps:
                            userAgent = UserAgents.Empty;
                            mirrors = AppSupply.GetMirrors(AppSuppliers.PortableApps);
                            break;
                        case AppSupplierHosts.SourceForge:
                            userAgent = UserAgents.Default;
                            mirrors = AppSupply.GetMirrors(AppSuppliers.SourceForge);
                            break;
                        default:
                            userAgent = UserAgents.Default;
                            var srcUrl = tuple.Item1;
                            if (AppData.ServerKey != default(byte[]))
                                foreach (var srv in Shareware.GetAddresses())
                                {
                                    if (Shareware.FindAddressKey(srv) != AppData.ServerKey.Encode(BinaryToTextEncodings.Base85))
                                        continue;
                                    if (!srcUrl.StartsWithEx("http://", "https://"))
                                        srcUrl = PathEx.AltCombine(srv, srcUrl);
                                    UserData = Tuple.Create(Shareware.GetUser(srv), Shareware.GetPassword(srv));
                                    break;
                                }
                            SrcData.Add(Tuple.Create(srcUrl, tuple.Item2, userAgent, false));
                            continue;
                    }

                    var sHost = NetEx.GetShortHost(tuple.Item1);
                    var fhost = tuple.Item1.Substring(0, tuple.Item1.IndexOf(sHost, StringComparison.OrdinalIgnoreCase) + sHost.Length);
                    foreach (var mirror in mirrors)
                    {
                        var srcUrl = tuple.Item1;
                        if (!fhost.EqualsEx(mirror))
                            srcUrl = tuple.Item1.Replace(fhost, mirror);
                        if (SrcData.Any(x => x.Item1.EqualsEx(srcUrl)))
                            continue;
                        if (redirect)
                        {
                            userAgent = UserAgents.Internal;
                            srcUrl = CorePaths.RedirectUrl + srcUrl.Encode();
                        }
                        SrcData.Add(Tuple.Create(srcUrl, tuple.Item2, userAgent, false));
                        if (Log.DebugMode > 1)
                            Log.Write($"Transfer: '{srcUrl}' has been added.");
                    }
                }
                break;
            }
            Transfer = new NetEx.AsyncTransfer();
        }

        public AppData AppData { get; }

        public string DestPath { get; }

        public List<Tuple<string, string, string, bool>> SrcData { get; }

        public NetEx.AsyncTransfer Transfer { get; }

        public Tuple<string, string> UserData { get; }

        public bool AutoRetry { get; private set; }

        public bool DownloadStarted { get; private set; }

        public bool InstallStarted { get; private set; }

        public void StartDownload(bool force = false)
        {
            DownloadStarted = false;
            if (Transfer.IsBusy)
            {
                if (!force)
                    return;
                Transfer.CancelAsync();
            }
            for (var i = 0; i < SrcData.Count; i++)
            {
                var data = SrcData[i];
                if (data.Item4)
                    continue;
                if (!FileEx.Delete(DestPath))
                    throw new InvalidOperationException();

                SrcData[i] = Tuple.Create(data.Item1, data.Item2, data.Item3, true);
                var userAgent = data.Item3;
                if (!NetEx.FileIsAvailable(data.Item1, UserData.Item1, UserData.Item2, 60000, userAgent))
                {
                    userAgent = UserAgents.WindowsChrome;
                    if (!NetEx.FileIsAvailable(data.Item1, UserData.Item1, UserData.Item2, 60000, userAgent))
                    {
                        if (Log.DebugMode > 0)
                            Log.Write($"Transfer: Could not find target '{data.Item1}'.");
                        continue;
                    }
                }
                if (Log.DebugMode > 0)
                    Log.Write($"Transfer{(!string.IsNullOrEmpty(userAgent) ? $" [{userAgent}]" : string.Empty)}: '{data.Item1}' has been found.");

                Transfer.DownloadFile(data.Item1, DestPath, UserData.Item1, UserData.Item2, true, 60000, userAgent, false);
                DownloadStarted = true;
            }
        }

        public bool StartInstall()
        {
            InstallStarted = false;
            if (Transfer.IsBusy || !File.Exists(DestPath))
                return false;

            const string nonHash = "None";
            var fileHash = default(string);
            foreach (var data in SrcData)
            {
                if (!data.Item4)
                    continue;

                if (fileHash == default(string) || fileHash == nonHash)
                    switch (data.Item2.Length)
                    {
                        case Crypto.Md5.HashLength:
                            fileHash = DestPath.EncryptFile();
                            break;
                        case Crypto.Sha1.HashLength:
                            fileHash = DestPath.EncryptFile(ChecksumAlgorithms.Sha1);
                            break;
                        case Crypto.Sha256.HashLength:
                            fileHash = DestPath.EncryptFile(ChecksumAlgorithms.Sha256);
                            break;
                        case Crypto.Sha384.HashLength:
                            fileHash = DestPath.EncryptFile(ChecksumAlgorithms.Sha384);
                            break;
                        case Crypto.Sha512.HashLength:
                            fileHash = DestPath.EncryptFile(ChecksumAlgorithms.Sha512);
                            break;
                        default:
                            fileHash = nonHash;
                            break;
                    }

                if (fileHash != nonHash && !fileHash.EqualsEx(data.Item2))
                    switch (MessageBoxEx.Show(string.Format(Language.GetText(nameof(en_US.ChecksumErrorMsg)), Path.GetFileName(DestPath)), Settings.Title, MessageBoxButtons.AbortRetryIgnore, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3))
                    {
                        case DialogResult.Ignore:
                            break;
                        case DialogResult.Retry:
                            AutoRetry = true;
                            continue;
                        default:
                            continue;
                    }

                if (Directory.Exists(AppData.InstallDir))
                    if (!BreakFileLocks(AppData.InstallDir, false))
                    {
                        MessageBoxEx.Show(string.Format(Language.GetText(nameof(en_US.InstallSkippedMsg)), AppData.Name), Settings.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        continue;
                    }

                if (DestPath.EndsWithEx(".7z", ".rar", ".zip"))
                {
                    if (!File.Exists(CorePaths.FileArchiver))
                        throw new PathNotFoundException(CorePaths.FileArchiver);
                    using (var process = Compaction.SevenZipHelper.Unzip(DestPath, AppData.InstallDir))
                        if (process?.HasExited == false)
                            process.WaitForExit();
                }
                else
                {
                    var appsDir = CorePaths.AppsDir;
                    using (var process = ProcessEx.Start(DestPath, appsDir, $"/DESTINATION=\"{appsDir}\\\"", Elevation.IsAdministrator, false))
                        if (process?.HasExited == false)
                        {
                            process.WaitForInputIdle(0x1000);
                            try
                            {
                                var buttons = Settings.NsisButtons;
                                if (buttons == null)
                                    throw new NotSupportedException();
                                var okButton = buttons.Take(2).ToArray();
                                var langId = WinApi.NativeHelper.GetUserDefaultUILanguage();
                                var wndState = langId == 1031 || langId == 1033 || langId == 2057 ? WinApi.ShowWindowFlags.ShowMinNoActive : WinApi.ShowWindowFlags.ShowNa;
                                var stopwatch = Stopwatch.StartNew();
                                var minimized = new List<IntPtr>();
                                var counter = new CounterInvestor<int>();
                                while (stopwatch.Elapsed.TotalMinutes < 5d)
                                {
                                    if (process.HasExited)
                                        break;
                                    string title;
                                    using (var proc = Process.GetProcessById(process.Id))
                                        title = proc.MainWindowTitle;
                                    if (string.IsNullOrEmpty(title))
                                        continue;
                                    var parent = WinApi.NativeHelper.FindWindow(null, title);
                                    if (parent == IntPtr.Zero)
                                        continue;
                                    if (!minimized.Contains(parent) && WinApi.NativeHelper.ShowWindowAsync(parent, wndState))
                                        minimized.Add(parent);
                                    foreach (var button in buttons)
                                    {
                                        var child = WinApi.NativeHelper.FindWindowEx(parent, IntPtr.Zero, "Button", button);
                                        if (child == IntPtr.Zero)
                                            continue;
                                        if (counter.Increase(button.GetHashCode()) > 10)
                                        {
                                            if (button.EqualsEx(okButton))
                                                goto Manually;
                                            continue;
                                        }
                                        WinApi.NativeHelper.SendMessage(child, 0xf5u, IntPtr.Zero, IntPtr.Zero);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Write(ex);
                            }
                            Manually:
                            if (!process.HasExited)
                            {
                                try
                                {
                                    using (var proc = Process.GetProcessById(process.Id))
                                    {
                                        var hWnd = WinApi.NativeHelper.FindWindow(null, proc.MainWindowTitle);
                                        WinApi.NativeHelper.ShowWindowAsync(hWnd, WinApi.ShowWindowFlags.ShowNormal);
                                        WinApi.NativeHelper.SetForegroundWindow(hWnd);
                                        WinApi.NativeHelper.SetWindowPos(hWnd, new IntPtr(-1), 0, 0, 0, 0, WinApi.SetWindowPosFlags.NoMove | WinApi.SetWindowPosFlags.NoSize);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log.Write(ex);
                                }
                                process.WaitForExit();
                            }
                        }

                    // fix for messy app installer
                    var retries = 0;
                    retry:
                    try
                    {
                        var appDirs = new[]
                        {
                            Path.Combine(appsDir, "App"),
                            Path.Combine(appsDir, "Data"),
                            Path.Combine(appsDir, "Other")
                        };
                        if (appDirs.Any(Directory.Exists))
                        {
                            if (!Directory.Exists(AppData.InstallDir))
                                Directory.CreateDirectory(AppData.InstallDir);
                            else
                            {
                                BreakFileLocks(AppData.InstallDir);
                                foreach (var dirName in new[]
                                {
                                    "App",
                                    "Other"
                                })
                                {
                                    var dir = Path.Combine(AppData.InstallDir, dirName);
                                    if (!Directory.Exists(dir))
                                        continue;
                                    Directory.Delete(dir, true);
                                }
                                foreach (var file in Directory.EnumerateFiles(AppData.InstallDir, "*.*", SearchOption.TopDirectoryOnly))
                                    File.Delete(file);
                            }
                            foreach (var dir in appDirs)
                            {
                                if (!Directory.Exists(dir))
                                    continue;
                                var dirName = Path.GetFileName(dir);
                                if (string.IsNullOrEmpty(dirName))
                                    continue;
                                BreakFileLocks(dir);
                                if (dirName.EqualsEx("Data"))
                                {
                                    Directory.Delete(dir, true);
                                    continue;
                                }
                                var innerDir = Path.Combine(AppData.InstallDir, dirName);
                                Directory.Move(innerDir, dir);
                            }
                            foreach (var file in Directory.EnumerateFiles(appsDir, "*.*", SearchOption.TopDirectoryOnly))
                            {
                                if (FileEx.IsHidden(file) || file.EndsWithEx(".7z", ".rar", ".zip", ".paf.exe"))
                                    continue;
                                var fileName = Path.GetFileName(file);
                                if (string.IsNullOrEmpty(fileName))
                                    continue;
                                BreakFileLocks(file);
                                var innerFile = Path.Combine(AppData.InstallDir, fileName);
                                File.Move(innerFile, file);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Write(ex);
                        if (retries >= 15)
                            return false;
                        retries++;
                        Thread.Sleep(1000);
                        goto retry;
                    }
                }

                FileEx.TryDelete(DestPath);
                InstallStarted = true;
                return true;
            }
            return false;
        }

        private static bool BreakFileLocks(string path, bool force = true)
        {
            if (!PathEx.DirOrFileExists(path))
                return true;
            var doubleTap = false;
            Check:
            var locks = PathEx.GetLocks(path)?.ToArray();
            if (locks?.Any() != true)
                return true;
            if (doubleTap)
            {
                ProcessEx.Terminate(locks);
                return true;
            }
            if (!force)
            {
                var lockData = locks.Select(p => $"ID: {p.Id:d5}; Name: '{p.ProcessName}.exe'").ToArray();
                var information = string.Format(Language.GetText(lockData.Length == 1 ? nameof(en_US.FileLockMsg) : nameof(en_US.FileLocksMsg)), lockData.Join(Environment.NewLine));
                if (MessageBoxEx.Show(information, Settings.Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                    return false;
            }
            foreach (var process in locks)
            {
                if (process.ProcessName.EndsWithEx("64Portable", "Portable64", "Portable"))
                    continue;
                ProcessEx.Close(process);
            }
            Thread.Sleep(400);
            doubleTap = true;
            goto Check;
        }
    }
}
