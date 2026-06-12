using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TwinCAT.Ads;

namespace AdsSymbolViewer
{
    /// <summary>
    /// Lightweight symbol descriptor holding only the fields the UI needs.
    /// Unlike TcAdsSymbolInfo it stays valid after the loader is disposed.
    /// </summary>
    public class SymbolEntry
    {
        public string Name { get; set; }
        public string TypeName { get; set; }
        public int Size { get; set; }
        public uint IndexGroup { get; set; }
        public uint IndexOffset { get; set; }
        public bool HasChildren { get; set; }
    }

    /// <summary>
    /// Encapsulates all TwinCAT ADS communication: connection lifecycle,
    /// symbol enumeration and raw read/write access by index group/offset.
    /// </summary>
    public sealed class AdsService : IDisposable
    {
        TcAdsClient _client;

        public bool IsConnected => _client?.IsConnected == true;

        public void Connect(string netId, int port)
        {
            Disconnect();
            _client = new TcAdsClient();
            try
            {
                _client.Connect(AmsNetId.Parse(netId), port);
            }
            catch
            {
                _client.Dispose();
                _client = null;
                throw;
            }
        }

        public void Disconnect()
        {
            _client?.Dispose();
            _client = null;
        }

        public StateInfo ReadState() => _client.ReadState();

        /// <summary>
        /// Enumerates every symbol on the target as a flat list.
        /// </summary>
        public List<SymbolEntry> LoadSymbols()
        {
            var list = new List<SymbolEntry>();
#pragma warning disable 0618
            var loader = _client.CreateSymbolInfoLoader();
#pragma warning restore 0618
            foreach (TcAdsSymbolInfo sym in loader)
            {
                list.Add(new SymbolEntry
                {
                    Name = sym.Name ?? "",
                    TypeName = sym.TypeName ?? "",
                    Size = sym.Size,
                    IndexGroup = (uint)sym.IndexGroup,
                    IndexOffset = (uint)sym.IndexOffset,
                    HasChildren = sym.SubSymbols.Count > 0
                });
            }
            return list;
        }

        /// <summary>
        /// Reads a symbol value directly through an AdsStream based on its IEC type.
        /// More reliable than ReadSymbol for all primitive types.
        /// </summary>
        public string ReadValue(SymbolEntry sym)
        {
            string t = (sym.TypeName ?? "").Trim().ToUpperInvariant();
            int sz = sym.Size;

            if (t.StartsWith("STRING"))
            {
                int len = sz > 0 ? sz : 81;
                var st = new AdsStream(len);
                _client.Read(sym.IndexGroup, sym.IndexOffset, st);
                byte[] buf = st.GetBuffer();
                int end = Array.IndexOf(buf, (byte)0, 0, len);
                return Encoding.ASCII.GetString(buf, 0, end < 0 ? len : end);
            }

            if (t.StartsWith("WSTRING"))
            {
                int len = sz > 0 ? sz : 162;
                var st = new AdsStream(len);
                _client.Read(sym.IndexGroup, sym.IndexOffset, st);
                byte[] buf = st.GetBuffer();
                int end = len;
                for (int i = 0; i < len - 1; i += 2)
                    if (buf[i] == 0 && buf[i + 1] == 0) { end = i; break; }
                return Encoding.Unicode.GetString(buf, 0, end);
            }

            if (sym.HasChildren) return $"({sym.TypeName})";

            if (sz <= 0) return "(size=0)";

            var s = new AdsStream(sz);
            _client.Read(sym.IndexGroup, sym.IndexOffset, s);
            s.Position = 0;
            var br = new BinaryReader(s);

            switch (t)
            {
                case "BOOL": return (br.ReadByte() != 0).ToString();
                case "BYTE": case "USINT": return br.ReadByte().ToString();
                case "SINT": return br.ReadSByte().ToString();
                case "WORD": case "UINT": return br.ReadUInt16().ToString();
                case "INT": return br.ReadInt16().ToString();
                case "DWORD": case "UDINT": return br.ReadUInt32().ToString();
                case "DINT": return br.ReadInt32().ToString();
                case "LINT": return br.ReadInt64().ToString();
                case "ULINT": return br.ReadUInt64().ToString();
                case "REAL": return br.ReadSingle().ToString("G", CultureInfo.InvariantCulture);
                case "LREAL": return br.ReadDouble().ToString("G", CultureInfo.InvariantCulture);
                case "TIME": return $"T#{br.ReadUInt32()}ms";
                case "DATE": return DateFromDWord(br.ReadUInt32());
                case "TOD": return TodFromDWord(br.ReadUInt32());
                case "DT": return DtFromDWord(br.ReadUInt32());
            }

            if (sz == 1) return br.ReadByte().ToString();
            if (sz == 2) return br.ReadUInt16().ToString();
            if (sz == 4) return br.ReadUInt32().ToString();
            if (sz == 8) return br.ReadUInt64().ToString();

            return $"({sz} bytes, raw)";
        }

