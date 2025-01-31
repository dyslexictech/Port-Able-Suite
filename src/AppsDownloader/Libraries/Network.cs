﻿namespace AppsDownloader.Libraries
{
    using System.Windows.Forms;
    using LangResources;
    using SilDev;
    using SilDev.Forms;

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
                if (_internetIsAvailable == true)
                    MessageBoxEx.Show(Language.GetText(nameof(en_US.InternetProtocolWarningMsg)), Settings.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return (bool)_internetIsAvailable;
            }
        }
    }
}
