using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearRegression;
using Newtonsoft.Json;
using ScottPlot;
using ScottPlot.Plottable;
using SpectrumPlotter.LIBS;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

/*
 Icon source:
   https://www.flaticon.com/de/kostenloses-icon/das-ganze-spektrum_1488165
 */

namespace SpectrumPlotter
{
    public partial class MainScreen : Form
    {
        private SerialPort Port = null;
        private Thread MainThread = null;
        private System.Windows.Forms.Timer UpdateTimer = null;

        private ushort[] PayloadBuffer = new ushort[8192];
        private double[] SignalCorrectedX = null;
        private double[] SignalCorrectedY = null;
        private double[] SignalResampledX = null;
        private double[] SignalResampledY = null;
        private int PayloadUsed = 0;
        private bool SensorDataReceived = false;
        private bool PlotUpdateAsync = false;
        private bool PlotRebuild = true;
        private bool PlotFit = false;
        private bool MouseEntered = false;

        private ListViewItem LastCheckedElement = null;

        private bool Updating = false;
        private SignalPlotXY PlotPolygon = null;
        private SignalPlotXY PlotSelectedElement = null;
        private Text MaxLabel = null;
        private Text CursorLabel = null;
        private ArrowCoordinated MaxArrow = null;

        private ConfigFile Config = new ConfigFile();
        private ElementDatabase Elements = new ElementDatabase();
        private Dictionary<string, ListViewItem> ElementListMap = new Dictionary<string, ListViewItem>();
        private Dictionary<SignalPlotXY, ListViewItem> CaptureListMap = new Dictionary<SignalPlotXY, ListViewItem>();
        private Dictionary<SignalPlotXY, SpectrumWindow> CapturedPlots = new Dictionary<SignalPlotXY, SpectrumWindow>();
        private ListViewColumnSorter lvwColumnSorter;

        private DateTime CaptureStart = DateTime.Now;
        private DateTime CaptureEnd = DateTime.Now;

        private bool ConfigUpdated = false;
        private double CurrentWavelength = 0;
        private double[] LastGeneratedPoly = null;
        private bool Resampling;

        public MainScreen()
        {
            InitializeComponent();

            Text += "      v" + ThisAssembly.Git.SemVer.Major + "." + ThisAssembly.Git.SemVer.Minor + "." + ThisAssembly.Git.Commits + "-" + ThisAssembly.Git.Branch + "+" + ThisAssembly.Git.Commit + (ThisAssembly.Git.IsDirty ? " dirty" : "");

            formsPlot1.Plot.XLabel("Wavelength [nm]");
            formsPlot1.Plot.YLabel("Amplitude [rel]");
            formsPlot1.Plot.Title("CMOS Spectral plot");
            formsPlot1.Plot.Style(Style.Gray2);
            formsPlot1.Plot.Legend();
            formsPlot1.MouseMove += FormsPlot1_MouseMove;
            formsPlot1.RightClicked -= formsPlot1.DefaultRightClickEvent;
            formsPlot1.RightClicked += CustomRightClickEvent;

            MaxLabel = new Text() { X = 0, Y = 0, Label = "(empty)", Color = Color.Red, IsVisible = false };
            MaxArrow = new ArrowCoordinated(0, 0, 0, 0) { Label = "", Color = Color.Red, LineWidth = 2, ArrowheadWidth = 9, ArrowheadLength = 9, IsVisible = false };
            CursorLabel = new Text() { X = 0, Y = 0, Label = "(empty)", Color = Color.Green, IsVisible = false };
            PlotSelectedElement = new SignalPlotXY() { Label = "empty", IsVisible = false };
            PlotPolygon = new SignalPlotXY() { Label = "Measurement", IsVisible = false };

            Config = ConfigFile.Load(Config.Filename, out bool valid);

            if(!valid)
            {
                MessageBox.Show("Failed to load config file. Resetting to defaults.", "Failed to load config");
            }

            UpdateTimer = new System.Windows.Forms.Timer
            {
                Interval = 50
            };
            UpdateTimer.Tick += UpdateTimer_Tick;
            UpdateTimer.Start();

            lvwColumnSorter = new ListViewColumnSorter();
            this.lstElementLib.ListViewItemSorter = lvwColumnSorter;

            Config.ChangedCallback += () => { UpdateFromConfig(); };
            UpdateFromConfig();

            UpdateElements();
        }

        void UpdateFromConfig()
        {
            PolyfitCalc();
            cmbPorts.Text = Config.SerialPort;
            txtShPeriod.Text = (Config.ShPeriod * 2).ToString();
            txtIcgPeriod.Text = (Config.IcgPeriod * 2).ToString();
            chkTrigger.Checked = Config.Trigger;
            txtTriggerDelay.Text = Config.TriggerDelay.ToString();

            PlotSelectedElement.Color = GetColor(Config.LibsColor, Color.Red);
            PlotPolygon.Color = GetColor(Config.MeasurementColor, Color.SkyBlue);
        }

        private void cmbPorts_DropDown(object sender, EventArgs e)
        {
            string[] ports = SerialPort.GetPortNames();

            cmbPorts.Items.Clear();
            cmbPorts.Items.AddRange(ports);
        }

        private void CustomRightClickEvent(object sender, EventArgs e)
        {
            var form = PolyfitForm.CurrentForm;
            if (form != null && !form.Disposing && form.Visible)
            {
                if (ModifierKeys == Keys.Alt)
                {
                    form.Sensor = CurrentWavelength;
                    return;
                }
                if (ModifierKeys == (Keys.Alt | Keys.Shift))
                {
                    form.Reference = CurrentWavelength;
                    return;
                }
            }
            ContextMenuStrip customMenu = new ContextMenuStrip();
            customMenu.Items.Add(new ToolStripMenuItem("Zoom to fit data", null, (s, ev) => { formsPlot1.Plot.AxisAuto(); }));
            customMenu.Items.Add(new ToolStripMenuItem("---"));
            customMenu.Items.Add(new ToolStripMenuItem("Save last captured spectrum", null, new EventHandler(SaveCaptured)));
            customMenu.Items.Add(new ToolStripMenuItem("Load captured spectrum", null, new EventHandler(LoadCaptured)));
            customMenu.Show(System.Windows.Forms.Cursor.Position);
        }

