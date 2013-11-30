using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using BitcoinCharts;
using BtcE;
using BtcE.Utils;
using easybot.Properties;
using TicTacTec.TA.Library;

namespace easybot
{
    public partial class Main : Form
    {
        private string key = string.Empty;
        private string secret = string.Empty;
        private string localTimezone = string.Empty;
        private string btceTimezone = string.Empty;

        private Task task;
        private IList<OHLC> realtimeCandles = new List<OHLC>();
        private IList<OHLC> historyCandles = new List<OHLC>();
        private TimeSpan providerTimeOffset;

        private DateTime lastUpdate = DateTime.MinValue;
        private BtcePair pair = BtcePair.Unknown;
        private decimal fee;
        private UserInfo info;
        private Ticker ticker;
        private SimulationResult lastSimulation;

        public Main()
        {
            InitializeComponent();

            cbSlowMAType.SelectedIndex = 0;
            cbFastMAType.SelectedIndex = 0;
            cbSlowTAType.SelectedIndex = 0;
            cbFastTAType.SelectedIndex = 0;

            NameValueCollection section = (NameValueCollection)ConfigurationManager.GetSection("easybot");
            if (section != null && section.Count > 0)
            {
                key = section["btce.key"];
                secret = section["btce.secret"];
                localTimezone = section["local.timezone"];
                btceTimezone = section["btce.timezone"];
            }

            var local = TimeZoneInfo.FindSystemTimeZoneById(localTimezone);
            var provider = TimeZoneInfo.FindSystemTimeZoneById(btceTimezone);
            if (local != null && provider != null)
            {
                var now = DateTimeOffset.UtcNow;
                TimeSpan localOffset = local.GetUtcOffset(now);
                TimeSpan providerOffset = provider.GetUtcOffset(now);
                providerTimeOffset = localOffset - providerOffset;
            }

            var pairs = Enum.GetNames(typeof(BtcePair)).Select(x => x.Replace("_", "/").ToUpperInvariant()).ToList();
            cbPairs.DataSource = pairs;
            cbPairs.SelectedIndex = 2;

            // Load image lists
            imageList.Images.Add(Resources.information);
            imageList.Images.Add(Resources.warning1);
            imageList.Images.Add(Resources.delete);

            //Task updateTask = Task.Factory.StartNew(() => { Update(chkRealtimeCandles.Checked); }, new CancellationToken());
            //task = updateTask.ContinueWith(_ => { if (realtimeCandles.Count > 0) DrawChart(realtimeCandles); }, new CancellationToken(), TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
        }

        #region Events

