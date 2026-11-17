// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 127salva

using System;
using System.IO;
using System.Text;

namespace SalvaOPM
{
    /// <summary>
    /// Everything the app keeps on disk: %ProgramData%\SalvaOPM\state.txt (key=value lines), validated on load,
    /// written atomically (temp file, then replace).
    ///  savedMode       Eco or Balanced (Performance is saved as Balanced)
    ///  baselineTdp     concurrent TDP read before the first Performance write, restored when leaving Performance
    ///  factoryGpu      GPU record read before the app's first GPU write, put back by uninstall
    ///  fanPerformance  auto | custom: which curve Performance uses (Eco and Balanced always use Auto)
    ///  curve           the one custom curve: seven levels at 60..90 C
    /// </summary>
    public sealed class Settings
    {
        public Mode SavedMode = Mode.Balanced;
        public int BaselineTdp = -1;
        public byte[] FactoryGpu;
        public bool PerfCustom;
        public int[] Curve = (int[])FanCurve.DefaultCustom.Clone();

        private readonly string _path;

        public Settings(string directory) { _path = Path.Combine(directory, "state.txt"); }

        public static string DefaultDirectory()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SalvaOPM");
        }

        public static Settings Load(string directory)
        {
            Settings s = new Settings(directory);
            try
            {
                if (!File.Exists(s._path)) return s;
                foreach (string raw in File.ReadAllLines(s._path))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = raw.Substring(0, eq).Trim();
                    string val = raw.Substring(eq + 1).Trim();
                    int v;
                    if (key == "savedMode") s.SavedMode = val == "Eco" ? Mode.Eco : Mode.Balanced;
                    else if (key == "baselineTdp") { if (int.TryParse(val, out v) && v >= HpBios.TdpMin && v <= HpBios.TdpMax) s.BaselineTdp = v; }
                    else if (key == "factoryGpu") s.FactoryGpu = ParseRecord(val);
                    else if (key == "fanPerformance") s.PerfCustom = val == "custom";
                    else if (key == "curve") { int[] c = ParseCurve(val); if (c != null) s.Curve = c; }
                }
            }
            catch (Exception) { /* unreadable file: defaults */ }
            return s;
        }

        /// <summary>A GPU record the app could legally write back, or null.</summary>
        public static byte[] ParseRecord(string val)
        {
            string[] parts = val.Split('-');
            if (parts.Length != 4) return null;
            byte[] b = new byte[4];
            for (int i = 0; i < 4; i++) { if (!byte.TryParse(parts[i], System.Globalization.NumberStyles.HexNumber, null, out b[i])) return null; }
            if (b[0] > 1 || b[1] > 1 || b[2] > 1 || b[3] < HpBios.GpsMin || b[3] > HpBios.GpsMax) return null;
            return b;
        }

        /// <summary>Seven levels, each between OGH's lower bound for that temperature and 48, never falling as temperature rises.</summary>
        public static bool ValidCurve(int[] c)
        {
            if (c == null || c.Length != FanCurve.CustomTemps.Length) return false;
            for (int i = 0; i < c.Length; i++)
            {
                if (c[i] < FanCurve.CustomFloor[i] || c[i] > HpBios.FanMax) return false;
                if (i > 0 && c[i] < c[i - 1]) return false;
            }
            return true;
        }

        /// <summary>A stored curve. Points below OGH's lower bound (a curve saved by an older build) are raised to it.</summary>
        public static int[] ParseCurve(string val)
        {
            string[] parts = val.Split(',');
            if (parts.Length != FanCurve.CustomTemps.Length) return null;
            int[] c = new int[parts.Length];
            for (int i = 0; i < c.Length; i++)
            {
                if (!int.TryParse(parts[i], out c[i]) || c[i] < HpBios.FanMin) return null;
                c[i] = Math.Max(c[i], FanCurve.CustomFloor[i]);
            }
            return ValidCurve(c) ? c : null;
        }

        public bool Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                StringBuilder sb = new StringBuilder();
                sb.Append("savedMode=").Append(SavedMode == Mode.Eco ? "Eco" : "Balanced").Append("\r\n");
                if (BaselineTdp >= 0) sb.Append("baselineTdp=").Append(BaselineTdp).Append("\r\n");
                if (FactoryGpu != null) sb.Append("factoryGpu=").Append(BitConverter.ToString(FactoryGpu)).Append("\r\n");
                sb.Append("fanPerformance=").Append(PerfCustom ? "custom" : "auto").Append("\r\n");
                sb.Append("curve=").Append(string.Join(",", Array.ConvertAll(Curve, delegate(int x) { return x.ToString(); }))).Append("\r\n");
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Encoding.ASCII);
                if (File.Exists(_path)) File.Replace(tmp, _path, null);
                else File.Move(tmp, _path);
                return true;
            }
            catch (Exception) { return false; }
        }
    }
}