        private void SaveCaptured(object sender, EventArgs e)
        {
            SignalPlotXY plot = CapturedPlots.Keys.LastOrDefault();

            if (plot == null)
            {
                MessageBox.Show("You have to capture first");
            }

            SaveCapturedDialog(plot);
        }

        private void SaveCapturedDialog(SignalPlotXY plot)
        {
            if(!CapturedPlots.ContainsKey(plot))
            {
                return;
            }

            SaveFileDialog dlg = new SaveFileDialog()
            {
                FileName = "spectrum.spect",
                Filter = "Spectrum Files (*.spect)|*.spect;*.spect" +
                    "|JSON Files (*.json)|*.json;*.json" +
                    "|All files (*.*)|*.*"
            };

            if (dlg.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            try
            {
                SpectrumWindow window = CapturedPlots[plot];
                string ser = JsonConvert.SerializeObject(window);

                File.WriteAllText(dlg.FileName, ser);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save spectrum to '" + dlg.FileName + "'");
            }
        }

        private void LoadCaptured(object sender, EventArgs e)
        {
            LoadCapturedDialog();
        }

        private void LoadCapturedDialog()
        {
            OpenFileDialog dlg = new OpenFileDialog()
            {
                Multiselect = true,
                FileName = "spectrum.spect",
                Filter = "Spectrum Files (*.spect)|*.spect;*.spect" +
                    "|JSON Files (*.json)|*.json;*.json" +
                    "|All files (*.*)|*.*"
            };

            if (dlg.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            foreach (string name in dlg.FileNames)
            {
                try
                {
                    LoadCaptured(name);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to load spectrum from '" + name + "'");
                }
            }
            RefreshCaptures();
        }

        private void LoadCaptured(string name)
        {
            string ser = File.ReadAllText(name);
            SpectrumWindow window = JsonConvert.DeserializeObject<SpectrumWindow>(ser);

            var plot = formsPlot1.Plot.AddSignalXY(window.Wavelengths, window.Intensities, label: window.Name);
            if (!string.IsNullOrEmpty(window.Color))
            {
                Color color = plot.Color;

                try
                {
                    color = Color.FromName(window.Color);
                }
                catch(Exception ex)
                {
                }
                plot.Color = color;
            }
            window.Temporary = false;

            CapturedPlots.Add(plot, window);
        }

        private void LoadCaptured()
        {
            foreach (var fi in new DirectoryInfo(".").GetFiles("*.spect"))
            {
                try
                {
                    LoadCaptured(fi.FullName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to load spectrum from '" + fi.FullName + "'");
                }
            }
        }

        private void UpdateElements()
        {
            lstElementLib.Items.Clear();
            ElementListMap.Clear();

            foreach (var elemInfo in Elements.ElementInfos)
            {
                foreach (var elem in elemInfo.Elements)
                {
                    ListViewItem item = new ListViewItem(new string[] { elem.Name, "" });
                    lstElementLib.Items.Add(item);
                    ElementListMap.Add(elem.Name, item);
                }
            }
        }

        private async void FormsPlot1_MouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                double pxPerUnit = formsPlot1.Plot.XAxis.Dims.PxPerUnit;
                double pyPerUnit = formsPlot1.Plot.YAxis.Dims.PxPerUnit;
                (double mouseCoordX, double mouseCoordY) = formsPlot1.GetMouseCoordinates();

                if (double.IsNaN(pxPerUnit) || double.IsNaN(pyPerUnit))
                {
                    return;
                }

                CursorLabel.Label = "      λ: " + mouseCoordX.ToString("0.00") + " nm";
                CursorLabel.X = mouseCoordX + 8 / pxPerUnit;
                CursorLabel.Y = mouseCoordY;
                CursorLabel.IsVisible = true;

                if (PlotPolygon.IsVisible)
                {
                    (double pointX, double pointY, int pointIndex) = PlotPolygon.GetPointNearestX(mouseCoordX);

                    CurrentWavelength = pointX;

                    if (pointIndex > 0 && pointX > 0)
                    {
                        MaxArrow.Tip.X = pointX;
                        MaxArrow.Tip.Y = pointY;
                        MaxArrow.Base.X = pointX;
                    }

                    int region = 50;
                    int pointStart = Math.Max(pointIndex - region, PlotPolygon.MinRenderIndex);
                    int pointEnd = Math.Min(pointIndex + region, PlotPolygon.MaxRenderIndex);
                    double peakValue = double.MinValue;
                    double peakWavelength = 0;
                    double peakRelValue = 0;
                    int peakIndex = -1;

                    for (int pos = pointStart; pos < pointEnd; pos++)
                    {
                        int distance = Math.Max(1, Math.Abs(pointIndex - pos));
                        double distFact = Math.Sqrt(1.0f - (distance / (double)region));
                        double relValue = PlotPolygon.Ys[pos] * distFact;

                        if (relValue > peakRelValue)
                        {
                            peakWavelength = PlotPolygon.Xs[pos];
                            peakValue = PlotPolygon.Ys[pos];
                            peakRelValue = relValue;
                            peakIndex = pos;
                        }
                    }

                    if (peakIndex >= 0)
                    {
                        MaxArrow.Label = "Maximum λ: " + peakWavelength.ToString("0.00") + " nm";
                        MaxArrow.Tip.X = peakWavelength;
                        MaxArrow.Base.X = peakWavelength;
                        MaxArrow.Tip.Y = peakValue;
                        MaxLabel.X = peakWavelength + 15 / pxPerUnit;
                        MaxLabel.Y = peakValue + 32 / pyPerUnit;
                        MaxLabel.Label = "Maximum at λ: " + peakWavelength.ToString("0.00") + " nm";
                    }

                }
                RedrawPlot();
            }
            catch (Exception ex)
            {
            }
        }

        private void btnConnect_Click(object sender, EventArgs e)
        {
            if (Port == null)
            {
                bool success = false;
                string message = "";

                try
                {
                    Port = new SerialPort(cmbPorts.Text, 500000);
                    Port.Open();

                    Port.ReadTimeout = 200;
                    Flush();

                    byte[] txCommand = new byte[] { 0x45, 0x52, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x01, 0x00, 0x02, 0x00 };

                    Port.Write(txCommand, 0, txCommand.Length);
                    Thread.Sleep(100);

                    try
                    {
                        byte[] rxBuffer = new byte[10];

                        PortRead(rxBuffer, 1000);

                        string resp = Encoding.ASCII.GetString(rxBuffer);

                        if (resp == "[g3gg0.de]")
                        {
                            success = true;
                        }
                        else
                        {
                            message = "Invalid magic received";
                        }
                    }
                    catch (TimeoutException ex)
                    {
                        message = "No custom response";
                    }
                }
                catch (Exception ex)
                {
                    message = "Exception: " + ex.Message;
                }

                if (success)
                {
                    MainThread = new Thread(MainFunc);
                    MainThread.Start();

                    btnConnect.Text = "Disconnect";
                    cmbPorts.Enabled = false;

                    Config.SerialPort = cmbPorts.Text;
                    Config.Changed = true;
                }
                else
                {
                    MessageBox.Show(message, "Failed to open serial port");
                    Port.Close();
                    Port = null;
                }
            }
            else
            {
                MainThread.Abort();
                MainThread = null;

                try
                {
                    Port.Close();
                }
                catch (Exception ex)
                {
                }
                Port = null;

                btnConnect.Text = "Connect";
                cmbPorts.Enabled = true;
            }
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                double captureDuration = (CaptureEnd - CaptureStart).TotalMilliseconds;
                double captureElapsed = (DateTime.Now - CaptureStart).TotalMilliseconds;
                double progress = 1000 * Math.Min(1.0f, captureElapsed / captureDuration);

                if (Config.Trigger)
                {
                    progress = 0;
                }
                progressBar.Minimum = 0;
                progressBar.Maximum = 1001;
                progressBar.Value = (int)progress + 1;
                progressBar.Value = (int)progress;

                ConfigUpdated |= Config.CheckReload();

                if (!SensorDataReceived && !PlotUpdateAsync && !ConfigUpdated)
                {
                    return;
                }

                lock (this)
                {
                    if (Resampling)
                    {
                        return;
                    }
                    Resampling = true;
                }

                if (SensorDataReceived || ConfigUpdated)
                {
                    int used = PayloadUsed;

                    if (Config.DarkFrame == null || Config.DarkFrame.Length != used)
                    {
                        Config.DarkFrame = new ushort[used];
                    }

                    if (used != 0)
                    {
                        PlotRebuild = true;

                        if (SignalCorrectedX == null)
                        {
                            SignalCorrectedX = new double[used];
                            SignalCorrectedY = new double[used];
                        }

                        if (SignalCorrectedX.Length != used)
                        {
                            Array.Resize(ref SignalCorrectedX, used);
                            Array.Resize(ref SignalCorrectedY, used);
                        }

                        for (int pos = 0; pos < used; pos++)
                        {
                            double dark = Config.DarkFrame[pos];
                            double value = PayloadBuffer[pos];
                            double adcValue = (value - Math.Min(dark, value)) / 0xFFFF;

                            adcValue = Math.Max(0, adcValue);
                            adcValue = Math.Min(1.0f, adcValue);

                            SignalCorrectedX[pos] = TransformLambda(pos);
                            SignalCorrectedY[pos] = TransformIntensity(adcValue, SignalCorrectedX[pos]);
                        }
                    }

                    if (Config.Trigger && Config.TriggerAutoNormalize && SignalCorrectedY != null)
                    {
                        double max = 0;
                        for (int pos = 0; pos < used; pos++)
                        {
                            max = Math.Max(max, SignalCorrectedY[pos]);
                        }

                        if (max != 0)
                        {
                            for (int pos = 0; pos < used; pos++)
                            {
                                SignalCorrectedY[pos] /= max;
                            }
                        }
                    }

                    if (used != 0)
                    {
                        int groupSize = Math.Max(1, SignalCorrectedX.Length / Config.ResampleResolution);
                        int resampleSize = SignalCorrectedX.Length / groupSize;
                        
                        if (SignalResampledX == null)
                        {
                            SignalResampledX = new double[resampleSize];
                            SignalResampledY = new double[resampleSize];
                        }

                        if (SignalResampledX.Length != resampleSize)
                        {
                            PlotRebuild = true;
                            Array.Resize(ref SignalResampledX, resampleSize);
                            Array.Resize(ref SignalResampledY, resampleSize);
                        }

                        for (int pos = 0; pos < resampleSize; pos++)
                        {
                            double xSum = 0;
                            double ySum = 0;
                            int startSrc = (int)(pos * groupSize);
                            int endSrc = (int)Math.Min(((pos + 1) * groupSize), SignalCorrectedX.Length);

                            for (int srcPos = startSrc; srcPos < endSrc; srcPos++)
                            {
                                xSum += SignalCorrectedX[srcPos];
                                ySum += SignalCorrectedY[srcPos];
                            }

                            SignalResampledX[pos] = xSum / groupSize;
                            SignalResampledY[pos] = ySum / groupSize;
                        }

                        if (Config.Trigger && Config.TriggerAutoCapture)
                        {
                            if (Config.TriggerAutoClear)
                            {
                                ClearCaptures();
                            }

                            CaptureAdd(SignalResampledX, SignalResampledY);
                            PlotRebuild = true;
                        }

                        if (Config.Trigger && Config.TriggerAutoMatch)
                        {
                            CalculateMatch(SignalResampledX, SignalResampledY);
                            PlotRebuild = true;
                        }
                    }

                    /* update main plot */
                    if (SignalResampledX != null)
                    {
                        if (PlotPolygon.Xs != SignalResampledX)
                        {
                            PlotPolygon.IsVisible = true;
                            PlotPolygon.Xs = SignalResampledX;
                            PlotPolygon.Ys = SignalResampledY;
                            PlotPolygon.MaxRenderIndex = SignalResampledX.Length - 1;
                            formsPlot1.Plot.SetAxisLimits(SignalResampledX[0], SignalResampledX[SignalResampledX.Length - 1], 0, 1.0f);
                            PlotPolygon.ValidateData();
                            PlotPolygon.MarkerSize = 0;
                        }
                    }

                    PlotUpdateAsync = true;
                }

                RedrawPlot();
            }
            catch(Exception ex)
            {
            }

            Resampling = false;
        }