        private Task loadHistoricalCandles;
        private CancellationTokenSource ctsh = null;
        private void OnBitcoinChartsClick(object sender, EventArgs e)
        {
            if (loadHistoricalCandles == null || loadHistoricalCandles.Status == TaskStatus.RanToCompletion ||
                loadHistoricalCandles.Status == TaskStatus.Faulted || loadHistoricalCandles.Status == TaskStatus.Canceled)
            {
                DialogResult result = MessageBox.Show("This operation can take a minute or two.. Do you really want to continue?\n\nPlease also note that connecting to bitcoincharts.com more than once every 15 minutes will cause your IP to get banned.", "Connect Bitcoincharts.com?", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                {
                    ctsh = new CancellationTokenSource();
                    loadHistoricalCandles = Task.Factory.StartNew(InitFromBitcoincharts, ctsh.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    btnBitcoinCharts.Text = "Cancel load candles from BitcoinCharts";
                }
            }
            else
            {
                if (ctsh != null && loadHistoricalCandles.Status != TaskStatus.RanToCompletion)
                    ctsh.Cancel();

                loadHistoricalCandles.Wait();
                loadHistoricalCandles = null;
                btnBitcoinCharts.Text = "Load candles from BitcoinCharts";
            }
        }

        private void OnLastFetchedTradesClick(object sender, EventArgs e)
        {
            if (pair == BtcePair.Unknown)
                InitFromCSV("history.csv");
            else InitFromCSV(string.Format("history{0}.csv", pair.ToString()));
        }

        private void OnHistorichalTradesClick(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog1 = new OpenFileDialog();

            openFileDialog1.Filter = "csv files (*.csv)|*.csv|All files (*.*)|*.*";
            openFileDialog1.FilterIndex = 1;
            openFileDialog1.RestoreDirectory = true;
            openFileDialog1.InitialDirectory = Application.ExecutablePath;

            if (openFileDialog1.ShowDialog() == DialogResult.OK)
                InitFromCSV(openFileDialog1.FileName);
        }

        private void OnHistorichalFromDbClick(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show("This function require some configuration and I left it commented in source code only for developers to experiment... Sorry for the inconvenience", "Function is disabled", MessageBoxButtons.OK, MessageBoxIcon.Stop);
            if (System.Diagnostics.Debugger.IsAttached)
                InitFromDatabase(DateTime.Now.Subtract(new TimeSpan(9, 0, 0, 0)), false, Convert.ToDouble(nupHistoryPeriod.Value));
        }

        private Task loadRealtimeCandles;
        private CancellationTokenSource cts = null;
        private void OnTradeTickerClick(object sender, EventArgs e)
        {
            if (loadRealtimeCandles == null || loadRealtimeCandles.Status == TaskStatus.RanToCompletion || loadRealtimeCandles.Status == TaskStatus.Faulted || loadRealtimeCandles.Status == TaskStatus.Canceled)
            {
                DialogResult result = MessageBox.Show("This operation can take a minute or two.. It first try to recreate the last 24 hours candles and then connect to the latest trades on BTC-e. Do you really want to continue?\n\nPlease also note that connecting to bitcoincharts.com more than once every 15 minutes will cause your IP to get banned.", "Connect Bitcoincharts.com?", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result == System.Windows.Forms.DialogResult.Yes)
                {
                    cts = new CancellationTokenSource();
                    loadRealtimeCandles = Task.Factory.StartNew(OnTraderTick, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    btnTradeTick.Text = "Cancel load candles from BTC-e";
                }
            }
            else
            {
                if (cts != null && loadRealtimeCandles.Status != TaskStatus.RanToCompletion)
                    cts.Cancel();

                loadRealtimeCandles.Wait();
                loadRealtimeCandles = null;
                btnTradeTick.Text = "Load candles from BTC-e";
            }
        }

        private void OnRefresh(object sender, EventArgs e)
        {
            if (loadRealtimeCandles == null || loadRealtimeCandles.Status == TaskStatus.RanToCompletion ||
                loadRealtimeCandles.Status == TaskStatus.Faulted || loadRealtimeCandles.Status == TaskStatus.Canceled)
                btnTradeTick.Text = "Load candles from BTC-e";
            else btnTradeTick.Text = "Cancel load candles from BTC-e";

            if (loadHistoricalCandles == null || loadHistoricalCandles.Status == TaskStatus.RanToCompletion ||
                loadHistoricalCandles.Status == TaskStatus.Faulted || loadHistoricalCandles.Status == TaskStatus.Canceled)
                btnBitcoinCharts.Text = "Load candles from BitcoinCharts";
            else btnBitcoinCharts.Text = "Cancel load candles from BitcoinCharts";

            if (findBestEma == null || findBestEma.Status == TaskStatus.RanToCompletion ||
                findBestEma.Status == TaskStatus.Faulted || findBestEma.Status == TaskStatus.Canceled)
                btnCalculateBestEMA.Text = "Calculate Best EMA Crossing Parameters";
            else btnCalculateBestEMA.Text = "Cancel Calculate Best EMA Parameters";
        }

        private void OnTraderTick()
        {
            if (realtimeCandles.Count == 0)
            {
                UpdateStatus("Initializing realtime candles...");
                realtimeCandles = GetCandlesFromBitcoincharts(DateTime.Now.Subtract(new TimeSpan(24, 0, 0)), Convert.ToDouble(nupRealtimePeriod.Value));
            }

            // Fetch live data from internet
            InitFromBtce(Convert.ToDouble(nupRealtimePeriod.Value));

        }

        private void OnTick(object sender, EventArgs e)
        {
            if (task == null || task.IsCompleted || task.IsFaulted || task.IsCanceled)
            {
                if (DateTime.UtcNow.Subtract(lastUpdate).TotalSeconds >= 5)
                {
                    var updateTask = Task.Factory.StartNew(() => { Update(chkRealtimeCandles.Checked, Convert.ToDouble(nupRealtimePeriod.Value)); });
                    task = updateTask.ContinueWith(_ => { if (realtimeCandles.Count > 0) DrawChart(realtimeCandles); }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
                }
            }
            else if (task != null) lblStatus.Text = lblStatus.Text + ".";

            if (pair != BtcePair.Unknown)
            {
                string currency = pair.ToString().Substring(pair.ToString().IndexOf("_") + 1, 3).ToUpperInvariant();
                string item = pair.ToString().Substring(0, pair.ToString().IndexOf("_")).ToUpperInvariant();
                lblCurrency1.Text = lblCurrency2.Text = currency;

                if (ticker != null)
                {

                    lblBtceTicker.Text = string.Format(CultureInfo.InvariantCulture, "Last Price: {0:0.####} {3} Low: {1:0.####} {3} High: {2:0.####} {3}", ticker.Last, ticker.Low, ticker.High, currency);
                    lblBuy.Text = string.Format(CultureInfo.InvariantCulture, "{0:0.#####} {1}", ticker.Buy, currency);
                    lblSell.Text = string.Format(CultureInfo.InvariantCulture, "{0:0.#####} {1}", ticker.Sell, currency);

                    if (string.IsNullOrEmpty(txtPriceBuy.Text))
                        txtPriceBuy.Text = ticker.Buy.ToString(CultureInfo.InvariantCulture);

                    if (string.IsNullOrEmpty(txtPriceSell.Text))
                        txtPriceSell.Text = ticker.Sell.ToString(CultureInfo.InvariantCulture);
                }

                if (info != null)
                {

                    lblItemBalance.Text = string.Format(CultureInfo.InvariantCulture, "{0:0.#####} {1}", info.Funds.GetBalance(currency), currency);
                    lblCurrencyBalance.Text = string.Format(CultureInfo.InvariantCulture, "{0:0.#####} {1}", info.Funds.GetBalance(item), item);
                }
            }
        }

        private void Update(bool enabled, double period)
        {
            lock (this)
            {
                // Fetch live data from internet
                if (enabled)
                {
                    if (realtimeCandles.Count == 0)
                    {
                        UpdateStatus("Loading last 24 hours candles");
                        try
                        {
                            IList<OHLC> candles = GetCandlesFromBitcoincharts(DateTime.Now.Subtract(new TimeSpan(24, 0, 0)), period);
                            if (candles.Count == 0)
                                candles = ReadCandlesFromCSV(string.Format("realtime{0}.csv", pair.ToString()), DateTime.Now.Subtract(new TimeSpan(24, 0, 0)));

                            foreach (var candle in candles)
                                realtimeCandles.Add(candle);
                        }
                        catch (Exception)
                        {
                            UpdateStatus("Failed to get last 24 hours candles from Bitcoincharts");
                            Thread.Sleep(1000);
                        }
                    }

                    // First load history
                    if (realtimeCandles.Count > 0)
                    {
                        UpdateStatus("Fetching new data from BTC-e");
                        GetCandlesFromBtce(period);
                        SaveCandlesToCSV(realtimeCandles, string.Format("realtime{0}.csv", pair.ToString()));
                    }
                }

                try
                {
                    if (pair != BtcePair.Unknown)
                    {
                        //UpdateStatus("Updating BTC-e ticker and fees");
                        if (fee == 0m) fee = BtceApi.GetFee(pair);
                        ticker = BtceApi.GetTicker(pair);
                    }

                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(secret))
                    {
                        //UpdateStatus("Updating account informations");
                        BtceApi btceApi = new BtceApi(key, secret);
                        info = btceApi.GetInfo();
                    }
                    UpdateStatus("Ready...");
                }
                catch (Exception ex)
                {
                    UpdateStatus(string.Format("Error during Update: {0}", ex.Message));
                }

                //Depth btcusdDepth = BtceApi.GetDepth(BtcePair.btc_eur);
                //var transHistory = btceApi.GetTransHistory();
                //var tradeHistory = btceApi.GetTradeHistory(count: 20);
                //var orderList = btceApi.GetOrderList();
                //var tradeAnswer = btceApi.Trade(BtcePair.btc_eur, TradeType.Sell, 20, 0.1m);
                //var cancelAnswer = btceApi.CancelOrder(tradeAnswer.OrderId);
            }
        }

        private void UpdateStatus(string message)
        {
            if (!this.InvokeRequired)
            {
                lblStatus.Text = message;
            }
            else this.BeginInvoke(new Action<string>(UpdateStatus), new object[] { message });
        }

        private Task findBestEma;
        private CancellationTokenSource ctsEma = null;
        private void OnCalculateBestEMA(object sender, EventArgs e)
        {
            if (findBestEma == null || findBestEma.Status == TaskStatus.RanToCompletion || findBestEma.Status == TaskStatus.Faulted || findBestEma.Status == TaskStatus.Canceled)
            {
                lstMessages.Items.Clear();

                ctsEma = new CancellationTokenSource();
                IList<OHLC> candles = rbRealtimeCalc.Checked ? realtimeCandles : rbHistoricalCalc.Checked ? historyCandles : new List<OHLC>();
                findBestEma = Task.Factory.StartNew(() => CalculateBestEMACrossing(candles), ctsEma.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                btnCalculateBestEMA.Text = "Cancel Calculate Best EMA Parameters";
            }
            else
            {
                if (ctsEma != null && findBestEma.Status != TaskStatus.RanToCompletion)
                    ctsEma.Cancel();
                SetText(MessageType.Warning, "Calculation interrupted!");
                findBestEma = null;
                btnCalculateBestEMA.Text = "Calculate Best EMA Crossing Parameters";
            }
        }

        private void OnSimulateEMA(object sender, EventArgs e)
        {
            if (rbRealtimeSim.Checked)
            {
                lastSimulation = SimulateEMACrossing(realtimeCandles, (int)nupShortEMA.Value, (int)nupLongEMA.Value, (double)nupBuyDiff.Value, (double)nupSellDiff.Value, true);
                if (realtimeCandles.Count > 0) DrawChart(realtimeCandles, "price");
            }
            else if (rbHistoricalSim.Checked)
            {
                lastSimulation = SimulateEMACrossing(historyCandles, (int)nupShortEMA.Value, (int)nupLongEMA.Value, (double)nupBuyDiff.Value, (double)nupSellDiff.Value, true);
                if (historyCandles.Count > 0) DrawChart(historyCandles, "priceHistory");
            }
        }
        #endregion

        #region Chart MA/Forecasting

        private void CalculateAverages(string dataSerieName)
        {
            string[] series;
            string area = dataSerieName == "price" ? "Price" : "History";

            if (area == "Price")
                series = new string[] { "Simple", "SimpleFast", "Exponential", "ExponentialFast", "Triangular", "TriangularFast", "Weighted", "WeightedFast" };
            else series = new string[] { "SimpleH", "SimpleFastH", "ExponentialH", "ExponentialFastH", "TriangularH", "TriangularFastH", "WeightedH", "WeightedFastH" };

            foreach (string serie in series)
            {
                if (chart1.Series.FindByName(serie) != null)
                {
                    if (chart1.Series[serie].ChartArea == area)
                    {
                        chart1.Series[serie].Points.Clear();
                        chart1.Series[serie].Enabled = false;
                        chart1.Series[serie].IsVisibleInLegend = false;
                    }
                }
            }

            // Return if anything to calculate or not enabled
            if (chart1.Series[dataSerieName].Points.Count == 0) return;

            // Start from first property is true by default.
            chart1.DataManipulator.IsStartFromFirst = true;

            // Calculate and draw slow average
            if (chkShowSlowAverage.Checked)
            {
                switch (cbSlowMAType.SelectedIndex)
                {
                    // Calculates simple moving average   
                    case 0:
                        chart1.DataManipulator.FinancialFormula(FinancialFormula.MovingAverage, numSlowMAPeriod.Value.ToString(), dataSerieName, series[0]);
                        chart1.Series[series[0]].ChartArea = area;
                        SetSeriesAppearance(series[0], SeriesChartType.Line);
                        break;

                    // Calculates exponential moving average    
                    case 1:
                        chart1.DataManipulator.FinancialFormula(FinancialFormula.ExponentialMovingAverage, numSlowMAPeriod.Value.ToString(), dataSerieName, series[2]);
                        chart1.Series[series[2]].ChartArea = area;
                        SetSeriesAppearance(series[2], SeriesChartType.Line);
                        break;

                    // Calculates triangular moving average    
                    case 2:
                        chart1.DataManipulator.FinancialFormula(FinancialFormula.TriangularMovingAverage, numSlowMAPeriod.Value.ToString(), dataSerieName, series[4]);
                        chart1.Series[series[4]].ChartArea = area;
                        SetSeriesAppearance(series[4], SeriesChartType.Line);
                        break;

                    // Calculates weighted moving average    
                    case 3:
                        chart1.DataManipulator.FinancialFormula(FinancialFormula.WeightedMovingAverage, numSlowMAPeriod.Value.ToString(), dataSerieName, series[6]);
                        chart1.Series[series[6]].ChartArea = area;
                        SetSeriesAppearance(series[6], SeriesChartType.Line);
                        break;
                }
            }

            // Calculate and draw fast average
            if (chkShowFastAverage.Checked)
            {
                switch (cbFastMAType.SelectedIndex)
                {
                    case 0:
                        chart1.DataManipulator.FinancialFormula(FinancialFormula.MovingAverage, numFastMAPeriod.Value.ToString(), dataSerieName, series[1]);
                        chart1.Series[series[1]].ChartArea = area;
                        SetSeriesAppearance(series[1], SeriesChartType.Line);
                        break;

                    case 1:
                        chart1.DataManipulator.FinancialFormula(FinancialFormula.ExponentialMovingAverage, numFastMAPeriod.Value.ToString(), dataSerieName, series[3]);
                        chart1.Series[series[3]].ChartArea = area;
                        SetSeriesAppearance(series[3], SeriesChartType.Line);
                        break;

                    case 2:
                        chart1.DataManipulator.FinancialFormula(FinancialFormula.TriangularMovingAverage, numFastMAPeriod.Value.ToString(), dataSerieName, series[5]);
                        chart1.Series[series[5]].ChartArea = area;
                        SetSeriesAppearance(series[5], SeriesChartType.Line);
                        break;

                    case 3:
                        chart1.DataManipulator.FinancialFormula(FinancialFormula.WeightedMovingAverage, numFastMAPeriod.Value.ToString(), dataSerieName, series[7]);
                        chart1.Series[series[7]].ChartArea = area;
                        SetSeriesAppearance(series[7], SeriesChartType.Line);
                        break;
                }
            }
        }

        private int CalculateTALib(IList<OHLC> k, string dataSerieName)
        {
            string[] series;
            if (dataSerieName == "price")
                series = new string[] { "TASimple", "TASimpleFast", "TAExponential", "TAExponentialFast" };
            else series = new string[] { "TASimpleH", "TASimpleFastH", "TAExponentialH", "TAExponentialFastH" };

            foreach (string serie in series)
            {
                string area = !serie.EndsWith("H") ? "Price" : "History";
                if (chart1.Series.FindByName(serie) != null)
                {
                    chart1.Series[serie].Points.Clear();
                    chart1.Series[serie].Enabled = false;
                    chart1.Series[serie].IsVisibleInLegend = false;
                }
                else chart1.Series.Add(new Series() { Enabled = false, Name = serie, IsVisibleInLegend = false, IsXValueIndexed = true, ChartArea = area });
            }

            string slowName = string.Format("{0}{1}", cbSlowTAType.SelectedIndex == 0 ? "TASimple" : "TAExponential", (dataSerieName != "price") ? "H" : string.Empty);
            string fastName = string.Format("{0}{1}", cbFastTAType.SelectedIndex == 0 ? "TASimpleFast" : "TAExponentialFast", (dataSerieName != "price") ? "H" : string.Empty);

            if (chkShowSlowTA.Checked)
                SetSeriesAppearance(slowName, SeriesChartType.Line);

            if (chkShowFastTA.Checked)
                SetSeriesAppearance(fastName, SeriesChartType.Line);

            MovingAverage slowEma = cbSlowTAType.SelectedIndex == 0 ? CalculateSMA(k, (int)numSlowTAPeriod.Value) : CalculateEMA(k, (int)numSlowTAPeriod.Value);
            MovingAverage fastEma = cbFastTAType.SelectedIndex == 0 ? CalculateSMA(k, (int)numFastTAPeriod.Value) : CalculateEMA(k, (int)numFastTAPeriod.Value);

            if (slowEma != null && fastEma != null)
            {
                int min = Math.Max(slowEma.Begin, fastEma.Begin);
                string format = dataSerieName == "price" ? "HH:mm" : "dd/MM HH:mm";

                // Calculate and draw slow average
                if (chkShowSlowTA.Checked)
                {
                    for (int i = 0; i < k.Count(); i++)
                    {
                        if (i >= min)
                        {
                            chart1.Series[slowName].Points.AddXY(k[i].Date, slowEma.Output[i - min]);
                            chart1.Series[slowName].Points[i - min].AxisLabel = k[i].Date.ToString(format);
                        }
                    }
                    chart1.Series[slowName].Enabled = true;
                }

                // Calculate and draw slow average
                if (chkShowFastTA.Checked)
                {
                    for (int i = 0; i < k.Count(); i++)
                    {
                        if (i >= min)
                        {
                            chart1.Series[fastName].Points.AddXY(k[i].Date, fastEma.Output[i - min]);
                            chart1.Series[fastName].Points[i - min].AxisLabel = k[i].Date.ToString(format);
                        }
                    }
                    chart1.Series[fastName].Enabled = true;
                }
                return min;
            }
            return 0;
        }

        private void Forecast()
        {
            // typeRegression is a string represented by one of the following strings:
            // "Linear", "Exponential", "Logarithmic", or "Power".
            // Polynomial is represented by an integer value in the form of a string.
            string typeRegression;

            // Defining the typeRegression.
            // This Statement can also be represented by the statement typeRegression = "2";
            typeRegression = "Exponential";

            // The number of days for Forecasting.
            int forecasting = 10;

            // Show Error as a range chart.
            string error = "true";

            // Show Forecasting Error as a range chart.
            string forecastingError = "true";

            // Formula parameters
            string parameters = typeRegression + ',' + forecasting + ',' + error + ',' + forecastingError;

            // Create Forecasting Series.
            chart1.DataManipulator.FinancialFormula(FinancialFormula.Forecasting, parameters, "priceHistory:Y", "Forecasting:Y,Forecasting:Y2,Forecasting:Y3");
            SetSeriesAppearance("Forecasting", SeriesChartType.Line);

            // Copy Forecasting Series Data Points to Range Chart.
            if (error == "true" || forecastingError == "true")
            {
                //chart1.DataManipulator.CopySeriesValues("Forecasting:Y2,Forecasting:Y3", "Range:Y,Range:Y");
                //SetSeriesAppearance("Range", SeriesChartType.Range, true);
            }
        }

        #endregion

        #region Init methods

        public void InitFromDatabase(DateTime minValue, bool addToRealtime, double interval)
        {
            IList<OHLC> candles = GetCandlesFromDatabase(minValue, interval);

            if (candles != null)
            {
                if (addToRealtime) foreach (var candle in candles) realtimeCandles.Add(candle);
                else historyCandles = candles;

                SaveCandlesToCSV(candles, "history.csv");
                if (!addToRealtime)
                    DrawChart(candles, "priceHistory");
            }
        }

        public void InitFromCSV(string filename)
        {
            historyCandles = ReadCandlesFromCSV(filename, DateTime.Now.Subtract(new TimeSpan(365, 0, 0, 0)));
            DrawChart(historyCandles, "priceHistory");
        }

        public void InitFromBitcoincharts()
        {
            historyCandles = GetCandlesFromBitcoincharts(DateTime.MinValue, Convert.ToDouble(nupHistoryPeriod.Value));
            if (historyCandles.Count > 0)
            {
                SaveCandlesToCSV(historyCandles, string.Format("history{0}.csv", pair.ToString()));
                DrawChart(historyCandles, "priceHistory");
            }
        }

        public void InitFromBtce(double interval)
        {
            try
            {
                if (GetCandlesFromBtce(interval))
                {
                    SaveCandlesToCSV(realtimeCandles, string.Format("realtime{0}.csv", pair.ToString()));
                    DrawChart(realtimeCandles);
                }

            }
            catch (Exception ex)
            {
                UpdateStatus(ex.Message + (ex.InnerException != null ? " " + ex.InnerException.Message : string.Empty));
            }
        }

        #endregion

        #region Core methods

        private SimulationResult SimulateEMACrossing(IList<OHLC> candles, int shortEMAPeriod, int longEMAPeriod, double buyThreshold, double sellThreshold, bool showLog)
        {
            decimal item = 100m;
            decimal currency = 0m;
            decimal buyAndHoldCurrency = 0m;
            //decimal last = 0m;
            //decimal lastBuyAndHold = 0m;

            SimulationResult result = new SimulationResult()
            {
                ShortMA = CalculateEMA(candles, shortEMAPeriod),
                LongMA = CalculateEMA(candles, longEMAPeriod),
                BuyThreshold = buyThreshold,
                SellThreshold = sellThreshold
            };

            if (showLog) lstMessages.Items.Clear();
            if (fee == 0) fee = 0.2m;

            if (result.ShortMA != null && result.LongMA != null)
            {
                int min = Math.Max(result.ShortMA.Begin, result.LongMA.Begin);

                decimal lastPrice = 0m;
                IList<Action> actions = new List<Action>();
                Action lastAction = null;
                for (int i = 0; i < candles.Count; i++)
                {
                    // Get current candle
                    OHLC candle = candles[i];
                    lastAction = actions.LastOrDefault();

                    // Simulate buy (used in buy and hold) on first candle
                    if (i == 0)
                    {
                        if (showLog) SetText(MessageType.Message, string.Format(CultureInfo.InvariantCulture, "{0} Simulation Started...", candle.Date));

                        decimal curFee = Math.Round(item / candle.Close * fee / 100m, 8);
                        decimal buy = Math.Round(item / candle.Close, 8);
                        buyAndHoldCurrency = buy - curFee;
                    }

                    // Analyze moving average starting from a certain point (where all MA have a value)
                    if (i >= min)
                    {
                        //var diff = 100 * (result.SlowMA.Output[i - min] - result.FastMA.Output[i - min]) / ((result.SlowMA.Output[i - min] + result.FastMA.Output[i - min]) / 2);
                        var diff = 100 * (result.LongMA.Output[i - min] - result.ShortMA.Output[i - min]) / ((result.LongMA.Output[i - min] + result.ShortMA.Output[i - min]) / 2);

                        if (item >= 0.1m)
                        {
                            if (diff > buyThreshold)
                            {
                                if (showLog) SetText(MessageType.Message, string.Format(CultureInfo.InvariantCulture, "{0} we are currently in uptrend ({1:0.###}%)", candle.Date, diff));

                                {
                                    decimal curFee = Math.Round(item / candle.Close * fee / 100m, 8);
                                    decimal buy = Math.Round(item / candle.Close, 8);
                                    currency += (buy - curFee);
                                    item -= Math.Round(buy * candle.Close, 4);

                                    actions.Add(new Action() { ActionType = ActionType.Bid, Date = candle.Date, AmountCurrency = currency, AmountItem = item });

                                    if (showLog)
                                    {
                                        if (actions.Count < 1 || lastPrice > candle.Close)
                                            SetText(MessageType.Message, string.Format(CultureInfo.InvariantCulture, "{0} bought {1} @ {2}", candle.Date, buy, buy * candle.Close), Color.LightGreen);
                                        else SetText(MessageType.Warning, string.Format(CultureInfo.InvariantCulture, "{0} bought {1} @ {2}", candle.Date, buy, buy * candle.Close), Color.LightGreen);
                                    }

                                    lastPrice = candle.Close;
                                }
                            }
                        }

                        if (currency >= 0.1m)
                        {
                            if (diff < sellThreshold)
                            {
                                if (showLog) SetText(MessageType.Message, string.Format(CultureInfo.InvariantCulture, "{0} we are currently in a downtrend ({1:0.###}%)", candle.Date, diff));

                                {
                                    decimal itemFee = Math.Round(currency * candle.Close * fee / 100m, 4);
                                    decimal sell = Math.Round(currency * candle.Close, 8);
                                    item += (sell - itemFee);
                                    currency -= Math.Round(sell / candle.Close, 8);

                                    actions.Add(new Action() { ActionType = ActionType.Ask, Date = candle.Date, AmountCurrency = currency, AmountItem = item });

                                    if (showLog)
                                    {
                                        if (actions.Count < 1 || lastPrice < candle.Close)
                                            SetText(MessageType.Message, string.Format(CultureInfo.InvariantCulture, "{0} sold {1} @ {2}", candle.Date, Math.Round(sell / candle.Close, 8), sell), Color.LightSkyBlue);
                                        else SetText(MessageType.Warning, string.Format(CultureInfo.InvariantCulture, "{0} sold {1} @ {2}", candle.Date, Math.Round(sell / candle.Close, 8), sell), Color.LightSkyBlue);
                                    }
                                    lastPrice = candle.Close;

                                }
                            }
                        }

                        //if (showLog)
                        //    SetText(MessageType.Message, string.Format(CultureInfo.InvariantCulture, "{0} we are currently not in an up or down trend ({1:0.###}%)", candle.Date, diff));
                    }

                    result.Actions = actions;
                    result.Performance = currency > item ? currency * candle.Close : item;
                    result.BuyAndHold = buyAndHoldCurrency * candle.Close;
                }

                if (showLog)
                {
                    SetText(MessageType.Message, string.Format(CultureInfo.InvariantCulture, "EMA Strategy Result (ShortEMA:{0}) (LongEMA:{1}) Performance:{2:0.####}% (vs B&H:{5:0.####}%) BuyThreshold:{3:0.###}% SellThreshold:{4:0.###}%",
                        result.ShortMA.Period, result.LongMA.Period, Math.Round(result.Performance, 4), result.BuyThreshold, result.SellThreshold, result.BuyAndHold));
                    SetText(MessageType.Message, string.Format("Buy and Hold wallet have {0:0.########} BTC", buyAndHoldCurrency));
                    SetText(MessageType.Message, string.Format("Strategy wallet have {0:0.########} {1}", currency > item ? currency : item, currency > item ? "BTC" : "EUR"));
                }
            }
            else if (showLog) SetText(MessageType.Warning, string.Format("Something wrong calculating EMAs..."));


            return result;
        }

        private void CalculateBestEMACrossing(IList<OHLC> candles)
        {
            if (candles.Count == 0)
            {
                SetText(MessageType.Warning, "Candles not loaded");
                return;
            }

            decimal best = 0;
            for (decimal sellInc = -0.02m; sellInc > -2.0m; sellInc -= 0.01m)
                for (decimal buyInc = 0.02m; buyInc < 2.0m; buyInc += 0.01m)
                    for (int k = 0; k < 24; k++)
                        for (int j = k; j < 60; j++)
                        {
                            SimulationResult simulation = SimulateEMACrossing(candles, 2 + k, 3 + j, (double)buyInc, (double)sellInc, false);
                            if ((k * j) % 20 == 0) UpdateStatus(string.Format(CultureInfo.InvariantCulture, "Simulation Result:{4:0.###}% ShortEMA:{0} LongEMA:{1} BuyThreshold:{2:0.###}% SellThreshold:{3:0.###}%", simulation.ShortMA.Period, simulation.LongMA.Period, buyInc, sellInc, simulation.Performance));

                            if (simulation.Performance > best)
                            {
                                best = simulation.Performance;
                                lastSimulation = simulation;

                                UpdateParams(simulation.ShortMA.Period, simulation.LongMA.Period, buyInc, sellInc);

                                SetText(MessageType.Message, string.Format(CultureInfo.InvariantCulture, "Best strategy {2:0.####}% vs Buy & Hold:{5:0.####}% => ShortEMA:{0} LongEMA:{1} BuyThreshold:{3:0.###}% SellThreshold:{4:0.###}% ",
                                    simulation.ShortMA.Period, simulation.LongMA.Period, Math.Round(best, 4), (double)buyInc, (double)sellInc, simulation.BuyAndHold));
                            }

                            if (ctsEma != null && ctsEma.IsCancellationRequested)
                                return;
                        }
        }

        private void UpdateParams(int shortPeriod, int longPeriod, decimal buyInc, decimal sellInc)
        {
            if (!InvokeRequired)
            {
                nupShortEMA.Value = shortPeriod;
                nupLongEMA.Value = longPeriod;
                nupBuyDiff.Value = buyInc;
                nupSellDiff.Value = sellInc;
            }
            else BeginInvoke(new Action<int, int, decimal, decimal>(UpdateParams), new object[] { shortPeriod, longPeriod, buyInc, sellInc });

        }

        private bool GetCandlesFromBtce(double interval)
        {
            try
            {
                if (pair == BtcePair.Unknown) return false;

                // Fetch trades from Btc-e API (return last 200 trades)
                IList<TradeInfo> trades = BtceApi.GetTrades(pair);
                trades = trades.Reverse().ToList();

                // Create trade list (required to calculate OHLC)
                IList<BitcoinCharts.Models.Trade> list = (from trade in trades
                                                          select new BitcoinCharts.Models.Trade()
                                                          {
                                                              Datetime = trade.Date.Subtract(providerTimeOffset),
                                                              Price = Convert.ToDecimal(trade.Price),
                                                              Quantity = Convert.ToDecimal(trade.Amount),
                                                              Symbol = trade.PriceCurrency.ToString()
                                                          }).ToList();

                // Get most recent candle
                OHLC last = realtimeCandles.LastOrDefault();

                // Calculate trades
                IList<OHLC> candles = CalculateOHLCFromTrades(list, interval, TradeSource.BTCe);

                // Get only new trades
                IList<OHLC> newCandles = candles.Where(x => x.Date > (last != null ? last.Date : DateTime.MinValue)).ToList();

                // Remove last candle in order to recalculate it
                if (newCandles.Count > 0)
                {
                    if (realtimeCandles.Count > 0)
                        realtimeCandles.RemoveAt(realtimeCandles.Count - 1);

                    if (newCandles.Count > 1)
                        if (realtimeCandles.Count() > 0)
                            realtimeCandles.RemoveAt(realtimeCandles.Count() - 1);
                }

                foreach (OHLC item in newCandles)
                    realtimeCandles.Add(item);

                return newCandles.Count() > 0;
            }
            catch (Exception)
            {
            }
            return false;
        }

        private IList<OHLC> GetCandlesFromBitcoincharts(DateTime minValue, double interval)
        {
            string currency = pair.ToString().Substring(pair.ToString().IndexOf("_") + 1, 3).ToUpperInvariant();

            lock (this)
            {
                var client = new BitcoinChartsClient();
                IList<OHLC> aggregate = new List<OHLC>();
                while (minValue.AddMinutes(interval) < DateTime.Now)
                {
                    Task<BitcoinCharts.Models.Trades> tradeFetcher;
                    if (minValue != DateTime.MinValue) tradeFetcher = client.GetTradesAsync(new DateTimeOffset(minValue), "btce", currency);
                    else tradeFetcher = client.GetTradesAsync("btce", currency);

                    if (tradeFetcher != null && tradeFetcher.Status != TaskStatus.Faulted)
                    {
                        try { tradeFetcher.Wait(); }
                        catch (Exception) { }

                        if (tradeFetcher.Status != TaskStatus.Faulted)
                        {
                            if (tradeFetcher.Result != null && tradeFetcher.Result.Count > 0)
                            {
                                IList<OHLC> candles = CalculateOHLCFromTrades(tradeFetcher.Result, interval, TradeSource.BitcoinCharts).Where(x => x.Date > minValue).ToList(); ;
                                if (candles.Count() > 0)
                                {
                                    aggregate = aggregate.Union(candles).ToList();
                                    minValue = candles.Last().Date;
                                }
                                else break;
                            }
                            else break;
                        }
                        else throw new Exception("Failed to get values from Bitcoincharts");
                    }
                    else break;
                }
                return aggregate;
            }
        }

        private IList<OHLC> GetCandlesFromDatabase(DateTime minValue, double interval)
        {
            try
            {
                using (var db = new TradeDbContext())
                {
                    IList<BitcoinCharts.Models.Trade> list = (from trade in db.BtceTrades
                                                              select new BitcoinCharts.Models.Trade()
                                                              {
                                                                  Datetime = trade.Timestamp,
                                                                  Price = trade.Price,
                                                                  Quantity = trade.Amount,
                                                                  Symbol = trade.Price_Currency
                                                              }).ToList();

                    IList<OHLC> candles = CalculateOHLCFromTrades(list, interval, TradeSource.Database).Where(x => x.Date > minValue).ToList();
                    return candles;
                }
            }
            catch (Exception e)
            {
            }
            return null;
        }

        public IList<OHLC> ReadCandlesFromCSV(string filename)
        {
            return ReadCandlesFromCSV(filename, null);
        }

        public IList<OHLC> ReadCandlesFromCSV(string filename, DateTime? minValue)
        {
            if (!File.Exists(filename)) return new List<OHLC>();
            UpdateStatus(string.Format("Loading {0}", filename));
            using (var reader = new StreamReader(File.OpenRead(filename)))
            {
                List<OHLC> k = new List<OHLC>();
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine(); //date,open,high,low,close
                    var values = line.Split(',');
                    k.Add(new OHLC()
                    {
                        Date = UnixTime.ConvertToDateTime(Convert.ToUInt32(values[0])),
                        Open = decimal.Parse(values[1], CultureInfo.InvariantCulture),
                        High = decimal.Parse(values[2], CultureInfo.InvariantCulture),
                        Low = decimal.Parse(values[3], CultureInfo.InvariantCulture),
                        Close = decimal.Parse(values[4], CultureInfo.InvariantCulture),
                        TradeSource = TradeSource.CSV
                    });
                }

                if (minValue.HasValue)
                    k = k.Where(x => x.Date > minValue).ToList();

                return k;
            }
        }

        public void SaveCandlesToCSV(IList<OHLC> candles, string filename)
        {
            IList<string> lines = candles.Select(x => string.Format(CultureInfo.InvariantCulture, "{0},{1:N8},{2:N8},{3:N8},{4:N8}", UnixTime.GetFromDateTime(x.Date), x.Open, x.High, x.Low, x.Close)).ToList();
            File.WriteAllLines(filename, lines);
            UpdateStatus(string.Format("Candles were saved to {0}", filename));
        }

        public IList<OHLC> CalculateOHLCFromTrades(IList<BitcoinCharts.Models.Trade> trades, double interval, TradeSource tradeSource)
        {
            var result = from trade in trades
                         group trade by (long)(trade.Datetime.DateTime.Subtract(DateTime.MinValue).TotalMinutes / interval) into candleTrades
                         let key = candleTrades.Key
                         select new OHLC()
                         {
                             Date = DateTime.MinValue.AddMinutes(candleTrades.Key * interval),
                             Open = candleTrades.First().Price,
                             Close = candleTrades.Last().Price,
                             Low = candleTrades.Min(x => x.Price),
                             High = candleTrades.Max(x => x.Price),
                             TradesCount = candleTrades.Count(),
                             TradeSource = tradeSource
                         };

            return result.ToList();
        }

        public MovingAverage CalculateEMA(IList<OHLC> candles, int periodsAverage)
        {
            double[] closePrice = candles.Select(x => (double)x.Close).ToArray();
            double[] output = new double[closePrice.Length];
            int begin;
            int length;

            TicTacTec.TA.Library.Core.RetCode retCode = Core.Ema(0, closePrice.Length - 1, closePrice, periodsAverage, out begin, out length, output);

            if (retCode == TicTacTec.TA.Library.Core.RetCode.Success)
                return new MovingAverage() { Begin = begin, Length = length, Output = output, Period = periodsAverage };

            return null;
        }

        public MovingAverage CalculateSMA(IList<OHLC> candles, int periodsAverage)
        {
            double[] closePrice = candles.Select(x => (double)x.Close).ToArray();
            double[] output = new double[closePrice.Length];
            int begin;
            int length;

            TicTacTec.TA.Library.Core.RetCode retCode = Core.Sma(0, closePrice.Length - 1, closePrice, periodsAverage, out begin, out length, output);

            //a_wma = talib.WMA(a_cls, 25)
            //a_chaikin = talib.AD(a_hig, a_low, a_cls, a_vol)
            //a_cdlCounterAttack = talib.CDLCOUNTERATTACK(a_opn, a_hig, a_low, a_cls)

            if (retCode == TicTacTec.TA.Library.Core.RetCode.Success)
                return new MovingAverage() { Begin = begin, Length = length, Output = output, Period = periodsAverage };

            return null;
        }

        public void DrawChart(IList<OHLC> k)
        {
            DrawChart(k, "price");
        }

        public void DrawChart(IList<OHLC> k, string serieName)
        {
            if (!InvokeRequired)
            {
                System.Threading.Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-us");

                try
                {
                    // Set series chart type
                    if (chart1.Series.FindByName(serieName) != null)
                        chart1.Series[serieName].Points.Clear();

                    // Adjust Y & X axis scale
                    chart1.ResetAutoValues();
                    chart1.Series[serieName].Color = Color.SteelBlue;

                    // Set point width
                    chart1.Series[serieName]["PointWidth"] = "0.8";
                    chart1.Series[serieName].IsXValueIndexed = true;

                    // Set colors bars
                    if (serieName != "price")
                    {
                        chart1.Series[serieName]["PriceUpColor"] = "White"; // <<== use text indexer for series
                        chart1.Series[serieName]["PriceDownColor"] = "SteelBlue"; // <<== use text indexer for series
                    }

                    // Align series with minumum from ta-lib
                    int min = CalculateTALib(k, serieName);
                    for (int i = 0; i < k.Count; i++)
                    {
                        if (i >= min)
                        {
                            // adding date and high, low, open, close
                            chart1.Series[serieName].Points.AddXY(k[i].Date, (double)k[i].High);

                            DataPoint currentPoint = chart1.Series[serieName].Points[i - min];
                            currentPoint.YValues[1] = (double)k[i].Low;
                            currentPoint.YValues[2] = (double)k[i].Open;
                            currentPoint.YValues[3] = (double)k[i].Close;

                            string format = serieName == "price" ? "HH:mm" : "dd/MM HH:mm";
                            currentPoint.AxisLabel = k[i].Date.ToString(format);

                            if (lastSimulation != null)
                            {
                                Action action = lastSimulation.Actions.Where(x => x.Date == k[i].Date).FirstOrDefault();
                                if (action != null)
                                {
                                    if (action.ActionType == ActionType.Bid)
                                        currentPoint.MarkerImage = "MaxMarker.bmp";
                                    else if (action.ActionType == ActionType.Ask)
                                        currentPoint.MarkerImage = "MinMarker.bmp";

                                    currentPoint.MarkerImageTransparentColor = Color.White;

                                    currentPoint.ToolTip = string.Format(CultureInfo.InvariantCulture, "Source: {0}\nDate: {1}\nLow: {2:0.####} High: {3:0.####}\nOpen: {4:0.####} Close: {5:0.####}\n\nAction:{6} Currency:{7} Item:{8}",
                                                                                                    k[i].TradeSource.ToString(), k[i].Date, k[i].Low, k[i].High, k[i].Open, k[i].Close,
                                                                                                    action.ActionType.ToString(), action.AmountCurrency, action.AmountItem);


                                }
                                else currentPoint.ToolTip = string.Format(CultureInfo.InvariantCulture, "Source: {0}\nDate: {1}\nLow: {2:0.####} High: {3:0.####}\nOpen: {4:0.####} Close: {5:0.####}",
                                                                                                        k[i].TradeSource.ToString(), k[i].Date, k[i].Low, k[i].High, k[i].Open, k[i].Close);

                            }
                            else currentPoint.ToolTip = string.Format(CultureInfo.InvariantCulture, "Source: {0}\nDate: {1}\nLow: {2:0.####} High: {3:0.####}\nOpen: {4:0.####} Close: {5:0.####}",
                                                                                                    k[i].TradeSource.ToString(), k[i].Date, k[i].Low, k[i].High, k[i].Open, k[i].Close);

                        }
                    }

                    CalculateAverages(serieName);
                    //if (chkShowForecast.Checked) Forecast();

                    string area = serieName == "price" ? "Price" : "History";
                    chart1.ChartAreas[area].AxisX.ScaleView.Zoomable = true;
                    chart1.ChartAreas[area].AxisY.ScaleView.Zoomable = true;
                    chart1.ChartAreas[area].CursorX.AutoScroll = true;
                    chart1.ChartAreas[area].CursorY.AutoScroll = true;

                    /*
                    // Find point with maximum Y value and set marker image
                    DataPoint maxValuePoint = chart1.Series[serieName].Points.FindMaxByValue();
                    DataPoint minValuePoint = chart1.Series[serieName].Points.FindMinByValue();
                    */

                    chart1.AlignDataPointsByAxisLabel();
                    chart1.Invalidate();
                }
                catch (Exception e)
                {
                    lblStatus.Text = string.Format("Error in DrawChart: {0}", e.Message);
                    if (serieName == "price") realtimeCandles.Clear();
                }
            }
            else BeginInvoke(new Action<IList<OHLC>, string>(DrawChart), new object[] { k, serieName });
        }

        #endregion

        #region Helpers

        private void SetSeriesAppearance(string seriesName, SeriesChartType chartType)
        {
            SetSeriesAppearance(seriesName, chartType, false);
        }

        private void SetSeriesAppearance(string seriesName, SeriesChartType chartType, bool isTransparent)
        {
            chart1.Series[seriesName].ChartType = chartType;
            chart1.Series[seriesName].BorderWidth = 2;
            chart1.Series[seriesName].ShadowOffset = 1;
            chart1.Series[seriesName].IsVisibleInLegend = false;
            chart1.Series[seriesName].IsXValueIndexed = true;
            chart1.Series[seriesName].Enabled = true;

            if (isTransparent)
            {
                Color color = chart1.Series[seriesName].Color;
                chart1.Series[seriesName].Color = Color.FromArgb(128, color.R, color.G, color.B);
            }
        }

        public void SetText(MessageType messageType, string description)
        {
            SetText(messageType, description, Color.White);
        }

        public void SetText(MessageType messageType, string description, Color backColor)
        {
            if (!lstMessages.InvokeRequired)
            {
                try
                {
                    ListViewItem item = new ListViewItem();
                    item.Text = string.Empty;
                    item.ImageIndex = (int)messageType;
                    item.StateImageIndex = item.ImageIndex;
                    item.BackColor = backColor;

                    item.SubItems.AddRange(new string[] { DateTime.Now.ToString("HH:mm:ss:fff", CultureInfo.CurrentCulture), description });
                    lstMessages.Items.Insert(lstMessages.Items.Count, item);
                    item.EnsureVisible();
                    //if (lstMessages.Items.Count > 100)
                    //    lstMessages.Items.RemoveAt(0);

                }
                catch (ArgumentOutOfRangeException)
                {
                }
            }
            else BeginInvoke(new Action<MessageType, string>(SetText), new object[] { messageType, description });
        }

        #endregion

        #region Control Events

        private void OnFillItemAmount(object sender, EventArgs e)
        {
            decimal balance = decimal.Parse(lblItemBalance.Text.Substring(0, lblItemBalance.Text.IndexOf(" ")), CultureInfo.InvariantCulture);
            decimal buyPrice = decimal.Parse(txtPriceBuy.Text, CultureInfo.InvariantCulture);

            decimal result = balance / buyPrice;
            txtItemAmount.Text = string.Format(CultureInfo.InvariantCulture, "{0:0.########}", result);
        }

        private void OnFillCurrencyAmount(object sender, EventArgs e)
        {
            txtCurrencyAmount.Text = lblCurrencyBalance.Text.Substring(0, lblCurrencyBalance.Text.IndexOf(" "));
        }

        private void OnFillPriceCurrency(object sender, EventArgs e)
        {
            txtPriceSell.Text = lblSell.Text.Substring(0, lblSell.Text.IndexOf(" "));
        }

        private void OnFillPriceItem(object sender, EventArgs e)
        {
            txtPriceBuy.Text = lblBuy.Text.Substring(0, lblBuy.Text.IndexOf(" "));
        }

        private void OnBuyUpdateTotal(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(txtItemAmount.Text) && !string.IsNullOrEmpty(txtPriceBuy.Text))
            {
                decimal total = decimal.Parse(txtItemAmount.Text, CultureInfo.InvariantCulture) * decimal.Parse(txtPriceBuy.Text, CultureInfo.InvariantCulture);
                decimal totalFee = total * fee / 100m;
                lblBuyTotal.Text = string.Format(CultureInfo.InvariantCulture, "{0:0.#####} EUR", total);
                lblBuyFee.Text = string.Format(CultureInfo.InvariantCulture, "{0:0.#####} EUR", totalFee);
            }
        }

        private void OnSellUpdateTotal(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(txtCurrencyAmount.Text) && !string.IsNullOrEmpty(txtPriceSell.Text))
            {
                decimal total = decimal.Parse(txtCurrencyAmount.Text, CultureInfo.InvariantCulture) * decimal.Parse(txtPriceSell.Text, CultureInfo.InvariantCulture);
                decimal totalFee = total * fee / 100m;
                lblSellTotal.Text = string.Format(CultureInfo.InvariantCulture, "{0:0.#####} EUR", total);
                lblSellFee.Text = string.Format(CultureInfo.InvariantCulture, "{0:0.#####} EUR", totalFee);
            }
        }

        private void OnRealtimeEnableDisable(object sender, EventArgs e)
        {
        }

        private void OnShowAverageChanged(object sender, EventArgs e)
        {
            CalculateAverages("price");
            CalculateAverages("priceHistory");
            chart1.AlignDataPointsByAxisLabel();
            chart1.Invalidate();
        }

        private void OnMATypeChanged(object sender, EventArgs e)
        {
            CalculateAverages("price");
            CalculateAverages("priceHistory");
            chart1.AlignDataPointsByAxisLabel();
            chart1.Invalidate();
        }

        private void OnMAPeriodChanged(object sender, EventArgs e)
        {
            CalculateAverages("price");
            CalculateAverages("priceHistory");
            chart1.AlignDataPointsByAxisLabel();
            chart1.Invalidate();
        }

        private void OnShowForecastChanged(object sender, EventArgs e)
        {
            Forecast();
        }

        private void OnShowTAChanged(object sender, EventArgs e)
        {
            if (realtimeCandles.Count > 0) DrawChart(realtimeCandles, "price");
            if (historyCandles.Count > 0) DrawChart(historyCandles, "priceHistory");
        }

        private void OnTATypeChanged(object sender, EventArgs e)
        {
            if (realtimeCandles.Count > 0) DrawChart(realtimeCandles, "price");
            if (historyCandles.Count > 0) DrawChart(historyCandles, "priceHistory");
        }

        private void OnTAPeriodChanged(object sender, EventArgs e)
        {
            if (realtimeCandles.Count > 0) DrawChart(realtimeCandles, "price");
            if (historyCandles.Count > 0) DrawChart(historyCandles, "priceHistory");
        }

        private void OnPairChanged(object sender, EventArgs e)
        {
            ClearChart();
            string p = (string)cbPairs.SelectedValue;
            p = p.Replace("/", "_").ToLowerInvariant();
            pair = (BtcePair)Enum.Parse(typeof(BtcePair), p);
        }

        private void ClearChart()
        {
            realtimeCandles.Clear();
            historyCandles.Clear();
            chart1.Series["price"].Points.Clear();
            chart1.Series["priceHistory"].Points.Clear();
            chart1.Invalidate();
        }

        private void OnRealtimePeriodChange(object sender, EventArgs e)
        {
            ClearChart();
        }

        private void OnHistoricalPeriodChange(object sender, EventArgs e)
        {
            ClearChart();
        }

        #endregion
    }

}
