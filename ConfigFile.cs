using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpectrumPlotter
{
    public class ConfigFile
    {
        public class Polynomial
        {
            public string Description = "";
            public double x0 = 0;
            public double x1 = 0;
            public double x2 = 0;
            public double x3 = 0;

            public Polynomial(double x0, double x1, double x2, double x3, string desc)
            {
                this.x0 = x0;
                this.x1 = x1;
                this.x2 = x2;
                this.x3 = x3;
                this.Description = desc;
            }

            [JsonIgnore]
            public double[] Coefficients
            {
                get => new double[4] { x0, x1, x2, x3 };
                set
                {
                    x0 = value[0];
                    x1 = value[1];
                    x2 = value[2];
                    x3 = value[3];
                }
            }

            public double Calc(double input)
            {
                return x0 + input * x1 + input * input * x2 + input * input * input * x3;
            }
        }

        [JsonIgnore]
        public string Filename = "SensorConfig.json";
        [JsonIgnore]
        public DateTime LoadTime = DateTime.Now;
        [JsonIgnore]
        public bool Default = true;
        [JsonIgnore]
        public bool Changed = true;
        [JsonIgnore]
        private bool LastConfigFailed = false;
        [JsonIgnore]
        public Action ChangedCallback;

        public string MeasurementColor = "SkyBlue";
        public string LibsColor = "Red";
        public Polynomial LambdaMap = new Polynomial(450, 0.2f, 0, 0, "Polynomial to convert pixel number to wavelength. Input is pixel number, output is wavelength.");
        public Polynomial IntensityScaling = new Polynomial(1, 0, 0, 0, "Polynomial to scale intensity with. Input is wavelength, output is scale. Is applied before offset.");
        public Polynomial IntensityOffset = new Polynomial(0, 0, 0, 0, "Polynomial to offset intensity with. Input is wavelength, output is offset. Is applied after scaling.");
        public bool Trigger = false;
        public bool TriggerAutoClear = false;
        public bool TriggerAutoMatch = false;
        public bool TriggerAutoCapture = false;
        public bool TriggerAutoNormalize = false;
        public uint ShPeriod = 10;
        public uint IcgPeriod = 7400;
        public int TriggerDelay = 0;
        public string SerialPort = "COM1";
        public int ResampleResolution = 1024;
        public int LibsMinWavelength = 300;
        public int LibsMaxWavelength = 600;
        public int LibsMaxCharge = 1;
        public int LibsResolution = 500;
        public double LibsTemperature = 1;
        public string MatchMethodDescription = "Use either Multiply, SquaresSum or SquaresSumSat to select matching method";
        public string MatchMethod = "SquaresSumSat";
        public ushort[] DarkFrame = new ushort[0];


        public ConfigFile()
        {
            ChangedCallback += () => { };
        }

        public bool CheckReload()
        {
            DateTime now = DateTime.Now;

            if (Changed)
            {
                Save();
                Changed = false;
            }

            /* only check every 500ms if the file was changed */
            if ((now - LoadTime).TotalMilliseconds > 500)
            {
                /* file on disk is newer */
                if (new FileInfo(Filename).LastWriteTime > LoadTime)
                {
                    ConfigFile newCfg = Load(Filename, out bool valid);

                    if (valid)
                    {
                        IntensityOffset = newCfg.IntensityOffset;
                        IntensityScaling = newCfg.IntensityScaling;
                        LambdaMap = newCfg.LambdaMap;
                        Trigger = newCfg.Trigger;
                        TriggerAutoClear = newCfg.TriggerAutoClear;
                        TriggerAutoMatch = newCfg.TriggerAutoMatch;
                        TriggerAutoCapture = newCfg.TriggerAutoCapture;
                        TriggerAutoNormalize = newCfg.TriggerAutoNormalize;
                        ShPeriod = newCfg.ShPeriod;
                        IcgPeriod = newCfg.IcgPeriod;
                        TriggerDelay = newCfg.TriggerDelay;
                        SerialPort = newCfg.SerialPort;
                        LibsMinWavelength = newCfg.LibsMinWavelength;
                        LibsMaxWavelength = newCfg.LibsMaxWavelength;
                        LibsMaxCharge = newCfg.LibsMaxCharge;
                        LibsResolution = newCfg.LibsResolution;
                        LibsTemperature = newCfg.LibsTemperature;
                        MatchMethod = newCfg.MatchMethod;
                        ResampleResolution = newCfg.ResampleResolution;

                        LoadTime = now;
                        LastConfigFailed = false;

                        ChangedCallback();
                    }
                    else if(!LastConfigFailed)
                    {
                        LastConfigFailed = true;
                        MessageBox.Show("Failed to load config file. Ignoring changes.", "Failed to load config");
                    }

                    return true;
                }
            }

            return false;
        }

        private void Save()
        {
            ConfigFile cfg = new ConfigFile();

            try
            {
                string jsonString = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(Filename, jsonString);
                LoadTime = DateTime.Now;
            }
            catch (Exception ex)
            {
            }
        }

        public static ConfigFile Load(string filename, out bool valid)
        {
            ConfigFile cfg = new ConfigFile();

            valid = false;

            if (!File.Exists(filename))
            {
                cfg.Filename = filename;
                cfg.Save();

                return cfg;
            }

            try
            {
                string jsonString = File.ReadAllText(filename);

                cfg = JsonConvert.DeserializeObject<ConfigFile>(jsonString);
                cfg.LoadTime = DateTime.Now;
                valid = true;
            }
            catch(Exception ex)
            {
            }

            cfg.Filename = filename;

            return cfg;
        }
    }
}