        private void RedrawPlot()
        {
            if(InvokeRequired)
            {
                Invoke( new Action(()=> { RedrawPlot(); }));
                return;
            }

            lock (this)
            {
                if (Updating)
                {
                    return;
                }
                Updating = true;
            }

            try
            {
                lock (formsPlot1)
                {
                    if (PlotPolygon.IsVisible)
                    {
                        double minValue = double.MaxValue;
                        double maxValue = 0;

                        /* calculate min/max values */
                        for (int pos = 0; pos < PlotPolygon.Xs.Length; pos++)
                        {
                            if (PlotPolygon.Ys[pos] > maxValue)
                            {
                                maxValue = PlotPolygon.Ys[pos];
                            }
                            if (PlotPolygon.Ys[pos] < minValue)
                            {
                                minValue = PlotPolygon.Ys[pos];
                            }
                        }
                        MaxArrow.Base.Y = minValue;
                    }

                    /* update the plot setup */
                    bool plotFit = PlotFit;

                    /* re-add all plottables */
                    if (PlotRebuild)
                    {
                        formsPlot1.Plot.Clear();

                        formsPlot1.Plot.Add(PlotPolygon);
                        formsPlot1.Plot.Add(PlotSelectedElement);
                        formsPlot1.Plot.Add(MaxLabel);
                        formsPlot1.Plot.Add(CursorLabel);
                        formsPlot1.Plot.Add(MaxArrow);

                        RefreshCapturedPlots();
                    }

                    if (plotFit)
                    {
                        formsPlot1.Plot.AxisAuto();
                    }

                    formsPlot1.Refresh();
                    formsPlot1.Render();
                }
            }
            catch (Exception ex)
            {
            }

            SensorDataReceived = false;
            PlotUpdateAsync = false;
            ConfigUpdated = false;
            PlotRebuild = false;
            PlotFit = false;

            Updating = false;
        }

