using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Permissions;
using System.Text;
using System.Threading.Tasks;

namespace SpectrumPlotter.LIBS
{
    public class SpectrumWindow
    {
        public string Name;
        public string Color;
        public double[] Wavelengths;
        public double[] Intensities;
        [JsonIgnore]
        public bool Temporary = true;
        

        public SpectrumWindow(string name)
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
}
