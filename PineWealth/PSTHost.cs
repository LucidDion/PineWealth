using System.Diagnostics;
using System.IO;
using WealthLab.Core;
using WealthLab.Data;

namespace PineWealth
{
    //implement an IHost for the WL8 framework
    public class PSTHost : IHost
    {
        public PSTHost()
        {
            _sm = new SettingsManager(DataFolder);
        }

        public string AppFolder => Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName) + "\\";

        public string DataFolder => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\" + "WealthLab8\\";

        public string LocalUserName => "PST";

        public bool ExpertMode => false;

        public bool OfflineMode => false;

        public bool IsWebSite => false;

        public string PlatformToken => "WPF";

        public int WLBuildNumber => 113;

        public Version WLBuildVersion => new Version(8, 0, 113);

        public List<string> EventsSelected => new List<string>();

        public SettingsManager Settings => _sm;

        public List<DataSet> DataSets => new List<DataSet>();

        public void AddLogItem(string senderName, string msg, WLColor color, Exception ex = null, object sender = null)
        {
            EventRouter.FireEvent("AddLogItem", msg);
        }

        public void AddLogItemOrders(string senderName, string msg, WLColor color, Exception ex = null)
        {
        }

        public bool CanExtensionBeLoaded(string productCode, ExtensionLicenseType elt)
        {
            return true;
        }

        public bool ConfigureItem(Configurable c)
        {
            return false;
        }

        public bool ConfigureParameters(ParameterList pl, string title)
        {
            return false;
        }

        public void CopyFileToDataFolder(string sourceSubDir, string destSubDir, string fileName, bool alwaysOverwrite, bool reportErrors)
        {
        }

        public void DisableOfflineMode()
        {
        }

        public void DisplayMessage(string msg)
        {
            EventRouter.FireEvent("AddLogItem", msg);
        }

        public object ExecutePlatformMethod(string callerName, string methodName, object parameter)
        {
            return null;
        }

        public DataSet FindDataSet(string name)
        {
            return null;
        }

        public MarketDetails FindMarket(string symbol)
        {
            return null;
        }

        public Tick GetCurrentQuote(MarketDetails mkt, string symbol, DataSet ds)
        {
            return null;
        }

        public List<BarHistory> GetHistories(List<string> symbols, HistoryScale scale, DateTime start, DateTime end, int maxBars, DataRequestOptions hcb)
        {
            return null;
        }

        public BarHistory GetHistory(string symbol, HistoryScale scale, DateTime startDate, DateTime endDate, int maxBars, DataRequestOptions cb)
        {
            return null;
        }

        public BarData GetPartialBar(string symbol, HistoryScale scale, DataSet ds)
        {
            return null;
        }

        public void InjectSourceCodeCompilerReferences(UniqueList<string> asmRef)
        {
        }

        public string PerformOAuth(string authUrl, string clientName, string matchUrl = "http://localhost")
        {
            return null;
        }

        public void ShowWaitStatus(bool wait)
        {
        }

        //private members
        private SettingsManager _sm;
    }
}