        private Color GetColor(string colorString, Color defaultColor)
        {
            if (!string.IsNullOrEmpty(Config.LibsColor))
            {
                try
                {
                    defaultColor = Color.FromName(colorString);
                }
                catch (Exception ex)
                {
                }
            }

            return defaultColor;
        }

        private void formsPlot1_MouseEnter(object sender, EventArgs e)
        {
            lock (formsPlot1)
            {
                if (MouseEntered)
                {
                    return;
                }
                CursorLabel.IsVisible = PlotPolygon.IsVisible || PlotSelectedElement.IsVisible || CapturedPlots.Count > 0;
                MaxArrow.IsVisible = PlotPolygon.IsVisible;
                MaxLabel.IsVisible = PlotPolygon.IsVisible;

                MouseEntered = true;

                RedrawPlot();
            }
        }

        private void formsPlot1_MouseLeave(object sender, EventArgs e)
        {
            lock (formsPlot1)
            {
                if (!MouseEntered)
                {
                    return;
                }
                CursorLabel.IsVisible = false;
                MaxArrow.IsVisible = false;
                MaxLabel.IsVisible = false;

                MouseEntered = false;

                RedrawPlot();
            }
        }

        private double TransformIntensity(double rawValue, double lambda)
        {
            rawValue *= Config.IntensityScaling.Calc(lambda);
            rawValue += Config.IntensityOffset.Calc(lambda);

            return rawValue;
        }

        private double TransformLambda(double rawValue)
        {
            rawValue = Config.LambdaMap.Calc(rawValue);

            return rawValue;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            MainThread?.Abort();
            base.OnClosing(e);
        }

        private int PortRead(byte[] buffer, int timeout)
        {
            DateTime start = DateTime.Now;
            DateTime end = start.AddMilliseconds(timeout);

            while (timeout == -1 || (DateTime.Now < end))
            {
                if(Port.BytesToRead >= buffer.Length)
                {
                    break;
                }
            }

            int avail = Math.Min(Port.BytesToRead, buffer.Length);
            if (avail != Port.BytesToRead)
            {
                Console.WriteLine();
            }

            int read = Port.Read(buffer, 0, avail);
            if (read != avail)
            {
                throw new Exception("Failed to read data from serial port");
            }

            return read;
        }

