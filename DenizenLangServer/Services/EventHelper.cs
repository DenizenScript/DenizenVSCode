using FreneticUtilities.FreneticExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DenizenLangServer.Services
{
    public class EventHelper
    {
        public static string Snippetify(string eventLine, int skipArgs)
        {
            string[] parts = eventLine.SplitFast(' ');
            int index = 1;
            StringBuilder snippetBuilder = new StringBuilder(eventLine.Length * 2);
            for (int i = skipArgs; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.StartsWithFast('<') && part.EndsWithFast('>'))
                {
                    part = part.Replace("'", "");
                    snippetBuilder.Append("${" + (index++) + ":" + part[1..^1] + "}");
                }
                else
                {
                    snippetBuilder.Append(part);
                }
                if (i + 1 < parts.Length)
                {
                    snippetBuilder.Append(' ');
                }
            }
            snippetBuilder.Append(':');
            return snippetBuilder.ToString();
        }
    }
}
