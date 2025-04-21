using System.CodeDom.Compiler;
using System.Windows;
using WealthLab.Backtest;
using WealthLab.Core;

namespace PineWealth
{
    public partial class MainWindow : Window
    {
        //constructor
        public MainWindow()
        {
            InitializeComponent();
        }

        //window loaded
        private void onLoad(object sender, RoutedEventArgs e)
        {
            //create host
            WLHost.Instance = new PSTHost();

            //load default PineScript into editor
            txtPineScript.Text = Properties.Resources.PineScript;
        }

        //perform transdlation
        private void btnTranslateClick(object sender, RoutedEventArgs e)
        {
            txtStatus.Clear();
            PineScriptTranslator pst = new PineScriptTranslator();
            try
            {
                txtCSharp.Text = pst.Translate(txtPineScript.Text, Properties.Resources.PineScriptCSharp);
                txtStatus.AppendText("Translation succeeded." + Environment.NewLine);

                //attempt to compile the strategy code
                Strategy s = new Strategy(StrategyType.Code);
                s.StrategyData = txtCSharp.Text;
                UserStrategyBase usb = s.CreateNewInstance();
                if (usb != null)
                    txtStatus.AppendText("Strategy code compiled OK." + Environment.NewLine);

                //log errors
                if (s.CompilerErrors.Count > 0)
                {
                    foreach (CompilerError ce in s.CompilerErrors)
                        txtStatus.AppendText(ce.ErrorText + Environment.NewLine);
                }
            }
            catch(Exception ex)
            {
                txtStatus.AppendText(ex.Message + Environment.NewLine);
                txtStatus.AppendText(ex.StackTrace);
            }
        }
    }
}