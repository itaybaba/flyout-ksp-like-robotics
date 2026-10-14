using System.Text;

namespace RoboticsMod
{
    public static class ModuleBlock
    {
        /// <summary>Formats module data the way native modules do: "\n *Name\n {\n  k=v\n }".</summary>
        public static string Wrap(string name, string body)
        {
            var sb = new StringBuilder();
            sb.Append("\n *").Append(name).Append("\n {");
            foreach (var line in (body ?? "").Split('\n'))
            {
                var l = line.Trim();
                if (l.Length == 0) continue;
                sb.Append("\n  ").Append(l);
            }
            sb.Append("\n }");
            return sb.ToString();
        }
    }
}
