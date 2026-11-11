using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    private void LoadDirectory(string path)
    {
        if (_activeFileSystem == null) return;

        try
        {
            TxtCurrentPath.Text = path;
            _currentFolderItems = _activeFileSystem.ListDirectory(path);

            ApplyFilter();
            ClearSelection();
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Error reading directory '{path}': {ex.Message}";
        }
    }

    private void ApplyFilter()
    {
        string query = TxtSearch.Text.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _currentFolderItems
            : _currentFolderItems.Where(i => i.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

        var displayItems = filtered.Select(i => new DisplayFileItem
        {
            Name = (i.IsDirectory ? "📁 " : i.IsSymlink ? "🔗 " : "📄 ") + i.Name,
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
        }
        else
        {
            ClearSelection();
        }
    }

    private void LstFiles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_selectedItem != null && _selectedItem.IsDirectory)
        {
            LoadDirectory(_selectedItem.FullPath);
        }
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

        // Preview text if size <= 512KB and is regular file
        if (item.IsRegularFile && item.Size <= 512 * 1024 && _activeFileSystem != null)
        {
            try
            {
                using var stream = _activeFileSystem.OpenFile(item.Inode);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                char[] buffer = new char[8192];
                int read = reader.Read(buffer, 0, buffer.Length);
                string text = new string(buffer, 0, read);

                // Check for null bytes to verify if it's binary
                if (text.Contains('\0'))
                {
                    TxtPreview.Text = $"[Binary File - {item.Size:N0} bytes]\nDirect binary export is recommended.";
                }
                else
                {
                    TxtPreview.Text = text + (item.Size > (ulong)read ? "\n\n... [Preview truncated to first 8KB] ..." : "");
                }
            }
            catch (Exception ex)
            {
                TxtPreview.Text = $"[Preview unavailable: {ex.Message}]";
            }
        }
        else if (item.IsDirectory)
        {
            TxtPreview.Text = $"[Directory: {item.Name}]\nDouble-click to open or click 'Export' to copy entire folder tree to Windows.";
        }
        else
        {
            TxtPreview.Text = $"[File size exceeds 512KB ({item.FormattedSize})]\nClick 'Export to Windows' to save this file locally.";
        }
    }

    private void ClearSelection()
    {
        _selectedItem = null;
        TxtDetailName.Text = "No item selected";
        TxtDetailPath.Text = string.Empty;
        TxtDetailSize.Text = string.Empty;
        TxtDetailPerms.Text = string.Empty;
        TxtDetailInode.Text = string.Empty;
        TxtPreview.Text = "// File preview will appear here...";
        BtnExport.IsEnabled = false;
        BtnQuickExport.IsEnabled = false;
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
