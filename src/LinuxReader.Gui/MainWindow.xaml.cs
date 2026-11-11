using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LinuxReader.Core.Ext4;
using LinuxReader.Core.IO;
using LinuxReader.Core.Partitions;
using Microsoft.Win32;

namespace LinuxReader.Gui;

public partial class MainWindow : Window
{
    private Stream? _activeDiskStream;
    private string? _activeDevicePath;
    private Ext4FileSystem? _activeFileSystem;
    private List<Ext4Item> _currentFolderItems = new();
    private Ext4Item? _selectedItem;
    private string? _cachedTextPreview;
    private string? _cachedHexPreview;
    private BitmapImage? _cachedImagePreview;

    public class SourceItem
    {
        public string DisplayName { get; init; } = string.Empty;
        public string PathOrDevice { get; init; } = string.Empty;
        public PartitionInfo? Partition { get; init; }
        public bool IsImage { get; init; }
        public bool IsAccessible { get; init; } = true;
    }

    public class DisplayFileItem
    {
        public string Name { get; init; } = string.Empty;
        public string FormattedSize { get; init; } = string.Empty;
        public string Permissions { get; init; } = string.Empty;
        public string ModifiedFormatted { get; init; } = string.Empty;
        public string FileTypeName { get; init; } = string.Empty;
        public Ext4Item SourceItem { get; init; } = null!;
    }

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateAdminStatusUI();

