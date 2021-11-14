using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SpectrumPlotter.LIBS
{
    public class ElementInfo
    {
        public class SpectrumValues
        {
            public string Name;
            public double[] Intensities;

            public SpectrumValues(string name)
            {
                Name = name;
            }

            [JsonIgnore]
            public double[] _IntensitiesNormalized = null;

            public double[] IntensitiesNormalized
            {
                get
                {
                    if (_IntensitiesNormalized == null)
                    {
                        double max = Intensities.Max();
                        double[] vals = (double[])Intensities.Clone();
                        _IntensitiesNormalized = vals.Select(v => v / max).ToArray();
                    }
                    return _IntensitiesNormalized;
                }
            }
        }

        public string Name;
        public double[] Wavelengths;
        public SpectrumValues[] Elements = new SpectrumValues[0];

        public ElementInfo(string element)
        {
            Name = element;
        }

        internal void AddElement(string name)
        {
            Array.Resize(ref Elements, Elements.Length + 1);
            Elements[Elements.Length - 1] = new SpectrumValues(name);
        }

        public void Save()
        {
            try
            {
                string jsonString = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText("LIBS-" + Name + ".json", jsonString);
            }
            catch (Exception ex)
            {
            }
        }

        public static ElementInfo Load(string filename)
        {
            ElementInfo cfg = null;
            try
            {
                string jsonString = File.ReadAllText(filename);

                cfg = JsonConvert.DeserializeObject<ElementInfo>(jsonString);
            }
            catch (Exception ex)
            {
            }

            return cfg;
        }

        private void FindWavelengthEntry(ref int wlPos, double v)
        {
            for (int pos = 0; pos < Wavelengths.Length; pos++)
            {
                if (Wavelengths[pos] >= v)
                {
                    wlPos = pos - 1;
                    break;
                }
            }

            if (wlPos < 0)
            {
                wlPos = 0;
            }
        }

        /* return 0 for left, 1 for right value */
        private double GetIntWeight(int wlPos, double wavelength)
        {
            double wl1 = Wavelengths[wlPos];
            double wl2 = Wavelengths[wlPos+1];
            double delta = wl2 - wl1;

            if(delta == 0)
            {
                return 0.0f;
            }

            double weight = ((wavelength - wl1) / delta);

            return weight;
        }

        private double Interpolate(int pos, double[] intensities, double weight)
        {
            double val1 = intensities[pos];
            double val2 = intensities[pos + 1];
            double delta = val2 - val1;

            return val1 + weight * delta;
        }

        public SpectrumWindow[] GetWindow(double[] wavelengths, string text = null)
        {
            List<SpectrumWindow> ret = new List<SpectrumWindow>();
            List<List<double>> intensities = new List<List<double>>();

            List<SpectrumValues> matchedElements = new List<SpectrumValues>();

            if(text == null)
            {
                matchedElements.AddRange(Elements);
            }
            else
            {
                matchedElements.AddRange(Elements.Where(e=>e.Name == text));
            }

            foreach (var el in matchedElements)
            {
                intensities.Add(new List<double>());
                ret.Add(new SpectrumWindow(el.Name));
            }

            int wlPos = 0;
            foreach (double wavelength in wavelengths)
            {
                FindWavelengthEntry(ref wlPos, wavelength);
                double intWeight = GetIntWeight(wlPos, wavelength);

                for(int el = 0; el < matchedElements.Count; el++)
                {
                    double intensity = 0;

                    if (intWeight >= 0 && intWeight <= 1)
                    {
                        intensity = Interpolate(wlPos, matchedElements[el].Intensities, intWeight);
                    }
                    intensities[el].Add(intensity);
                }
            }

            for (int el = 0; el < matchedElements.Count; el++)
            {
                ret[el].Wavelengths = (double[])wavelengths.Clone();
                ret[el].Intensities = intensities[el].ToArray();
            }

            return ret.ToArray();
        }
    }
}
