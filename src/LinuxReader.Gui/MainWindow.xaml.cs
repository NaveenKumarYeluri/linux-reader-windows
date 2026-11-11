using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LinuxReader.Core.Ext4;
using LinuxReader.Core.IO;
using LinuxReader.Core.Partitions;
using Microsoft.Win32;

namespace LinuxReader.Gui;

public partial class MainWindow : Window
{
    private Stream? _activeDiskStream;
    private Ext4FileSystem? _activeFileSystem;
    private List<Ext4Item> _currentFolderItems = new();
    private Ext4Item? _selectedItem;

    public class SourceItem
    {
        public string DisplayName { get; init; } = string.Empty;
        public string PathOrDevice { get; init; } = string.Empty;
        public PartitionInfo? Partition { get; init; }
        public bool IsImage { get; init; }
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

    private void BtnScanDrives_Click(object sender, RoutedEventArgs e)
    {
        TxtStatus.Text = "Scanning physical hard drives (Read-Only mode)...";
        var drives = DeviceManager.GetPhysicalDrives();

        var sources = new List<SourceItem>();
        foreach (var drive in drives)
        {
            string accessSuffix = drive.IsAccessible ? "" : " [Requires Admin]";
            sources.Add(new SourceItem
            {
                DisplayName = $"{drive.FriendlyName} ({drive.FormattedSize}){accessSuffix}",
                PathOrDevice = drive.DevicePath,
                IsImage = false
            });
        }

        if (sources.Count == 0)
        {
            MessageBox.Show(
                "No accessible physical drives detected.\n\nTip: Run this application as Administrator to inspect raw physical disks directly.",
                "Drives Scan", MessageBoxButton.OK, MessageBoxImage.Information);
            TxtStatus.Text = "No physical drives detected.";
            return;
        }

        CmbSources.ItemsSource = sources;
        CmbSources.SelectedIndex = 0;
        TxtStatus.Text = $"Detected {sources.Count} physical drive(s).";
    }

    private void LoadDiskSource(string sourcePath, bool isImage)
    {
        try
        {
            CleanUpResources();
            TxtStatus.Text = $"Opening {Path.GetFileName(sourcePath)} in strict read-only mode...";

            _activeDiskStream = isImage
                ? SafeDiskStream.OpenFileImage(sourcePath)
                : SafeDiskStream.OpenPhysicalDrive(sourcePath);

            var partitions = PartitionScanner.Scan(_activeDiskStream);
            var sourceList = new List<SourceItem>();

            string baseName = isImage ? Path.GetFileName(sourcePath) : sourcePath;

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
                string linuxBadge = p.IsLinux ? " [Linux Ext4]" : "";
                sourceList.Add(new SourceItem
                {
                    DisplayName = $"{baseName} - Part {p.PartitionIndex}: {p.Name} ({p.FormattedSize}){linuxBadge}",
                    PathOrDevice = sourcePath,
                    Partition = p,
                    IsImage = isImage
                });

                if (bestLinuxIndex == -1 && p.IsLinux)
                {
                    bestLinuxIndex = i;
                }
            }

            CmbSources.ItemsSource = sourceList;
            CmbSources.SelectedIndex = bestLinuxIndex >= 0 ? bestLinuxIndex : 0;
            TxtStatus.Text = $"Loaded {sourceList.Count} partition(s). Ready.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open source: {ex.Message}", "Read Error", MessageBoxButton.OK, MessageBoxImage.Error);
            TxtStatus.Text = $"Error: {ex.Message}";
        }
    }

    private void CmbSources_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbSources.SelectedItem is not SourceItem item || item.Partition == null || _activeDiskStream == null)
        {
            return;
        }

        try
        {
            var p = item.Partition;
            TxtStatus.Text = $"Mounting partition {p.PartitionIndex} ({p.Name})...";

            var partStream = new SubStream(_activeDiskStream, p.StartOffset, p.Length, leaveOpen: true);
            _activeFileSystem?.Dispose();
            _activeFileSystem = Ext4FileSystem.Open(partStream);

            // Populate root directory tree
            PopulateRootTree();
            LoadDirectory("/");

            TxtStatus.Text = $"Mounted Ext4 volume: '{_activeFileSystem.Superblock.VolumeName}' (Block size: {_activeFileSystem.BlockSize} B). Read-only.";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Partition mount error: {ex.Message}";
            DirTreeView.Items.Clear();
            LstFiles.ItemsSource = null;
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
        _currentFolderItems.Clear();
        ClearSelection();
    }

    protected override void OnClosed(EventArgs e)
    {
        CleanUpResources();
        base.OnClosed(e);
    }
}
