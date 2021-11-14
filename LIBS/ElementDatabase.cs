using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SpectrumPlotter.LIBS
{
    public class ElementDatabase
    {
        public List<ElementInfo> ElementInfos = new List<ElementInfo>();

        public ElementDatabase()
        {
            var di = new DirectoryInfo(Environment.CurrentDirectory);

            foreach(var file in di.GetFiles("LIBS-*.json"))
            {
                var info = ElementInfo.Load(file.FullName);

                if(info != null)
                {
                    ElementInfos.Add(info);
                }
            }
        }

        internal ElementInfo Get(string text)
        {
            return ElementInfos.Where(e => (e.Name == text) || e.Elements.Where(el => el.Name == text).Any()).FirstOrDefault();
        }
    }
}
