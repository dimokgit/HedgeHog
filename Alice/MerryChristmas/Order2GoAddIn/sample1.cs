/** COM interface to indicators

    List of indicators.

    The example demonstrates loading of the both, standard and custom indicators and then
    lists indicators with their parameters.
  */
using System;
using Microsoft.Win32;

namespace Indicore.Samples
{
    public class Sample1
    {
        //keep always the reference to core outside of the local data in order to 
        //avoid premature releasing of the object by GC
        static Indicore.IndicoreManagerAut core;

        public static void Main(string[] args)
        {
            //get marketscope path
            string path = (string)Registry.GetValue("HKEY_LOCAL_MACHINE\\Software\\CandleWorks\\FXOrder2Go", "InstallPath", "");

            //initialize and load indicators.
            core = new Indicore.IndicoreManagerAut();
            object errors;

            if (!core.LoadFromCAB(path + "\\indicators\\indicators.cab", out errors))
            {
                Console.WriteLine("Load standard: {0}", errors);
            }

            if (!core.LoadFromFolder(path +  "\\indicators\\custom", out errors))
            {
                Console.WriteLine("Load custom: {0}", errors);
            }

            Indicore.IndicatorCollectionAut indicators;
            indicators = (Indicore.IndicatorCollectionAut)core.Indicators;

            foreach(Indicore.IndicatorAut indicator in indicators)
            {
                string type, req;

                if (indicator.Type == indicator.TYPE_OSCILLATOR)
                    type = "oscillator";
                else
                    type = "indicator";

                if (indicator.RequiredSource == indicator.SOURCE_BAR)
                    req = "bars";
                else
                    req = "ticks";

                Console.WriteLine("{0} {1} ({2}) of {3}", type, indicator.ID, indicator.Name, req);

                Indicore.IndicatorParameterCollectionAut parameters = (Indicore.IndicatorParameterCollectionAut)indicator.CreateParameterSet();
                foreach (Indicore.IndicatorParameterAut parameter in parameters)
                {
                    if (parameter.Type == parameter.TYPE_BOOL)
                        type = "Bool";
                    else if (parameter.Type == parameter.TYPE_INT)
                        type = "Int";
                    else if (parameter.Type == parameter.TYPE_DOUBLE)
                        type = "Double";
                    else if (parameter.Type == parameter.TYPE_STRING)
                        type = "String";
                    else if (parameter.Type == parameter.TYPE_COLOR)
                        type = "Color";
                    else
                        type = "Unknown";

                    Console.WriteLine("    {0} ({1}) : {2} = {3}", parameter.ID, parameter.Name, type, parameter.Default);
                }
            }
        }
    }
}

