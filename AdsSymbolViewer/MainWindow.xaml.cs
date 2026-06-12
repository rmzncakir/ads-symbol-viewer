using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TwinCAT.Ads;

namespace AdsSymbolViewer
{
    public class WatchItem : INotifyPropertyChanged
    {
        private string _value, _timestamp;
        public string Name { get; set; }
        public string TypeName { get; set; }
        public SymbolEntry Symbol { get; set; }

        public string Value
        {
            get => _value;
            set { _value = value; Notify(nameof(Value)); }
        }
        public string Timestamp
        {
            get => _timestamp;
            set { _timestamp = value; Notify(nameof(Timestamp)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void Notify(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    public partial class MainWindow : Window
    {
        readonly AdsService _ads = new AdsService();
        SymbolEntry _selected;

        List<SymbolEntry> _allSymbols = new List<SymbolEntry>();

        readonly ObservableCollection<WatchItem> _watch = new ObservableCollection<WatchItem>();
        readonly DispatcherTimer _timer = new DispatcherTimer();

        bool _boolChanging = false;

        List<string> _lastConn = new List<string>();
        static readonly string SettingsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AdsSymbolViewer", "connections.txt");

        public MainWindow()
        {
            InitializeComponent();
            cbNetId.Text = AmsNetId.Local.ToString();
            cbPort.Text = "851";
            dgWatch.ItemsSource = _watch;
            _timer.Tick += Timer_Tick;
            _timer.Interval = TimeSpan.FromMilliseconds(500);
            LoadLastConn();
            SetStatusDisconnected();
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer.Stop();
            Disconnect();
            SaveLastConn();
            base.OnClosed(e);
        }

        void Window_Closing(object sender, CancelEventArgs e) { }

        void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_ads.IsConnected)
            {
                Disconnect();
                btnConnect.Content = "Connect";
                btnLoadSymbols.IsEnabled = false;
                tvSymbols.Items.Clear();
                _allSymbols.Clear();
                ClearDetail();
                SetStatusDisconnected();
                Log("Disconnected.");
            }
            else Connect();
        }

        void Connect()
        {
            string netStr = cbNetId.Text?.Trim();
            if (string.IsNullOrWhiteSpace(netStr)) { Log("NetId cannot be empty."); return; }
            if (!int.TryParse(cbPort.Text?.Trim(), out int port)) { Log("Invalid port."); return; }

            try
            {
                _ads.Connect(netStr, port);

                btnConnect.Content = "Disconnect";
                btnLoadSymbols.IsEnabled = true;

                string entry = $"{netStr}:{port}";
                _lastConn.Remove(entry);
                _lastConn.Insert(0, entry);
                if (_lastConn.Count > 10) _lastConn.RemoveAt(_lastConn.Count - 1);
                RefreshNetIdCombo();

                UpdateStatusBadges();
                Log($"Connected → {netStr}:{port}");
            }
            catch (Exception ex)
            {
                Log($"Connection error: {ex.Message}");
            }
        }

        void Disconnect()
        {
            _timer.Stop();
            if (tglRefresh.IsChecked == true) tglRefresh.IsChecked = false;
            _ads.Disconnect();
            _allSymbols.Clear();
        }

        async void btnLoadSymbols_Click(object sender, RoutedEventArgs e)
        {
            if (!_ads.IsConnected) { Log("Connect first."); return; }

            tvSymbols.Items.Clear();
            _allSymbols.Clear();
            btnLoadSymbols.IsEnabled = false;
            btnLoadSymbols.Content = "Loading…";

            try
            {
                var flat = await Task.Run(() => _ads.LoadSymbols());

                _allSymbols = flat;

                var topGroups = new Dictionary<string, TreeViewItem>();

                foreach (var sym in flat)
                {
                    if (sym.Name.Contains('[')) continue;

                    int dot = sym.Name.IndexOf('.');
                    if (dot < 0)
                    {
                        tvSymbols.Items.Add(MakeShallowNode(sym));
                    }
                    else
                    {
                        string prefix = sym.Name.Substring(0, dot);
                        if (!topGroups.ContainsKey(prefix))
                        {
                            var grpNode = new TreeViewItem
                            {
                                Header = new TextBlock
                                {
                                    Text = prefix,
                                    FontSize = 13,
                                    FontWeight = FontWeights.SemiBold,
                                    Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39))
                                },
                                Tag = prefix
                            };
                            grpNode.Items.Add(new TreeViewItem { Header = "…" });
                            grpNode.Expanded += GroupNode_Expanded;
                            topGroups[prefix] = grpNode;
                            tvSymbols.Items.Add(grpNode);
                        }
                    }
                }

                Log($"Loaded {topGroups.Count} groups, {flat.Count} symbols.");
            }
            catch (Exception ex) { Log($"Load error: {ex.Message}"); }
            finally
            {
                btnLoadSymbols.IsEnabled = true;
                btnLoadSymbols.Content = "Load Symbols";
            }
        }

        void GroupNode_Expanded(object sender, RoutedEventArgs e)
        {
            var node = (TreeViewItem)sender;
            if (!(node.Items.Count == 1 && node.Items[0] is TreeViewItem ph && ph.Header as string == "…"))
                return;
            if (!(node.Tag is string prefix)) return;

            node.Items.Clear();

            foreach (var sym in _allSymbols)
            {
                if (!sym.Name.StartsWith(prefix + ".")) continue;
                string rest = sym.Name.Substring(prefix.Length + 1);
                if (rest.Contains('.') || rest.Contains('[')) continue;
                node.Items.Add(MakeShallowNode(sym));
            }
        }

        void SymbolNode_Expanded(object sender, RoutedEventArgs e)
        {
            var node = (TreeViewItem)sender;
            if (!(node.Items.Count == 1 && node.Items[0] is TreeViewItem ph && ph.Header as string == "…"))
                return;
            if (!(node.Tag is SymbolEntry sym)) return;

            node.Items.Clear();

            foreach (var child in _allSymbols)
            {
                bool isDot = child.Name.StartsWith(sym.Name + ".");
                bool isBracket = child.Name.StartsWith(sym.Name + "[");

                if (!isDot && !isBracket) continue;

                if (isDot)
                {
                    // Struct member: direct members only, no nested or array elements
                    string rest = child.Name.Substring(sym.Name.Length + 1);
                    if (rest.Contains('.') || rest.Contains('[')) continue;
                }
                else
                {
                    // Array element: only plain [n], not [n].field or [n][m]
                    string rest = child.Name.Substring(sym.Name.Length);
                    int close = rest.IndexOf(']');
                    if (close < 0) continue;
                    if (rest.Substring(close + 1).Length > 0) continue;
                }

                node.Items.Add(MakeShallowNode(child));
            }
        }

        TreeViewItem MakeShallowNode(SymbolEntry sym)
        {
            var node = new TreeViewItem
            {
                Header = BuildHeader(sym),
                Tag = sym,
                ToolTip = $"{sym.Name}\nType: {sym.TypeName}\n" +
                          $"IG: 0x{sym.IndexGroup:X8}  IO: 0x{sym.IndexOffset:X8}\n" +
                          $"Size: {sym.Size} bytes"
            };

            if (sym.HasChildren)
            {
                node.Items.Add(new TreeViewItem { Header = "…" });
                node.Expanded += SymbolNode_Expanded;
            }
            return node;
        }

        StackPanel BuildHeader(SymbolEntry sym)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };

            var badge = new Border
            {
                Background = TypeBrush(sym.TypeName),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(3, 1, 3, 1),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = new TextBlock
            {
                Text = TypeAbbrev(sym.TypeName),
                Foreground = Brushes.White,
                FontSize = 9,
                FontWeight = FontWeights.Bold
            };
            sp.Children.Add(badge);

            string leaf = sym.Name.Contains('.')
                ? sym.Name.Substring(sym.Name.LastIndexOf('.') + 1)
                : sym.Name;

            sp.Children.Add(new TextBlock
            {
                Text = leaf,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39))
            });
            return sp;
        }

        void tvSymbols_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (tvSymbols.SelectedItem is TreeViewItem node && node.Tag is SymbolEntry sym)
            {
                _selected = sym;
                ShowDetail(_selected);

                bool conn = _ads.IsConnected;
                btnRead.IsEnabled = conn;
                btnAddToWatch.IsEnabled = conn;
            }
        }

        void ShowDetail(SymbolEntry sym)
        {
            tbName.Text = sym.Name;
            tbTypeName.Text = sym.TypeName;
            tbSize.Text = sym.Size.ToString();
            tbIndexGroup.Text = $"0x{sym.IndexGroup:X8}";
            tbIndexOffset.Text = $"0x{sym.IndexOffset:X8}";

            if (_ads.IsConnected)
                UpdateValueControl(sym);
        }

        void ClearDetail()
        {
            tbName.Text = tbTypeName.Text = tbSize.Text =
            tbIndexGroup.Text = tbIndexOffset.Text = tbValue.Text = "";
            _selected = null;

            tbValue.Visibility = Visibility.Visible;
            chkBool.Visibility = Visibility.Collapsed;
            cbEnum.Visibility = Visibility.Collapsed;

            btnRead.IsEnabled = btnWrite.IsEnabled = btnAddToWatch.IsEnabled = false;
        }

        /// <summary>
        /// Shows the appropriate value editor for the symbol type and loads the
        /// current value. BOOL → ToggleButton, everything else → TextBox.
        /// </summary>
        void UpdateValueControl(SymbolEntry sym)
        {
            string t = (sym.TypeName ?? "").Trim().ToUpperInvariant();

            tbValue.Visibility = Visibility.Collapsed;
            chkBool.Visibility = Visibility.Collapsed;
            cbEnum.Visibility = Visibility.Collapsed;
            btnWrite.IsEnabled = false;

            if (t == "BOOL")
            {
                chkBool.Visibility = Visibility.Visible;
                chkBool.IsEnabled = _ads.IsConnected;
                try
                {
                    _boolChanging = true;
                    bool val = _ads.ReadValue(sym) == "True";
                    chkBool.IsChecked = val;
                    chkBool.Content = val ? "TRUE" : "FALSE";
                    _boolChanging = false;
                }
                catch { _boolChanging = false; }
                return;
            }

            tbValue.Visibility = Visibility.Visible;
            btnWrite.IsEnabled = _ads.IsConnected;
            try { tbValue.Text = _ads.ReadValue(sym); }
            catch (Exception ex) { tbValue.Text = $"ERR: {ex.Message}"; }
        }

        void btnRead_Click(object sender, RoutedEventArgs e)
        {
            // Don't overwrite the value box while the user is editing it
            if (_selected != null && !tbValue.IsKeyboardFocused)
                UpdateValueControl(_selected);
        }

        void btnWrite_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null || !_ads.IsConnected) return;
            if (tbValue.Visibility != Visibility.Visible) return;
            try
            {
                _ads.WriteValue(_selected, tbValue.Text);
                Log($"Wrote → {_selected.Name} = {tbValue.Text}");
                UpdateValueControl(_selected);
            }
            catch (Exception ex)
            {
                Log($"Write error [{_selected.Name}]: {ex.Message}");
                MessageBox.Show($"Write failed:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void chkBool_Changed(object sender, RoutedEventArgs e)
        {
            if (_boolChanging || _selected == null || !_ads.IsConnected) return;
            try
            {
                bool val = chkBool.IsChecked == true;
                _ads.WriteBool(_selected, val);
                chkBool.Content = val ? "TRUE" : "FALSE";
                Log($"Wrote → {_selected.Name} = {val}");
            }
            catch (Exception ex) { Log($"Write error [{_selected.Name}]: {ex.Message}"); }
        }

        void cbEnum_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        void btnAddToWatch_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            if (_watch.Any(w => w.Name == _selected.Name)) { Log($"Already watching: {_selected.Name}"); return; }

            _watch.Add(new WatchItem
            {
                Name = _selected.Name,
                TypeName = _selected.TypeName,
                Value = _ads.ReadValueSafe(_selected),
                Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                Symbol = _selected
            });
            Log($"Added to watch list: {_selected.Name}");
        }

        void btnRemoveWatch_Click(object sender, RoutedEventArgs e)
        {
            if (dgWatch.SelectedItem is WatchItem item) _watch.Remove(item);
        }

        void tglRefresh_Checked(object sender, RoutedEventArgs e)
        {
            tglRefresh.Content = "▶ Running";
            _timer.Start();
        }

        void tglRefresh_Unchecked(object sender, RoutedEventArgs e)
        {
            tglRefresh.Content = "⏸ Stopped";
            _timer.Stop();
        }

        void cbInterval_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cbInterval.SelectedItem is ComboBoxItem ci && ci.Tag != null)
            {
                bool was = _timer.IsEnabled;
                _timer.Stop();
                _timer.Interval = TimeSpan.FromMilliseconds(int.Parse(ci.Tag.ToString()));
                if (was) _timer.Start();
            }
        }

        void Timer_Tick(object sender, EventArgs e)
        {
            if (!_ads.IsConnected) return;

            string now = DateTime.Now.ToString("HH:mm:ss");
            foreach (var item in _watch.ToList())
            {
                string v = _ads.ReadValueSafe(item.Symbol);
                if (item.Value == v) continue;
                item.Value = v;
                item.Timestamp = now;
            }

            UpdateStatusBadges();
        }

        void tbFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            tbFilterPlaceholder.Visibility =
                string.IsNullOrEmpty(tbFilter.Text) ? Visibility.Visible : Visibility.Collapsed;

            string f = tbFilter.Text.ToUpperInvariant().Trim();
            foreach (TreeViewItem node in tvSymbols.Items)
                FilterNode(node, f);
        }

        bool FilterNode(TreeViewItem node, string f)
        {
            var sym = node.Tag as SymbolEntry;
            bool self = string.IsNullOrEmpty(f)
                         || (sym?.Name.ToUpperInvariant().Contains(f) == true)
                         || (node.Tag is string prefix && prefix.ToUpperInvariant().Contains(f));
            bool child = node.Items.OfType<TreeViewItem>().Any(c => FilterNode(c, f));

            node.Visibility = (self || child) ? Visibility.Visible : Visibility.Collapsed;
            if (child && !string.IsNullOrEmpty(f)) node.IsExpanded = true;
            return self || child;
        }

        void btnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_allSymbols.Count == 0) { Log("Load symbols first."); return; }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV|*.csv",
                FileName = $"symbols_{DateTime.Now:yyyyMMdd_HHmm}.csv"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Name;Type;Size;IndexGroup;IndexOffset");
                foreach (var sym in _allSymbols)
                    sb.AppendLine($"{sym.Name};{sym.TypeName};{sym.Size};{sym.IndexGroup};{sym.IndexOffset}");
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                Log($"CSV saved: {dlg.FileName}");
            }
            catch (Exception ex) { Log($"CSV error: {ex.Message}"); }
        }

        void SetStatusDisconnected()
        {
            Badge(bdConn, tbConn, "Offline", "#E5E7EB", "#6B7280");
            Badge(bdAds, tbAds, "—", "#E5E7EB", "#6B7280");
        }

        void UpdateStatusBadges()
        {
            if (!_ads.IsConnected) { SetStatusDisconnected(); return; }
            Badge(bdConn, tbConn, "Online", "#DCFCE7", "#166534");
            try
            {
                StateInfo si = _ads.ReadState();
                switch (si.AdsState)
                {
                    case AdsState.Run: Badge(bdAds, tbAds, "RUN", "#DCFCE7", "#166534"); break;
                    case AdsState.Stop: Badge(bdAds, tbAds, "STOP", "#FEE2E2", "#991B1B"); break;
                    case AdsState.Config: Badge(bdAds, tbAds, "CONFIG", "#DBEAFE", "#1E40AF"); break;
                    case AdsState.Error: Badge(bdAds, tbAds, "ERROR", "#FEE2E2", "#991B1B"); break;
                    default: Badge(bdAds, tbAds, si.AdsState.ToString(), "#E5E7EB", "#4B5563"); break;
                }
            }
            catch { Badge(bdAds, tbAds, "?", "#FEF3C7", "#92400E"); }
        }

        static void Badge(Border bd, TextBlock tb, string label, string bg, string fg)
        {
            bd.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg));
            tb.Text = label;
            tb.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg));
        }

        void cbNetId_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cbNetId.SelectedItem is string entry && entry.Contains(':'))
            {
                var parts = entry.Split(':');
                if (parts.Length == 2) { cbNetId.Text = parts[0]; cbPort.Text = parts[1]; }
            }
        }

        void LoadLastConn()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    _lastConn = new List<string>(File.ReadAllLines(SettingsFile));
                    RefreshNetIdCombo();
                }
            }
            catch { }
        }

        void SaveLastConn()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
                File.WriteAllLines(SettingsFile, _lastConn);
            }
            catch { }
        }

        void RefreshNetIdCombo()
        {
            string cur = cbNetId.Text;
            cbNetId.Items.Clear();
            foreach (var c in _lastConn) cbNetId.Items.Add(c);
            cbNetId.Text = cur;
        }

        void Log(string msg)
        {
            tbLog.AppendText($"[{DateTime.Now:HH:mm:ss}]  {msg}\n");
            tbLog.ScrollToEnd();
        }

        void btnClearLog_Click(object sender, RoutedEventArgs e) => tbLog.Clear();

        static string TypeAbbrev(string t)
        {
            if (string.IsNullOrEmpty(t)) return "?";
            string u = t.ToUpperInvariant();
            if (u == "BOOL") return "BOOL";
            if (u == "REAL" || u == "LREAL") return "REAL";
            if (u.StartsWith("STRING") || u.StartsWith("WSTRING")) return "STR";
            if (u.StartsWith("ARRAY")) return "ARR";
            if (u == "INT" || u == "DINT" || u == "LINT" ||
                u == "UINT" || u == "UDINT" || u == "ULINT" ||
                u == "SINT" || u == "USINT") return "INT";
            if (u == "BYTE") return "BYTE";
            if (u == "WORD") return "WORD";
            if (u == "DWORD") return "DWRD";
            if (u == "TIME") return "TIME";
            if (u == "DATE") return "DATE";
            if (u == "TOD") return "TOD";
            if (u == "DT") return "DT";
            return u.Length > 4 ? u.Substring(0, 4) : u;
        }

        static SolidColorBrush TypeBrush(string t)
        {
            if (string.IsNullOrEmpty(t))
                return new SolidColorBrush(Color.FromRgb(107, 114, 128));
            string u = t.ToUpperInvariant();
            if (u == "BOOL")
                return new SolidColorBrush(Color.FromRgb(37, 99, 235));
            if (u == "INT" || u == "DINT" || u == "LINT" ||
                u == "UINT" || u == "UDINT" || u == "ULINT" ||
                u == "SINT" || u == "USINT" ||
                u == "BYTE" || u == "WORD" || u == "DWORD")
                return new SolidColorBrush(Color.FromRgb(22, 163, 74));
            if (u == "REAL" || u == "LREAL")
                return new SolidColorBrush(Color.FromRgb(234, 88, 12));
            if (u.StartsWith("STRING") || u.StartsWith("WSTRING"))
                return new SolidColorBrush(Color.FromRgb(124, 58, 237));
            if (u.StartsWith("ARRAY"))
                return new SolidColorBrush(Color.FromRgb(13, 148, 136));
            if (u == "TIME" || u == "DATE" || u == "TOD" || u == "DT")
                return new SolidColorBrush(Color.FromRgb(225, 29, 72));
            return new SolidColorBrush(Color.FromRgb(147, 51, 234));
        }
    }
}