        private void MainFunc()
        {
            byte[] txCommand = new byte[] { 0x45, 0x52, 0x00, 0x00, 0x01, 0x00, 0x02, 0x00, 0x01, 0x00, 0x02, 0x00 };
            byte[] serialReadBuf = new byte[3694 * 2];

            while (true)
            {
                /* send config */
                InsertUint32(txCommand, 2, (uint)Config.TriggerDelay);
                txCommand[6] = (byte)(Config.Trigger ? 0 : 2);
                txCommand[10] = 2;
                Port.Write(txCommand, 0, txCommand.Length);

                CaptureStart = DateTime.Now;
                CaptureEnd = CaptureStart.AddMilliseconds(Config.IcgPeriod / 2000);

                /* send request */
                InsertUint32(txCommand, 2, Config.ShPeriod);
                InsertUint32(txCommand, 6, Config.IcgPeriod);
                txCommand[10] = 0;
                txCommand[11] = 0;
                Port.Write(txCommand, 0, txCommand.Length);

                Console.WriteLine(DateTime.Now + " SH: " + Config.ShPeriod + ", ICG: " + Config.IcgPeriod);

                PortRead(serialReadBuf, -1);

                if(PayloadBuffer.Length != serialReadBuf.Length/2)
                {
                    Array.Resize(ref PayloadBuffer, serialReadBuf.Length / 2);
                }

                for (int pos = 0; pos < serialReadBuf.Length / 2; pos++)
                {
                    PayloadBuffer[pos] = (ushort)((serialReadBuf[pos * 2 + 1] << 8) | serialReadBuf[pos * 2 + 0]);
                    //PayloadBuffer[pos] = (ushort)(0xFFFF * pos / 3694);
                }
                PayloadUsed = serialReadBuf.Length / 2;
                SensorDataReceived = true;
            }
        }

        private void InsertUint32(byte[] buffer, int offset, uint value)
        {
            Array.Copy(BitConverter.GetBytes(value).Reverse().ToArray(), 0, buffer, offset, 4);
        }

        private void Flush()
        {
            byte[] buffer = new byte[8192];

            int timeout = 200;
            DateTime start = DateTime.Now;
            DateTime end = start.AddMilliseconds(timeout);
            int readPos = 0;

            while (DateTime.Now < end)
            {
                if (Port.BytesToRead > 0)
                {
                    Port.Read(buffer, readPos, buffer.Length);
                    end = DateTime.Now.AddMilliseconds(timeout);
                }
            }
        }

        private void chkTrigger_CheckedChanged(object sender, EventArgs e)
        {
            if (chkTrigger.Checked != Config.Trigger)
            {
                Config.Trigger = chkTrigger.Checked;
                Config.Changed = true;
            }
        }

        private void txtShPeriod_TextChanged(object sender, EventArgs e)
        {
            bool success = long.TryParse(txtShPeriod.Text, out long value);

            if (!success)
            {
                txtShPeriod.BackColor = Color.Red;
                return;
            }
        }

        private void txtIcgPeriod_TextChanged(object sender, EventArgs e)
        {
            bool success = long.TryParse(txtIcgPeriod.Text, out long value);

            if (!success)
            {
                txtIcgPeriod.BackColor = Color.Red;
                return;
            }
        }

        private void txtShPeriod_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }
            bool success = long.TryParse(txtShPeriod.Text, out long displayValue);

            if (!success)
            {
                txtShPeriod.BackColor = Color.Red;
                return;
            }

            displayValue = Math.Min(displayValue, uint.MaxValue / 2);
            displayValue = Math.Max(displayValue, 10);

            long rawValue = displayValue * 2;


            txtShPeriod.BackColor = Color.White;
            if (Config.ShPeriod != (uint)rawValue)
            {
                Config.ShPeriod = (uint)rawValue;
                Config.Changed = true;
            }
            if (txtShPeriod.Text != displayValue.ToString())
            {
                txtShPeriod.Text = displayValue.ToString();
            }

