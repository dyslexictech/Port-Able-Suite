﻿namespace Updater.Libraries
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using LangResources;
    using SilDev;

    internal static class Network
    {
        private static bool? _internetIsAvailable;

        internal static bool InternetIsAvailable
        {
            get
            {
                if (!_internetIsAvailable.HasValue)
                    _internetIsAvailable = NetEx.IPv4IsAvalaible;
                if (_internetIsAvailable == true)
                    return (bool)_internetIsAvailable;
                _internetIsAvailable = NetEx.IPv6IsAvalaible;
                return (bool)_internetIsAvailable;
            }
        }

        internal static void DownloadArchiver()
        {
            var notifyBox = new NotifyBox();
            notifyBox.Show(Language.GetText(nameof(en_US.InitRequirementsNotify)), Settings.Title, NotifyBoxStartPosition.Center);
            var mirrors = NetEx.InternalDownloadMirrors;
            var verMap = new Dictionary<string, string>();
            foreach (var mirror in mirrors)
            {
                try
                {
                    var url = PathEx.AltCombine(mirror, ".redists", "Extra", "Last.ini");
                    if (!NetEx.FileIsAvailable(url, 30000))
                        throw new PathNotFoundException(url);
                    var data = NetEx.Transfer.DownloadString(url);
                    if (string.IsNullOrWhiteSpace(data))
                        throw new ArgumentNullException(nameof(data));
                    const string name = "7z";
                    if (string.IsNullOrEmpty(name))
                        continue;
                    var version = Ini.ReadOnly(name, "Version", data);
                    if (string.IsNullOrWhiteSpace(version))
                        continue;
                    verMap.Add(name, version);
                }
                catch (Exception ex)
                {
                    Log.Write(ex);
                }
                if (verMap.Count > 0)
                    break;
            }
            foreach (var map in verMap)
            {
                var file = $"{map.Value}.zip";
                var path = PathEx.Combine(CachePaths.UpdateDir, file);
                foreach (var mirror in mirrors)
                {
                    try
                    {
                        var url = PathEx.AltCombine(mirror, ".redists", "Extra", map.Key, file);
                        if (!NetEx.FileIsAvailable(url, 30000))
                            throw new PathNotFoundException(url);
                        NetEx.Transfer.DownloadFile(url, path);
                        Compaction.Unzip(path, CachePaths.UpdateDir);
                    }
                    catch (Exception ex)
                    {
                        Log.Write(ex);
                    }
                    if (File.Exists(path))
                        break;
                }
            }
            notifyBox.Close();
        }
    }
}