        public string ReadValueSafe(SymbolEntry sym)
        {
            try { return ReadValue(sym); }
            catch { return "ERR"; }
        }

        /// <summary>
        /// Parses the given text according to the symbol's IEC type and writes it.
        /// </summary>
        public void WriteValue(SymbolEntry sym, string text)
        {
            string t = (sym.TypeName ?? "").Trim().ToUpperInvariant();
            int sz = sym.Size;

            if (t.StartsWith("STRING"))
            {
                int len = sz > 0 ? sz : 81;
                var st = new AdsStream(len);
                byte[] bytes = Encoding.ASCII.GetBytes(text);
                st.Write(bytes, 0, Math.Min(bytes.Length, len - 1));
                _client.Write(sym.IndexGroup, sym.IndexOffset, st);
                return;
            }

            if (t.StartsWith("WSTRING"))
            {
                int len = sz > 0 ? sz : 162;
                var st = new AdsStream(len);
                byte[] bytes = Encoding.Unicode.GetBytes(text);
                st.Write(bytes, 0, Math.Min(bytes.Length, len - 2));
                _client.Write(sym.IndexGroup, sym.IndexOffset, st);
                return;
            }

            var s = new AdsStream(sz);
            var bw = new BinaryWriter(s);

            switch (t)
            {
                case "BOOL": bw.Write(text == "1" || text.ToUpper() == "TRUE" ? (byte)1 : (byte)0); break;
                case "BYTE": case "USINT": bw.Write(byte.Parse(text)); break;
                case "SINT": bw.Write(sbyte.Parse(text)); break;
                case "WORD": case "UINT": bw.Write(ushort.Parse(text)); break;
                case "INT": bw.Write(short.Parse(text)); break;
                case "DWORD": case "UDINT": bw.Write(uint.Parse(text)); break;
                case "DINT": bw.Write(int.Parse(text)); break;
                case "LINT": bw.Write(long.Parse(text)); break;
                case "ULINT": bw.Write(ulong.Parse(text)); break;
                case "REAL": bw.Write(float.Parse(text, CultureInfo.InvariantCulture)); break;
                case "LREAL": bw.Write(double.Parse(text, CultureInfo.InvariantCulture)); break;
                default:
                    if (sz == 1) bw.Write(byte.Parse(text));
                    else if (sz == 2) bw.Write(ushort.Parse(text));
                    else if (sz == 4) bw.Write(uint.Parse(text));
                    else throw new NotSupportedException($"Writing is not supported for type: {t}");
                    break;
            }
            _client.Write(sym.IndexGroup, sym.IndexOffset, s);
        }

        public void WriteBool(SymbolEntry sym, bool value)
        {
            var s = new AdsStream(1);
            new BinaryWriter(s).Write(value ? (byte)1 : (byte)0);
            _client.Write(sym.IndexGroup, sym.IndexOffset, s);
        }

        static string DateFromDWord(uint d)
        {
            try { return new DateTime(1970, 1, 1).AddSeconds(d).ToString("yyyy-MM-dd"); }
            catch { return d.ToString(); }
        }

        static string TodFromDWord(uint ms)
            => $"{ms / 3_600_000:D2}:{ms % 3_600_000 / 60_000:D2}:{ms % 60_000 / 1000:D2}.{ms % 1000:D3}";

        static string DtFromDWord(uint d)
        {
            try { return new DateTime(1970, 1, 1).AddSeconds(d).ToString("yyyy-MM-dd HH:mm:ss"); }
            catch { return d.ToString(); }
        }

        public void Dispose() => Disconnect();
    }
}