            if(Config.ShPeriod > Config.IcgPeriod)
            {
                Config.IcgPeriod = Config.ShPeriod;
                txtIcgPeriod.Text = Config.IcgPeriod.ToString();
            }
        }

        private void txtIcgPeriod_KeyDown(object sender, KeyEventArgs e)
        {
            if(e.KeyCode != Keys.Enter)
            {
                return;
            }
            bool success = long.TryParse(txtIcgPeriod.Text, out long displayValue);

            if (!success)
            {
                txtIcgPeriod.BackColor = Color.Red;
                return;
            }

            displayValue = Math.Min(displayValue, uint.MaxValue / 2);
            displayValue = Math.Max(displayValue, 7388);

            long rawValue = displayValue * 2;

            int fact = (int)((rawValue + Config.ShPeriod - 1) / Config.ShPeriod);
            rawValue = fact * Config.ShPeriod;

            displayValue = rawValue / 2;

            txtIcgPeriod.BackColor = Color.White;
            if (Config.IcgPeriod != (uint)rawValue)
            {
                Config.IcgPeriod = (uint)rawValue;
                Config.Changed = true;
            }
            if (txtIcgPeriod.Text != displayValue.ToString())
            {
                txtIcgPeriod.Text = displayValue.ToString();
            }
        }

        private void txtTriggerDelay_TextChanged(object sender, EventArgs e)
        {
            bool success = long.TryParse(txtTriggerDelay.Text, out long value);

            if (!success)
            {
                txtTriggerDelay.BackColor = Color.Red;
                return;
            }
        }

        private void txtTriggerDelay_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }
            bool success = long.TryParse(txtTriggerDelay.Text, out long value);

            if (!success)
            {
                txtTriggerDelay.BackColor = Color.Red;
                return;
            }

            value = Math.Min(Math.Max(0, value), 65535);

            txtTriggerDelay.BackColor = Color.White;
            if (txtTriggerDelay.Text != value.ToString())
            {
                txtTriggerDelay.Text = value.ToString();
            }
            Config.TriggerDelay = (int)value;
            Config.Changed = true;
        }

        private void btnDark_Click(object sender, EventArgs e)
        {
            ushort[] darkFrame = (ushort[])PayloadBuffer.Clone();

            Config.DarkFrame = darkFrame;
            Config.Changed = true;
        }

        private void BtnCapture_Click(object sender, EventArgs e)
        {
            if(!PlotPolygon.IsVisible)
            {
                return;
            }

            CaptureAdd(PlotPolygon.Xs, PlotPolygon.Ys);
            CalculateMatch(PlotPolygon.Xs, PlotPolygon.Ys);

            PlotRebuild = true;
            PlotFit = true;
            RedrawPlot();
        }

        private void RemoveCapture(SignalPlotXY signalPlotXY)
        {
            lock (formsPlot1)
            {
                CapturedPlots.Remove(signalPlotXY);
            }
            RefreshCaptures();

            PlotRebuild = true;
            PlotFit = true;
            RedrawPlot();
        }

        private void ClearCaptures()
        {
            lock (formsPlot1)
            {
                var keys = CapturedPlots.Where(kvp => kvp.Value.Temporary).Select(kvp => kvp.Key).ToList();
                keys.ForEach(p => CapturedPlots.Remove(p));
            }
            RefreshCaptures();

            PlotRebuild = true;
            PlotFit = true;
            RedrawPlot();
        }

        private void BtnClear_Click(object sender, EventArgs e)
        {
            ClearCaptures();
        }

        private void CaptureAdd(double[] xs, double[] ys)
        {
            double[] Xs = (double[])xs.Clone();
            double[] Ys = (double[])ys.Clone();

            lock (formsPlot1)
            {
                string label = "Captured " + DateTime.Now.ToLongTimeString();
                var plot = formsPlot1.Plot.AddSignalXY(Xs, Ys, label: label);
                SpectrumWindow window = new SpectrumWindow(label) { Wavelengths = Xs, Intensities = Ys };
                CapturedPlots.Add(plot, window);

                window.Color = plot.Color.ToString();
            }
            RefreshCaptures();

            PlotRebuild = true;
            PlotFit = true;
            RedrawPlot();
        }

        private void CalculateMatch(double[] xs, double[] ys)
        {
            double max = ys.Max();
            double[] ysNormalized = ys.Select(v => v / max).ToArray();

            Thread calcThread = new Thread(() => {
                try
                {
                    foreach (var plot in CapturedPlots.Keys)
                    {
                        double match = 0;
                        if (plot.Ys.Max() > 0)
                        {
                            match = MatchSignal(ysNormalized, plot.Ys) * 100;
                        }
                        BeginInvoke(new Action(() =>
                        {
                            CaptureListMap[plot].SubItems[1].Text = match.ToString("0.0");
                        }));
                    }

                    foreach (ElementInfo info in Elements.ElementInfos)
                    {
                        foreach (var elem in info.Elements)
                        {
                            SpectrumWindow[] wins = info.GetWindow(xs, elem.Name);

                            if (wins.Length == 1)
                            {
                                double match = 0;
                                if (wins[0].Intensities.Max() > 0)
                                {
                                    match = MatchSignal(ysNormalized, wins[0].IntensitiesNormalized) * 100;
                                }
                                BeginInvoke(new Action(() =>
                                {
                                    ElementListMap[elem.Name].SubItems[1].Text = match.ToString("0.0");
                                }));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                }
            });
            calcThread.Start();
        }


        private void RefreshCapturedPlots()
        {
            foreach (var kvp in CapturedPlots)
            {
                formsPlot1.Plot.Add(kvp.Key);
            }
        }

        private void RefreshCaptures()
        {
            BeginInvoke(new Action(() =>
            {
                lstCaptures.Items.Clear();
                CaptureListMap.Clear();

                foreach (var cap in CapturedPlots.Keys)
                {
                    var lst = new ListViewItem(new string[] { cap.Label, "" }) { Tag = cap, Checked = cap.IsVisible };
                    lstCaptures.Items.Add(lst);
                    CaptureListMap.Add(cap, lst);
                }
            }));
        }

        private void lstCaptures_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            var p = CaptureListMap.Where(pair => pair.Value == e.Item).FirstOrDefault();

            if(p.Key == null)
            {
                return;
            }

            p.Key.IsVisible = e.Item.Checked;

            PlotUpdateAsync = true;
        }

        private void lstCaptures_SelectedIndexChanged(object sender, EventArgs e)
        {
            foreach (var cap in CapturedPlots.Keys)
            {
                cap.LineWidth = 1;
            }

            if (lstCaptures.SelectedItems.Count == 0)
            {
                PlotUpdateAsync = true;
                return;
            }

            ListViewItem item = lstCaptures.SelectedItems[0];
            SignalPlotXY plot = item.Tag as SignalPlotXY;

            plot.LineWidth = 3;
            PlotUpdateAsync = true;
        }

        private void lstElementLib_MouseDown(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (ModifierKeys == Keys.Alt)
            {
                var elem = lstElementLib.GetItemAt(e.Location.X, e.Location.Y);

                if (elem == null)
                {
                    return;
                }

                ElementInfo info = Elements.Get(elem.Text);
                if (info == null)
                {
                    return;
                }
                double[] Xs = info.Wavelengths;
                double[] Ys = info.Elements.Where(el => el.Name == elem.Text).First().IntensitiesNormalized;

                PlotPolygon.Xs = Xs;
                PlotPolygon.Ys = Ys;
                PlotPolygon.IsVisible = true;
                PlotPolygon.MinRenderIndex = 0;
                PlotPolygon.MaxRenderIndex = Xs.Length - 1;
                PlotPolygon.MinRenderIndex = 0;

                PlotUpdateAsync = true;
                PlotFit = true;
            }
        }

        private void lstElementLib_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (e.CurrentValue == CheckState.Checked && e.NewValue == CheckState.Unchecked)
            {
                LastCheckedElement = null;
            }
            else if(e.NewValue == CheckState.Checked)
            {
                if (LastCheckedElement != null && LastCheckedElement.Checked && LastCheckedElement != lstElementLib.Items[e.Index])
                {
                    LastCheckedElement.Checked = false;
                }
                LastCheckedElement = lstElementLib.Items[e.Index];
            }
        }

        private void lstElementLib_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            UpdateNistSignals();
        }

        void UpdateNistSignals()
        {
            PlotSelectedElement.IsVisible = false;

            if(LastCheckedElement == null || !LastCheckedElement.Checked)
            {
                PlotUpdateAsync = true;
                return;
            }
            string elementName = LastCheckedElement.Text;


            ElementInfo info = Elements.Get(elementName);
            if (info == null)
            {
                PlotUpdateAsync = true;
                return;
            }
            double[] Xs = info.Wavelengths;
            double[] Ys = info.Elements.Where(el => el.Name == elementName).First().IntensitiesNormalized;

            PlotSelectedElement.IsVisible = true;
            PlotSelectedElement.Xs = Xs;
            PlotSelectedElement.Ys = Ys;
            PlotSelectedElement.Label = elementName;
            PlotSelectedElement.MaxRenderIndex = Ys.Length - 1;

            PlotFit = true;
            PlotUpdateAsync = true;
        }

        private double MatchSignal(double[] sample, double[] reference)
        {
            double match = 0;
            double max = 0;

            switch(Config.MatchMethod)
            {
                case "SquaresSum":
                    for (int pos = 0; pos < sample.Length; pos++)
                    {
                        double val = reference[pos] - sample[pos];
                        match += val * val;
                        max += reference[pos] * reference[pos];
                    }
                    if (max != 0)
                    {
                        match /= max;
                    }
                    return 1 - match;

                case "SquaresSumSat":
                    for (int pos = 0; pos < sample.Length; pos++)
                    {
                        double val = Math.Max(0, reference[pos] - sample[pos]);
                        match += val * val;
                        max += reference[pos] * reference[pos];
                    }
                    if (max != 0)
                    {
                        match /= max;
                    }
                    return 1 - match;

                case "Multiply":
                    for (int pos = 0; pos < sample.Length; pos++)
                    {
                        match += sample[pos] * reference[pos];
                        max += sample[pos] * sample[pos];
                    }
                    if (max != 0)
                    {
                        match /= max;
                    }
                    return match;
            }
            return 0;
        }

        private void btnFetch_Click(object sender, EventArgs e)
        {
            string[] elements = new[] { "H", "He", "Li", "Be", "B", "C", "N", "O", "F", "Ne", "Na", "Mg", "Al", "Si", "P", "S", "Cl", "Ar", "K", "Ca", "Sc", "Ti", "V", "Cr", "Mn", "Fe", "Co", "Ni", "Cu", "Zn", "Ga", "Ge", "As", "Se", "Br", "Kr", "Rb", "Sr", "Y", "Zr", "Nb", "Mo", "Tc", "Ru", "Rh", "Pd", "Ag", "Cd", "In", "Sn", "Sb", "Te", "I", "Xe", "Cs", "Ba", "La", "Ce", "Pr", "Nd", "Pm", "Sm", "Eu", "Gd", "Tb", "Dy", "Ho", "Er", "Tm", "Yb", "Lu", "Hf", "Ta", "W", "Re", "Os", "Ir", "Pt", "Au", "Hg", "Tl", "Pb", "Bi", "Po", "At", "Rn", "Fr", "Ra", "Ac", "Th", "Pa", "U", "Np", "Pu", "Am", "Cm", "Bk", "Cf", "Es", "Fm", "Md", "No", "Lr", "Rf", "Db", "Sg", "Bh", "Hs", "Mt", "Ds", "Rg", "Cn", "Nh", "Fl", "Mc", "Lv", "Ts", "Og" };

            int fetched = 0;

            Fetcher.MinWavelength = Config.LibsMinWavelength;
            Fetcher.MaxWavelength = Config.LibsMaxWavelength;
            Fetcher.MaxCharge = Config.LibsMaxCharge;
            Fetcher.Resolution = Config.LibsResolution;
            Fetcher.Temperature = Config.LibsTemperature;

            Thread fetcher = new Thread(() =>
            {
                try
                {
                    foreach (string element in elements)
                    {
                        if (Fetcher.RequestElement(element))
                        {
                            fetched++;
                        }
                        BeginInvoke(new Action(() => btnFetch.Text = "Fetch: " + fetched + "/" + elements.Length));
                    }
                    BeginInvoke(new Action(() =>
                    {
                        btnFetch.Text = "Fetched " + fetched + "/" + elements.Length;
                        Elements = new ElementDatabase();
                        UpdateElements();
                    }));
                }
                catch(Exception ex)
                {

                }
            });

            fetcher.Start();

            MessageBox.Show("Fetching NIST database. Please wait a minute...");
        }

        private void lstElementLib_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            // Determine if clicked column is already the column that is being sorted.
            if (e.Column == lvwColumnSorter.SortColumn)
            {
                // Reverse the current sort direction for this column.
                if (lvwColumnSorter.Order == SortOrder.Ascending)
                {
                    lvwColumnSorter.Order = SortOrder.Descending;
                }
                else
                {
                    lvwColumnSorter.Order = SortOrder.Ascending;
                }
            }
            else
            {
                // Set the column number that is to be sorted; default to ascending.
                lvwColumnSorter.SortColumn = e.Column;
                lvwColumnSorter.Order = SortOrder.Ascending;
            }

            // Perform the sort with these new sort options.
            this.lstElementLib.Sort();
        }

        private void lstCaptures_MouseUp(object sender, MouseEventArgs e)
        {
            bool match = false;

            if (e.Button == MouseButtons.Right)
            {
                foreach (ListViewItem item in lstCaptures.Items)
                {
                    if (item.Bounds.Contains(new Point(e.X, e.Y)))
                    {
                        lstCaptures.ContextMenu = new ContextMenu();
                        lstCaptures.ContextMenu.MenuItems.Add(new MenuItem("Save...", (s, ev) => { SaveCapturedDialog(item.Tag as SignalPlotXY); }));
                        lstCaptures.ContextMenu.MenuItems.Add(new MenuItem("Delete", (s, ev) => { RemoveCapture(item.Tag as SignalPlotXY); }));

                        match = true;
                        break;
                    }
                }

                if (!match)
                {
                    lstCaptures.ContextMenu = new ContextMenu();
                    lstCaptures.ContextMenu.MenuItems.Add(new MenuItem("Load...", (s, ev) => { LoadCapturedDialog(); }));
                    lstCaptures.ContextMenu.MenuItems.Add(new MenuItem("Clear", (s, ev) => { ClearCaptures(); }));
                }
            }
        }

        private void lstCaptures_AfterLabelEdit(object sender, LabelEditEventArgs e)
        {
            if (e.Label == null)
                return;
            lstCaptures.Items[e.Item].Text = e.Label;
            (lstCaptures.Items[e.Item].Tag as SignalPlotXY).Label = e.Label;
        }

        private void lstPoly_MouseUp(object sender, MouseEventArgs e)
        {
            lstPoly.ContextMenu = new ContextMenu();
            lstPoly.ContextMenu.MenuItems.Add(new MenuItem("Reset config", (s, ev) =>
            {
                Config.LambdaMap = new ConfigFile.Polynomial(0, 1, 0, 0, Config.LambdaMap.Description);
                Config.Changed = true;
                PolyfitCalc();
            }));
            lstPoly.ContextMenu.MenuItems.Add(new MenuItem("Apply polynomial", (s, ev) =>
            {
                Config.LambdaMap.Coefficients = LastGeneratedPoly;
                Config.Changed = true;
                PolyfitCalc();
            }) { Enabled = LastGeneratedPoly != null });
        }

        private void lstPolyfit_MouseUp(object sender, MouseEventArgs e)
        {
            bool match = false;

            if (e.Button == MouseButtons.Right)
            {
                foreach (ListViewItem item in lstPolyfit.Items)
                {
                    if (item.Bounds.Contains(new Point(e.X, e.Y)))
                    {
                        lstPolyfit.ContextMenu = new ContextMenu();
                        lstPolyfit.ContextMenu.MenuItems.Add(new MenuItem("Delete", (s, ev) => { lstPolyfit.Items.Remove(item); PolyfitCalc(); }));

                        match = true;
                        break;
                    }
                }

                if (!match)
                {
                    lstPolyfit.ContextMenu = new ContextMenu();
                    bool enabled = true;

                    enabled &= Config.LambdaMap.Coefficients.SequenceEqual(new double[] { 0, 1, 0, 0 });
                    if (!enabled)
                    {
                        lstPolyfit.ContextMenu.MenuItems.Add(new MenuItem("Here you can add measured and expected wavelengths.") { Enabled = false });
                        lstPolyfit.ContextMenu.MenuItems.Add(new MenuItem("To use, first reset polynomial above using right click") { Enabled = false });
                        lstPolyfit.ContextMenu.MenuItems.Add(new MenuItem("---") { Enabled = false });
                    }
                    lstPolyfit.ContextMenu.MenuItems.Add(new MenuItem("Add...", (s, ev) => { AddPolyfitItem(); }) { Enabled = enabled });
                    lstPolyfit.ContextMenu.MenuItems.Add(new MenuItem("Clear", (s, ev) => { lstPolyfit.Items.Clear(); PolyfitCalc(); }));
                }
            }
        }

        private void AddPolyfitItem()
        {
            PolyfitForm polyFitForm = new PolyfitForm();

            polyFitForm.PolyfitAdded += Form_PolyfitAdded;
            polyFitForm.StartPosition = FormStartPosition.Manual;
            polyFitForm.Location = lstPolyfit.PointToScreen(new Point());
            polyFitForm.Show(this);
        }

        private void Form_PolyfitAdded(double sensor, double reference)
        {
            lstPolyfit.Items.Add(new ListViewItem(new string[] { sensor.ToString("0.00"), reference.ToString("0.00") }) { Tag = (sensor, reference) });
            PolyfitCalc();
        }

        private void PolyfitCalc(int order = 3)
        {
            string[] superscript = new string[] { "₀", "₁", "₂", "₃", "₄", "₅", "₆" };
            List<double> x = new List<double>();
            List<double> y = new List<double>();

            foreach (ListViewItem item in lstPolyfit.Items)
            {
                (double sensor, double reference) = (ValueTuple<double, double>)item.Tag;

                x.Add(sensor);
                y.Add(reference);
            }

            lstPoly.Items.Clear();

            if (x.Count < order + 1)
            {
                var poly = Config.LambdaMap.Coefficients;
                for (int ord = 0; ord < poly.Length; ord++)
                {
                    lstPoly.Items.Add(new ListViewItem(new string[] { "x" + superscript[ord], poly[ord].ToString("0.000000000000") }));
                }
                return;
            }

            try
            {
                double[] poly = Polyfit(x.ToArray(), y.ToArray(), order);

                if (poly == null)
                {
                    return;
                }
                for (int ord = 0; ord < poly.Length; ord++)
                {
                    lstPoly.Items.Add(new ListViewItem(new string[] { "x" + superscript[ord], poly[ord].ToString("0.000000000000") }));
                }
                LastGeneratedPoly = poly;
            }
            catch (Exception ex)
            {
            }
        }

        /* https://stackoverflow.com/questions/20786756/use-math-nets-fit-polynomial-method-on-functions-of-multiple-parameters */
        public double[] Polyfit(double[] x, double[] y, int order)
        {
            var design = Matrix<double>.Build.Dense(x.Length, order + 1, (i, j) => Math.Pow(x[i], j));
            return MultipleRegression.QR(design, Vector<double>.Build.Dense(y)).ToArray();
        }
    }
}
