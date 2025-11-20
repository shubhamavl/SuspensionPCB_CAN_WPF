# Suspension System Monitor - User Guide

## 🚀 Quick Start Guide

### ⚠️ IMPORTANT: No .NET Installation Needed!

**The .NET 8.0 runtime is ALREADY INCLUDED in this package!**
- ❌ You do NOT need to download .NET 8.0 from Microsoft
- ❌ You do NOT need to install .NET runtime separately
- ✅ Everything is included - just extract and run!

### First Time Setup

1. **Extract the ZIP file** you received to any folder (Desktop is fine)
2. **Double-click** `SuspensionPCB_CAN_WPF.exe` to start
3. **Connect** your USB-CAN adapter to your computer
4. **Select** the COM port from the dropdown (usually COM3, COM4, etc.)
5. **Click** "Connect" button
6. **Calibrate** both left and right sides (see Calibration section below)
7. **Start** data streaming to see live weight measurements

That's it! The application is ready to use.

**Note**: The file is large (~70-100 MB) because it includes the .NET runtime. This is normal!

---

## 📁 Understanding the Application Folder

When you extract the ZIP file, you'll see:

```
SuspensionPCB_CAN_WPF/
├── SuspensionPCB_CAN_WPF.exe    ← Run this file
├── settings.json                 ← Your settings (created automatically)
├── Data/                         ← Your data files (created automatically)
│   ├── calibration_left.json    ← Left side calibration
│   ├── calibration_right.json   ← Right side calibration
│   ├── tare_config.json        ← Zero point settings
│   └── suspension_log_*.csv     ← Data log files
└── Logs/                         ← Log files (created automatically)
    └── suspension_log_*.txt     ← System logs
```

**Important**: Keep all these files together! Don't delete the `Data/` or `Logs/` folders.

---

## 🔧 Calibration

### Why Calibrate?

Calibration tells the system how to convert raw sensor readings into actual weight measurements. You need to calibrate each side (Left and Right) separately.

### Calibration Steps

1. **Start the application** and connect to your device
2. **Start data streaming** for the side you want to calibrate:
   - Click "Request Left Stream" for left side
   - Click "Request Right Stream" for right side
3. **Click "Calibrate Left"** or "Calibrate Right" button
4. **Step 1 - Zero Point**:
   - Remove all weight from the side
   - Wait for stable readings
   - Click "Record Zero Point"
5. **Step 2 - Known Weight**:
   - Place a known weight on the side (e.g., 10 kg)
   - Wait for stable readings
   - Enter the weight value
   - Click "Record Known Weight"
6. **Click "Save Calibration"**

The calibration is now saved and will be remembered for future use.

### When to Recalibrate

- After hardware changes
- If measurements seem inaccurate
- Periodically for best accuracy (monthly recommended)

---

## ⚖️ Tare (Zero Point)

### What is Tare?

Tare sets the current weight as the zero point. This is useful when you want to measure additional weight on top of an existing load.

### How to Tare

1. **Start data streaming** for the side you want to tare
2. **Wait** for stable readings
3. **Click "Tare Left"** or "Tare Right" button
4. The display will now show 0.0 kg (or close to it)

### When to Use Tare

- Daily zero-out before measurements
- When you want to measure weight changes
- After calibration to set a baseline

---

## 📊 Data Logging

### Start Logging

1. Click **"Start Logging"** button
2. Data will be saved to CSV files in the `Data/` folder
3. Files are named: `suspension_log_YYYYMMDD_HHMMSS.csv`

### Stop Logging

1. Click **"Stop Logging"** button
2. The current log file is saved automatically

### View Log Files

- Log files are in the `Data/` folder
- Open with Excel, Notepad, or any text editor
- Contains: Timestamp, Side, Raw ADC, Calibrated Weight, Tared Weight, etc.

---

## ⚙️ Settings

### COM Port

- Select the port where your USB-CAN adapter is connected
- Usually COM3, COM4, COM5, etc.
- Saved automatically

### Transmission Rate

- **100Hz**: Low frequency (for slow monitoring)
- **500Hz**: Standard operation
- **1kHz**: High speed (default, recommended)
- **1Hz**: Very slow (for debugging)

### Save Directory

- By default, data is saved in the `Data/` folder next to the executable
- You can change this if needed (not recommended for portable use)

---

## 🎮 Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+C` | Connect to CAN bus |
| `Ctrl+D` | Disconnect from CAN bus |
| `Ctrl+L` | Start left side streaming |
| `Ctrl+R` | Start right side streaming |
| `Ctrl+S` | Stop all streams |
| `Ctrl+T` | Toggle settings panel |
| `Ctrl+M` | Open monitor window |
| `Ctrl+P` | Open production logs |
| `F1` | Show help |

---

## 🔍 Monitor Window

The Monitor Window shows all CAN bus messages in real-time:
- **Blue messages**: Sent by the application (TX)
- **Green messages**: Received from device (RX)
- Useful for troubleshooting communication issues

---

## ❓ Troubleshooting

### Can't Connect

- **Check COM port**: Make sure the correct port is selected
- **Check cable**: Ensure USB-CAN adapter is connected
- **Check drivers**: Install USB-CAN adapter drivers if needed
- **Try different port**: The port number may change

### No Data Displayed

- **Start streaming**: Click "Request Left Stream" or "Request Right Stream"
- **Check connection**: Make sure device is connected and responding
- **Check calibration**: Calibrate if you haven't already

### Incorrect Weight Readings

- **Recalibrate**: Calibration may be off
- **Check tare**: Make sure tare is set correctly
- **Check sensors**: Verify sensors are working properly

### Application Won't Start

- **Windows version**: Requires Windows 10 or 11 (64-bit)
- **Antivirus**: May be blocking the application (add exception)
- **Permissions**: Try running as Administrator

---

## 💾 Backup Your Data

### What to Backup

- **`Data/` folder**: Contains all calibration and data files
- **`settings.json`**: Contains your settings

### How to Backup

1. **Copy the entire `Data/` folder** to a safe location
2. **Copy `settings.json`** to the same location
3. Store on USB drive, cloud storage, or another computer

### Restore Backup

1. **Stop the application**
2. **Replace the `Data/` folder** with your backup
3. **Replace `settings.json`** with your backup
4. **Start the application**

---

## 📞 Getting Help

If you encounter issues:

1. **Check the Logs**: Open the Logs window (Ctrl+P) to see error messages
2. **Check Monitor**: Open Monitor window (Ctrl+M) to see CAN messages
3. **Contact Support**: Provide log files and error messages

---

## 🔄 Updating the Application

When a new version is available:

1. **Backup your data** (copy `Data/` folder and `settings.json`)
2. **Download the new ZIP file**
3. **Extract to a new folder** (or replace old files)
4. **Copy your backup** (`Data/` folder and `settings.json`) to the new location
5. **Run the new version**

Your calibration and settings will be preserved!

---

## 📝 Notes

- The application stores all data locally - nothing is sent to the internet
- You can run it from a USB drive - fully portable!
- No installation required - just extract and run
- All files stay in the application folder - easy to backup or move

---

**Version**: 1.0.0  
**Last Updated**: January 2025

