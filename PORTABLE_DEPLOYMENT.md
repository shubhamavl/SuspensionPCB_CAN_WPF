# Portable Deployment Guide

This application is designed to be fully portable - it can run from any directory, including USB drives, without requiring installation or .NET runtime installation on the target machine.

## 🎯 What Makes It Portable?

1. **Self-Contained**: Includes .NET 8.0 runtime - no installation required
2. **Portable File Paths**: All data files stored next to the executable
3. **No Registry Dependencies**: No system-wide configuration
4. **Single-File Deployment**: Optional single-file executable (all dependencies bundled)

## 📦 Building the Portable Version

### Prerequisites
- .NET 8.0 SDK installed on build machine
- Windows 10/11 (64-bit) for building

### Build Commands

#### Option 1: Single-File Executable (Recommended)
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true
```

This creates a single executable file that includes everything needed.

#### Option 2: Folder Deployment
```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

This creates a folder with the executable and all dependencies.

### Output Location
The published files will be in:
```
bin/Release/net8.0-windows/win-x64/publish/
```

## 📁 Portable Directory Structure

When deployed, the application creates the following structure:

```
SuspensionPCB_CAN_WPF/
├── SuspensionPCB_CAN_WPF.exe          # Main executable
├── settings.json                       # Application settings (created on first run)
├── Data/                               # Data directory (created automatically)
│   ├── calibration_left.json          # Left side calibration
│   ├── calibration_right.json         # Right side calibration
│   ├── tare_config.json               # Tare configuration
│   └── suspension_log_*.csv           # Data log files
└── Logs/                               # Logs directory (created automatically)
    └── suspension_log_*.txt            # Production log files
```

**All files are stored relative to the executable** - no system directories are used.

## 🚀 Distribution

### For End Users

1. **Download** the portable package (ZIP file)
2. **Extract** to any location:
   - Desktop folder
   - USB drive
   - Network share
   - Any directory
3. **Run** `SuspensionPCB_CAN_WPF.exe`
4. **No installation required!**

### Creating Distribution Package

1. Build the portable version (see above)
2. Copy the `publish/` folder contents
3. Optionally include:
   - `README.md` - User documentation
   - `PORTABLE_DEPLOYMENT.md` - This file
4. Create a ZIP file with all contents
5. Distribute the ZIP file

### Example Distribution Structure

```
SuspensionPCB_CAN_WPF_Portable_v1.0.zip
├── SuspensionPCB_CAN_WPF.exe
├── README.md
├── PORTABLE_DEPLOYMENT.md
└── Assets/ (if not embedded)
```

## ✅ Portable Features

### ✅ What Works Portably
- All settings stored in `settings.json` next to executable
- All calibration data in `Data/` folder
- All logs in `Logs/` folder
- All data files in `Data/` folder
- No registry writes
- No system folder dependencies
- Can run from read-only media (with write access to executable directory)

### ⚠️ Requirements
- Windows 10/11 (64-bit)
- Write access to the directory containing the executable
- USB-CAN adapter drivers (if using USB-CAN adapter)

## 🔧 Configuration

### Settings File Location
- **Portable**: `settings.json` (next to executable)
- **Old Location**: `%AppData%\SuspensionSystem\settings.json` (no longer used)

### Data Directory
- **Portable**: `Data/` folder (next to executable)
- **Old Location**: `%Documents%\SuspensionSystem\Data` (no longer used)

### Logs Directory
- **Portable**: `Logs/` folder (next to executable)
- **Old Location**: `%Documents%\SuspensionSystem\Logs` (no longer used)

## 🔄 Migration from Installed Version

If you have an existing installation with data in system folders:

1. **Export your data** from the old version:
   - Calibration files: `%Documents%\SuspensionSystem\Data\calibration_*.json`
   - Tare config: `%Documents%\SuspensionSystem\Data\tare_config.json`
   - Settings: `%AppData%\SuspensionSystem\settings.json`

2. **Copy to portable version**:
   - Place calibration and tare files in `Data/` folder
   - Place `settings.json` next to executable

3. **Run the portable version** - it will use the migrated data

## 🐛 Troubleshooting

### "Application won't start"
- Ensure you're running on Windows 10/11 (64-bit)
- Check that all files are extracted from the ZIP
- Try running as Administrator if permission issues occur

### "Can't save settings"
- Ensure write access to the executable directory
- If on USB drive, ensure it's not write-protected
- Check disk space availability

### "Data files not found"
- The `Data/` folder is created automatically on first run
- If missing, create it manually next to the executable
- Ensure write permissions to the directory

### "Logs not being written"
- The `Logs/` folder is created automatically on first run
- Check write permissions to the executable directory
- Ensure sufficient disk space

## 📝 Notes

- **First Run**: The application will create `Data/` and `Logs/` folders automatically
- **Multiple Instances**: Each instance uses the same data files (not recommended to run multiple instances simultaneously)
- **Network Deployment**: Can be run from network shares if write access is available
- **USB Deployment**: Fully supported - all data stays on the USB drive
- **Read-Only Media**: Will work if executable directory is writable (e.g., USB with write access)

## 🔐 Security Considerations

- All data is stored locally in the executable directory
- No data is sent to external servers
- No internet connection required
- Settings and calibration data are in plain JSON (not encrypted)
- Consider file permissions if storing sensitive data

## 📊 File Sizes

- **Single-File Executable**: ~70-100 MB (includes .NET runtime)
- **Folder Deployment**: ~100-150 MB total
- **Data Files**: Typically < 1 MB per session
- **Log Files**: Varies by usage (typically < 10 MB per day)

## 🎓 Best Practices

1. **Backup**: Regularly backup the `Data/` folder containing calibration data
2. **Version Control**: Keep different versions in separate folders
3. **USB Deployment**: Use a dedicated USB drive for portable deployment
4. **Documentation**: Include README.md with the distribution
5. **Testing**: Test on a clean Windows machine before distribution

---

**Version**: 1.0.0  
**Last Updated**: January 2025  
**Compatibility**: Windows 10/11 (64-bit)

