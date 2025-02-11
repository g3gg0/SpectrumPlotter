using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpectrumPlotter.LIBS
{
    public class Fetcher
    {
        public static int MinWavelength = 300;
        public static int MaxWavelength = 900;
        public static int MaxCharge = 1;
        public static int Resolution = 500;
        public static double Temperature = 1;
        

        [JsonArrayAttribute]
        public class SpectralTable
        {

        }

        public static bool RequestElement(string element)
        {
            try
            {
                string url = "https://physics.nist.gov/cgi-bin/ASD/lines1.pl?composition=" + element + "%3A100&mytext[]=" + element + "&myperc[]=100&spectra=" + element + "0-" + MaxCharge + "&low_w=" + MinWavelength + "&limits_type=0&upp_w=" + MaxWavelength + "&show_av=2&unit=1&resolution=" + Resolution + "&temp=" + Temperature.ToString("0.0000", CultureInfo.InvariantCulture) + "&eden=1e17&maxcharge=" + MaxCharge + "&min_rel_int=0.01&libs=1";

                HttpClient client = new HttpClient();

                Task<HttpResponseMessage> ret = client.GetAsync(url);
                if (!ret.Wait(10000))
                {
                    MessageBox.Show("Failed to retrieve, server didn't respond");
                    return false;
                }
                var resp = ret.Result.Content.ReadAsStringAsync();
                if (!resp.Wait(10000))
                {
                    MessageBox.Show("Failed to retrieve, server didn't respond with content");
                    return false;
                }

                string responseBody = resp.Result;

                if(!responseBody.Contains("var dataDopplerArray=") || !responseBody.Contains("    var dataSticksArray="))
                {
                    return false;
                }

                string assignStart = responseBody.Substring(responseBody.IndexOf("var dataDopplerArray=") + 21);
                string assignContent = assignStart.Substring(0, assignStart.IndexOf("    var dataSticksArray=")).Replace("\r", "").Replace("\n", "").Trim(';');

                JArray data = JsonConvert.DeserializeObject(assignContent) as JArray;
                ElementInfo spectra = new ElementInfo(element);
                List<double> wavelengths = new List<double>();
                List<List<double>> elementIntensities = new List<List<double>>();

                JArray header = data[0] as JArray;
                int elementStart = 1;

                for (int pos = 1; pos < header.Count; pos++)
                {
                    JObject elementInfo = header[pos] as JObject;
                    string name = elementInfo["label"].ToString();

                    if (name.StartsWith("Sum"))
                    {
                        elementStart = 2;
                    }
                    else
                    {
                        spectra.AddElement(name);
                        elementIntensities.Add(new List<double>());
                    }
                }
                int elements = header.Count - elementStart;

                for (int pos = 1; pos < data.Count; pos++)
                {
                    JArray spectrumInfo = data[pos] as JArray;
                    double wavelength = ((double)spectrumInfo[0]);
                    double sum = ((double)spectrumInfo[1]);

                    wavelengths.Add(wavelength);
                    for (int posw = 0; posw < elements; posw++)
                    {
                        double intensity = 0;

                        if (spectrumInfo[elementStart + posw].Type == JTokenType.Float)
                        {
                            intensity = ((double)spectrumInfo[elementStart + posw]);
                        }
                        elementIntensities[posw].Add(intensity);
                    }
                }

                spectra.Wavelengths = wavelengths.ToArray();
                for (int pos = 0; pos < elements; pos++)
                {
                    spectra.Elements[pos].Intensities = elementIntensities[pos].ToArray();
                }

                spectra.Save();

                return true;
            }
            catch(Exception ex)
            {
            }

            return false;
        }
    }
}