        if (DeviceManager.IsAdministrator())
        {
            // Administrator mode detected: automatically scan physical hard drives on startup
            ScanPhysicalDrives(autoSelectFirstLinux: true);
        }
        else
        {
            TxtStatus.Text = "Running as Standard User. Raw physical drives require Administrator rights to inspect. Click 'Scan Hard Drives' to elevate or 'Open Disk Image'.";
        }
    }

    private void UpdateAdminStatusUI()
    {
        bool isAdmin = DeviceManager.IsAdministrator();
        if (isAdmin)
        {
            BadgeAdminStatus.Background = new SolidColorBrush(Color.FromRgb(6, 95, 70));
            TxtAdminIcon.Text = "🛡️";
            TxtAdminStatus.Text = "ADMINISTRATOR MODE";
            TxtAdminStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            BtnElevate.Visibility = Visibility.Collapsed;
        }
        else
        {
            BadgeAdminStatus.Background = new SolidColorBrush(Color.FromRgb(120, 53, 15));
            TxtAdminIcon.Text = "⚠️";
            TxtAdminStatus.Text = "STANDARD USER";
            TxtAdminStatus.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
            BtnElevate.Visibility = Visibility.Visible;
        }
    }

    private void BtnElevate_Click(object sender, RoutedEventArgs e)
    {
        RestartAsAdministrator();
    }

    private void RestartAsAdministrator()
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                exePath = Environment.GetCommandLineArgs()[0];
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(startInfo);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not restart application with elevated privileges: {ex.Message}",
                "Elevation Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnScanDrives_Click(object sender, RoutedEventArgs e)
    {
        if (!DeviceManager.IsAdministrator())
        {
            var result = MessageBox.Show(
                "Accessing physical hard drives directly requires Administrator privileges.\n\nWould you like to restart Linux Reader as Administrator now?",
                "Administrator Rights Required",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                RestartAsAdministrator();
                return;
            }
        }

        ScanPhysicalDrives(autoSelectFirstLinux: true);
    }

    private void ScanPhysicalDrives(bool autoSelectFirstLinux)
    {
        try
        {
            TxtStatus.Text = "Scanning physical hard drives in strict read-only mode...";
            var drives = DeviceManager.GetPhysicalDrives();

            if (drives.Count == 0)
            {
                TxtStatus.Text = "No physical drives detected.";
                MessageBox.Show(
                    "No physical drives detected on this machine.\nTip: You can open disk image files (.img, .iso, .ext4) directly.",
                    "Scan Hard Drives", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sourceList = new List<SourceItem>();
            int firstLinuxIndex = -1;

            foreach (var drive in drives)
            {
                if (!drive.IsAccessible)
                {
                    sourceList.Add(new SourceItem
                    {
                        DisplayName = $"🔒 {drive.FriendlyName} ({drive.FormattedSize}) [Requires Admin]",
                        PathOrDevice = drive.DevicePath,
                        IsImage = false,
                        Partition = null,
                        IsAccessible = false
                    });
                    continue;
                }

                try
                {
                    using var probeStream = SafeDiskStream.OpenPhysicalDrive(drive.DevicePath, drive.SizeBytes);
                    var partitions = PartitionScanner.Scan(probeStream);

                    if (partitions.Count == 0)
                    {
                        sourceList.Add(new SourceItem
                        {
                            DisplayName = $"💾 {drive.FriendlyName} ({drive.FormattedSize}) [No recognized partitions]",
                            PathOrDevice = drive.DevicePath,
                            IsImage = false,
                            Partition = null,
                            IsAccessible = true
                        });
                    }
                    else
                    {
                        foreach (var p in partitions)
                        {
                            string linuxBadge = p.IsLinux ? " 🐧 [Linux Ext4]" : "";
                            sourceList.Add(new SourceItem
                            {
                                DisplayName = $"💾 {drive.FriendlyName} - Part {p.PartitionIndex}: {p.Name} ({p.FormattedSize}){linuxBadge}",
                                PathOrDevice = drive.DevicePath,
                                Partition = p,
                                IsImage = false,
                                IsAccessible = true
                            });

                            if (firstLinuxIndex == -1 && p.IsLinux)
                            {
                                firstLinuxIndex = sourceList.Count - 1;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    sourceList.Add(new SourceItem
                    {
                        DisplayName = $"⚠️ {drive.FriendlyName} - [Error reading partitions: {ex.Message}]",
                        PathOrDevice = drive.DevicePath,
                        IsImage = false,
                        Partition = null,
                        IsAccessible = false
                    });
                }
            }

            if (sourceList.Count == 0)
            {
                TxtStatus.Text = "No readable partitions found.";
                return;
            }

            CmbSources.ItemsSource = sourceList;

            if (autoSelectFirstLinux && firstLinuxIndex >= 0)
            {
                CmbSources.SelectedIndex = firstLinuxIndex;
                TxtStatus.Text = $"Detected {drives.Count} drive(s). Automatically mounted Linux partition.";
            }
            else
            {
                CmbSources.SelectedIndex = 0;
                TxtStatus.Text = $"Detected {drives.Count} drive(s) with {sourceList.Count} source partition(s).";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to scan physical drives: {ex.Message}", "Scan Error", MessageBoxButton.OK, MessageBoxImage.Error);
            TxtStatus.Text = $"Scan failed: {ex.Message}";
        }
    }

    private void BtnOpenImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Linux Disk Image or Partition File",
            Filter = "Disk Images (*.img;*.raw;*.iso;*.ext4;*.vhd;*.bin)|*.img;*.raw;*.iso;*.ext4;*.vhd;*.bin|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            LoadDiskSource(dialog.FileName, isImage: true);
        }
    }

    private void LoadDiskSource(string sourcePath, bool isImage)
    {
        try
        {
            CleanUpResources();
            string fileName = Path.GetFileName(sourcePath);
            TxtStatus.Text = $"Opening {fileName} in strict read-only mode...";

            using var probeStream = isImage
                ? SafeDiskStream.OpenFileImage(sourcePath)
                : SafeDiskStream.OpenPhysicalDrive(sourcePath);

            var partitions = PartitionScanner.Scan(probeStream);
            var sourceList = new List<SourceItem>();

            if (partitions.Count == 0)
            {
                MessageBox.Show("No GPT, MBR, or Ext4 partitions found on the selected target.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                TxtStatus.Text = "No valid partitions detected.";
                return;
            }

            int bestLinuxIndex = -1;
            for (int i = 0; i < partitions.Count; i++)
            {
                var p = partitions[i];
                string linuxBadge = p.IsLinux ? " 🐧 [Linux Ext4]" : "";
                sourceList.Add(new SourceItem
                {
                    DisplayName = $"📂 {fileName} - Part {p.PartitionIndex}: {p.Name} ({p.FormattedSize}){linuxBadge}",
                    PathOrDevice = sourcePath,
                    Partition = p,
                    IsImage = isImage,
                    IsAccessible = true
                });

                if (bestLinuxIndex == -1 && p.IsLinux)
                {
                    bestLinuxIndex = i;
                }
            }

            CmbSources.ItemsSource = sourceList;
            CmbSources.SelectedIndex = bestLinuxIndex >= 0 ? bestLinuxIndex : 0;
            TxtStatus.Text = $"Loaded {sourceList.Count} partition(s) from {fileName}. Ready.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open source: {ex.Message}", "Read Error", MessageBoxButton.OK, MessageBoxImage.Error);
            TxtStatus.Text = $"Error: {ex.Message}";
        }
    }

    private void CmbSources_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbSources.SelectedItem is not SourceItem item)
        {
            return;
        }

        if (!item.IsAccessible || item.Partition == null)
        {
            CleanUpResources();
            DirTreeView.Items.Clear();
            LstFiles.ItemsSource = null;
            TxtStatus.Text = item.Partition == null
                ? "Selected item has no readable filesystem partition."
                : "Selected source is not accessible (Administrator privileges required).";
            TxtPreview.Text = "// Select an accessible Linux Ext4 partition to inspect files...";
            return;
        }

        try
        {
            var p = item.Partition;
            TxtStatus.Text = $"Mounting partition {p.PartitionIndex} ({p.Name}) in read-only mode...";

            if (_activeDiskStream == null || _activeDevicePath != item.PathOrDevice)
            {
                _activeDiskStream?.Dispose();
                _activeDiskStream = item.IsImage
                    ? SafeDiskStream.OpenFileImage(item.PathOrDevice)
                    : SafeDiskStream.OpenPhysicalDrive(item.PathOrDevice);
                _activeDevicePath = item.PathOrDevice;
            }

            var partStream = new SubStream(_activeDiskStream, p.StartOffset, p.Length, leaveOpen: true);
            _activeFileSystem?.Dispose();
            _activeFileSystem = Ext4FileSystem.Open(partStream);

            // Populate root directory tree
            PopulateRootTree();
            LoadDirectory("/");

            string volName = string.IsNullOrWhiteSpace(_activeFileSystem.Superblock.VolumeName)
                ? "<unnamed>"
                : _activeFileSystem.Superblock.VolumeName;
            TxtStatus.Text = $"Mounted Ext4 volume: '{volName}' (Block size: {_activeFileSystem.BlockSize} B). Strict Read-Only.";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Partition mount notice: {ex.Message}";
            DirTreeView.Items.Clear();
            LstFiles.ItemsSource = null;
            ClearSelection();
            TxtPreview.Text = $"[Partition {item.Partition.PartitionIndex}: {item.Partition.FileSystemType}]\nThis partition does not contain a supported Ext2/Ext3/Ext4 filesystem.\n\nTip: Select a partition with the 🐧 [Linux Ext4] badge to browse files.";
        }
    }

    private void PopulateRootTree()
    {
        DirTreeView.Items.Clear();
        var rootNode = new TreeViewItem
        {
            Header = "📁 / (Root)",
            Tag = "/"
        };

        // Add dummy child for lazy loading
        rootNode.Items.Add(new TreeViewItem { Header = "Loading..." });
        rootNode.Expanded += TreeNode_Expanded;

        DirTreeView.Items.Add(rootNode);
        rootNode.IsExpanded = true;
    }

    private void TreeNode_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem item || _activeFileSystem == null) return;
        if (item.Items.Count == 1 && item.Items[0] is TreeViewItem dummy && dummy.Header?.ToString() == "Loading...")
        {
            item.Items.Clear();
            string path = item.Tag as string ?? "/";

            try
            {
                var children = _activeFileSystem.ListDirectory(path);
                foreach (var child in children.Where(c => c.IsDirectory))
                {
                    var childNode = new TreeViewItem
                    {
                        Header = $"📁 {child.Name}",
                        Tag = child.FullPath
                    };
                    childNode.Items.Add(new TreeViewItem { Header = "Loading..." });
                    childNode.Expanded += TreeNode_Expanded;
                    item.Items.Add(childNode);
                }
            }
            catch
            {
                // Ignore folder read issues
            }
        }
    }

    private void DirTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DirTreeView.SelectedItem is TreeViewItem item && item.Tag is string path)
        {
            LoadDirectory(path);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 && File.Exists(files[0]))
            {
                LoadDiskSource(files[0], isImage: true);
            }
        }
    }

    private void LoadDirectory(string path)
    {
        if (_activeFileSystem == null) return;

        try
        {
            UpdateBreadcrumbs(path);
            _currentFolderItems = _activeFileSystem.ListDirectory(path);

            ApplyFilter();
            ClearSelection();
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Error reading directory '{path}': {ex.Message}";
        }
    }

    private void UpdateBreadcrumbs(string path)
    {
        PanelBreadcrumbs.Children.Clear();

        var rootBtn = new Button
        {
            Content = "📁 /",
            Padding = new Thickness(4, 2, 4, 2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            FontWeight = path == "/" ? FontWeights.Bold : FontWeights.Normal,
            Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            Tag = "/"
        };
        rootBtn.Click += (s, e) => LoadDirectory("/");
        PanelBreadcrumbs.Children.Add(rootBtn);

        if (path == "/" || string.IsNullOrWhiteSpace(path)) return;

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string accumPath = "";

        for (int i = 0; i < segments.Length; i++)
        {
            string seg = segments[i];
            accumPath += "/" + seg;
            string target = accumPath;
            bool isLast = (i == segments.Length - 1);

            var sep = new TextBlock
            {
                Text = " › ",
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 2, 0)
            };
            PanelBreadcrumbs.Children.Add(sep);

            var segBtn = new Button
            {
                Content = seg,
                Padding = new Thickness(4, 2, 4, 2),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontWeight = isLast ? FontWeights.Bold : FontWeights.Normal,
                Foreground = isLast 
                    ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) 
                    : new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                Tag = target
            };
            segBtn.Click += (s, e) => LoadDirectory(target);
            PanelBreadcrumbs.Children.Add(segBtn);
        }
    }

    private static string GetFileIcon(Ext4Item item)
    {
        if (item.IsDirectory) return "📁 ";
        if (item.IsSymlink) return "🔗 ";

        string ext = Path.GetExtension(item.Name).ToLowerInvariant();
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".ico" or ".svg" or ".webp" => "🖼️ ",
            ".sh" or ".bash" or ".py" or ".js" or ".ts" or ".c" or ".cpp" or ".h" or ".rs" or ".go" or ".java" or ".cs" or ".pl" or ".php" => "⚡ ",
            ".conf" or ".cfg" or ".ini" or ".yaml" or ".yml" or ".json" or ".toml" or ".xml" or ".service" or ".desktop" => "⚙️ ",
            ".tar" or ".gz" or ".xz" or ".bz2" or ".zip" or ".deb" or ".rpm" or ".7z" or ".tgz" or ".iso" => "📦 ",
            ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" => "🎵 ",
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" => "🎬 ",
            ".so" or ".bin" or ".elf" or ".o" or ".dll" or ".exe" => "💿 ",
            ".log" => "📜 ",
            _ => "📄 "
        };
    }

    private void ApplyFilter()
    {
        string query = TxtSearch.Text.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _currentFolderItems
            : _currentFolderItems.Where(i => i.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

        var displayItems = filtered.Select(i => new DisplayFileItem
        {
            Name = GetFileIcon(i) + i.Name,
            FormattedSize = i.FormattedSize,
            Permissions = i.Permissions,
            ModifiedFormatted = i.ModifiedTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
            FileTypeName = i.FileTypeName,
            SourceItem = i
        }).ToList();

        LstFiles.ItemsSource = displayItems;
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void LstFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstFiles.SelectedItem is DisplayFileItem selected)
        {
            _selectedItem = selected.SourceItem;
            UpdateDetailsPane(_selectedItem);
            BtnExport.IsEnabled = true;
            BtnQuickExport.IsEnabled = true;
            BtnOpenWith.IsEnabled = !_selectedItem.IsDirectory;
            BtnCopyPath.IsEnabled = true;
        }
        else
        {
            ClearSelection();
        }
    }

    private void LstFiles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_selectedItem == null) return;

        if (_selectedItem.IsDirectory)
        {
            LoadDirectory(_selectedItem.FullPath);
        }
        else
        {
            OpenWithDefaultApp(_selectedItem);
        }
    }

    private void OpenWithDefaultApp(Ext4Item item)
    {
        if (_activeFileSystem == null || item.IsDirectory) return;

        try
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "LinuxReader_Preview");
            Directory.CreateDirectory(tempDir);
            string safeName = Path.GetFileName(item.Name);
            string tempFilePath = Path.Combine(tempDir, safeName);

            TxtStatus.Text = $"Extracting '{item.Name}' to open in Windows...";
            _activeFileSystem.ExtractFile(item.Inode, tempFilePath);

            var psi = new ProcessStartInfo
            {
                FileName = tempFilePath,
                UseShellExecute = true
            };
            Process.Start(psi);
            TxtStatus.Text = $"Opened '{item.Name}' with default application.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open file: {ex.Message}", "Open Error", MessageBoxButton.OK, MessageBoxImage.Error);
            TxtStatus.Text = $"Error opening file: {ex.Message}";
        }
    }

    private static string FormatHexDump(byte[] bytes, int maxBytes = 4096)
    {
        int length = Math.Min(bytes.Length, maxBytes);
        var sb = new StringBuilder();

        for (int lineOffset = 0; lineOffset < length; lineOffset += 16)
        {
            sb.Append($"{lineOffset:X8}  ");

            for (int i = 0; i < 16; i++)
            {
                if (i == 8) sb.Append(' ');
                if (lineOffset + i < length)
                {
                    sb.Append($"{bytes[lineOffset + i]:X2} ");
                }
                else
                {
                    sb.Append("   ");
                }
            }

            sb.Append(" |");

            for (int i = 0; i < 16 && (lineOffset + i) < length; i++)
            {
                byte b = bytes[lineOffset + i];
                sb.Append(b >= 32 && b <= 126 ? (char)b : '.');
            }

            sb.AppendLine("|");
        }

        if (bytes.Length > maxBytes)
        {
            sb.AppendLine($"\n... [Hex view truncated at {maxBytes / 1024} KB of {bytes.Length:N0} bytes] ...");
        }

        return sb.ToString();
    }

    private void UpdateDetailsPane(Ext4Item item)
    {
        TxtDetailName.Text = item.Name;
        TxtDetailPath.Text = $"Linux Path: {item.FullPath}";
        TxtDetailSize.Text = $"Size: {item.FormattedSize} ({item.Size:N0} bytes)";
        TxtDetailPerms.Text = $"Permissions: {item.Permissions}";
        TxtDetailInode.Text = $"Inode: {item.InodeNumber}";

        if (item.IsSymlink && !string.IsNullOrEmpty(item.SymlinkTarget))
        {
            TxtDetailPath.Text += $"\nPoints to: {item.SymlinkTarget}";
        }

        _cachedTextPreview = null;
        _cachedHexPreview = null;
        _cachedImagePreview = null;

        if (item.IsRegularFile && item.Size <= 10 * 1024 * 1024 && _activeFileSystem != null)
        {
            try
            {
                // Read up to 256KB for previewing text / hex / image
                int maxRead = (int)Math.Min(item.Size, 256 * 1024);
                byte[] buffer = new byte[maxRead];
                using (var stream = _activeFileSystem.OpenFile(item.Inode))
                {
                    int totalRead = 0;
                    while (totalRead < maxRead)
                    {
                        int r = stream.Read(buffer, totalRead, maxRead - totalRead);
                        if (r <= 0) break;
                        totalRead += r;
                    }
                    if (totalRead < maxRead)
                    {
                        Array.Resize(ref buffer, totalRead);
                    }
                }

                _cachedHexPreview = FormatHexDump(buffer);

                string ext = Path.GetExtension(item.Name).ToLowerInvariant();
                bool isImageExt = ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".ico";

                if (isImageExt)
                {
                    try
                    {
                        using var ms = new MemoryStream(buffer);
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                        bmp.Freeze();
                        _cachedImagePreview = bmp;
                    }
                    catch
                    {
                        _cachedImagePreview = null;
                    }
                }

                // Check text decode
                bool hasNullBytes = buffer.Any(b => b == 0);
                if (!hasNullBytes)
                {
                    _cachedTextPreview = Encoding.UTF8.GetString(buffer);
                }

                if (_cachedImagePreview != null)
                {
                    RbViewImage.IsChecked = true;
                }
                else if (!hasNullBytes && !string.IsNullOrEmpty(_cachedTextPreview))
                {
                    RbViewText.IsChecked = true;
                }
                else
                {
                    RbViewHex.IsChecked = true;
                }

                RbViewMode_Checked(this, new RoutedEventArgs());
            }
            catch (Exception ex)
            {
                TxtPreview.Text = $"[Preview unavailable: {ex.Message}]";
                RbViewText.IsChecked = true;
                RbViewMode_Checked(this, new RoutedEventArgs());
            }
        }
        else if (item.IsDirectory)
        {
            TxtPreview.Text = $"[Directory: {item.Name}]\nDouble-click to enter folder, or click 'Export' to copy entire folder tree to Windows.";
            RbViewText.IsChecked = true;
            RbViewMode_Checked(this, new RoutedEventArgs());
        }
        else
        {
            TxtPreview.Text = $"[File size exceeds 10MB ({item.FormattedSize})]\nUse 'Open in Windows App' or 'Export' to view this file.";
            RbViewText.IsChecked = true;
            RbViewMode_Checked(this, new RoutedEventArgs());
        }
    }

    private void RbViewMode_Checked(object sender, RoutedEventArgs e)
    {
        if (ScrollTextHex == null || ScrollImage == null) return;

        if (RbViewImage.IsChecked == true)
        {
            ScrollTextHex.Visibility = Visibility.Collapsed;
            ScrollImage.Visibility = Visibility.Visible;
            ImgPreview.Source = _cachedImagePreview;
        }
        else if (RbViewHex.IsChecked == true)
        {
            ScrollTextHex.Visibility = Visibility.Visible;
            ScrollImage.Visibility = Visibility.Collapsed;
            TxtPreview.Text = _cachedHexPreview ?? "// No hex data available";
        }
        else
        {
            ScrollTextHex.Visibility = Visibility.Visible;
            ScrollImage.Visibility = Visibility.Collapsed;
            TxtPreview.Text = _cachedTextPreview ?? "// No text preview available (Binary or Non-text format)";
        }
    }

    private void ClearSelection()
    {
        _selectedItem = null;
        _cachedTextPreview = null;
        _cachedHexPreview = null;
        _cachedImagePreview = null;

        TxtDetailName.Text = "No item selected";
        TxtDetailPath.Text = string.Empty;
        TxtDetailSize.Text = string.Empty;
        TxtDetailPerms.Text = string.Empty;
        TxtDetailInode.Text = string.Empty;
        TxtPreview.Text = "// File preview will appear here...";

        BtnExport.IsEnabled = false;
        BtnQuickExport.IsEnabled = false;
        BtnOpenWith.IsEnabled = false;
        BtnCopyPath.IsEnabled = false;

        RbViewText.IsChecked = true;
        RbViewMode_Checked(this, new RoutedEventArgs());
    }

    private void BtnOpenWith_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem != null)
        {
            OpenWithDefaultApp(_selectedItem);
        }
    }

    private void BtnCopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem != null)
        {
            Clipboard.SetText(_selectedItem.FullPath);
            TxtStatus.Text = $"Copied path '{_selectedItem.FullPath}' to clipboard.";
        }
    }

    private void CtxOpenDefault_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem != null)
        {
            OpenWithDefaultApp(_selectedItem);
        }
    }

    private void CtxCopyPath_Click(object sender, RoutedEventArgs e)
    {
        BtnCopyPath_Click(sender, e);
    }

    private void CtxCopyContent_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem == null) return;

        if (!string.IsNullOrEmpty(_cachedTextPreview))
        {
            Clipboard.SetText(_cachedTextPreview);
            TxtStatus.Text = $"Copied text content of '{_selectedItem.Name}' to clipboard.";
        }
        else
        {
            MessageBox.Show("No text content available to copy for this item.", "Copy Notice", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void CtxProperties_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem == null) return;

        var sb = new StringBuilder();
        sb.AppendLine($"Name: {_selectedItem.Name}");
        sb.AppendLine($"Full Linux Path: {_selectedItem.FullPath}");
        sb.AppendLine($"Size: {_selectedItem.FormattedSize} ({_selectedItem.Size:N0} bytes)");
        sb.AppendLine($"Type: {_selectedItem.FileTypeName}");
        sb.AppendLine($"Permissions: {_selectedItem.Permissions} (Mode: 0x{_selectedItem.Inode.Mode:X4})");
        sb.AppendLine($"Inode Number: {_selectedItem.InodeNumber}");
        sb.AppendLine($"Links Count: {_selectedItem.Inode.LinksCount}");
        sb.AppendLine($"Flags: 0x{_selectedItem.Inode.Flags:X8} (Extents: {_selectedItem.Inode.UsesExtents})");
        sb.AppendLine($"Modified: {_selectedItem.ModifiedTime.LocalDateTime}");
        if (_selectedItem.IsSymlink && !string.IsNullOrEmpty(_selectedItem.SymlinkTarget))
        {
            sb.AppendLine($"Symlink Points To: {_selectedItem.SymlinkTarget}");
        }

        MessageBox.Show(sb.ToString(), $"Inode Metadata - {_selectedItem.Name}", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem == null || _activeFileSystem == null) return;

        if (_selectedItem.IsDirectory)
        {
            // Folder export
            var folderDialog = new OpenFolderDialog
            {
                Title = $"Select Windows destination folder for '{_selectedItem.Name}'"
            };

            if (folderDialog.ShowDialog() == true)
            {
                string destination = Path.Combine(folderDialog.FolderName, _selectedItem.Name);
                TxtStatus.Text = $"Extracting directory '{_selectedItem.Name}'...";

                try
                {
                    var progress = new Progress<string>(msg => TxtStatus.Text = msg);
                    _activeFileSystem.ExtractDirectory(_selectedItem.Inode, destination, progress);
                    MessageBox.Show($"Successfully extracted folder to:\n{destination}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    TxtStatus.Text = "Extraction finished successfully.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    TxtStatus.Text = $"Extraction failed: {ex.Message}";
                }
            }
        }
        else
        {
            // Single file export
            var saveDialog = new SaveFileDialog
            {
                Title = $"Save '{_selectedItem.Name}' to Windows",
                FileName = _selectedItem.Name
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    TxtStatus.Text = $"Saving {_selectedItem.Name}...";
                    _activeFileSystem.ExtractFile(_selectedItem.Inode, saveDialog.FileName);
                    MessageBox.Show($"File saved successfully:\n{saveDialog.FileName}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    TxtStatus.Text = $"Saved {_selectedItem.Name}.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    TxtStatus.Text = $"Export error: {ex.Message}";
                }
            }
        }
    }

    private void CleanUpResources()
    {
        _activeFileSystem?.Dispose();
        _activeFileSystem = null;
        _activeDiskStream?.Dispose();
        _activeDiskStream = null;
        _activeDevicePath = null;
        _currentFolderItems.Clear();
        ClearSelection();
    }

    protected override void OnClosed(EventArgs e)
    {
        CleanUpResources();
        base.OnClosed(e);
    }
}
