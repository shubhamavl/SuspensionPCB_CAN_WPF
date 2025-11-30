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

Calibration tells the system how to convert raw sensor readings into actual weight measurements. You need to calibrate each side (Left and Right) separately, and for each ADC mode (Internal and ADS1115).

### Multi-Point Calibration

The application supports **multi-point calibration** using least-squares linear regression. This provides better accuracy than two-point calibration, especially over a wide weight range.

**Recommended**: Use 3-5 calibration points for best accuracy.

### Calibration Steps

1. **Start the application** and connect to your device
2. **Start data streaming** for the side you want to calibrate:
   - Click "Request Left Stream" for left side
   - Click "Request Right Stream" for right side
3. **Click "Calibrate Left"** or "Calibrate Right" button
4. **Add Calibration Points**:
   - Click "Add Point" to create a new calibration point
   - Enter the known weight (can be in any order - zero point, then weights, or vice versa)
   - Click "Capture" - system automatically captures both Internal and ADS1115 ADC values
   - Repeat for additional points (minimum 1 point, 3-5 recommended)
5. **Calculate Calibration**:
   - Click "Calculate" to perform least-squares regression
   - Review R² value (should be >0.95 for good quality)
   - Review maximum error and quality assessment
6. **Save Calibration**:
   - Click "Save Calibration"
   - Calibration is saved for both ADC modes automatically

### Calibration Quality Metrics

- **R² (Coefficient of Determination)**: Measures fit quality
  - 1.0 = Perfect fit
  - >0.95 = Excellent
  - 0.90-0.95 = Good
  - <0.90 = May need more points or check for issues
- **Maximum Error**: Largest deviation from fitted line
- **Quality Assessment**: Excellent/Good/Acceptable/Poor

### ADC Mode-Specific Calibration

The system stores separate calibrations for:
- **Internal ADC (12-bit)**: Faster, standard precision
- **ADS1115 (16-bit)**: Higher precision, 6.4x better resolution

Both modes are calibrated simultaneously when you capture points. The system automatically uses the correct calibration based on the current ADC mode.

### When to Recalibrate

- After hardware changes
- If measurements seem inaccurate
- Periodically for best accuracy (monthly recommended)
- When switching between ADC modes (calibrations are independent)

---

## ⚖️ Tare (Zero Point)

### What is Tare?

Tare sets the current weight as the zero point. This is useful when you want to measure additional weight on top of an existing load. Tare values are stored separately for each ADC mode (Internal and ADS1115).

### How to Tare

1. **Start data streaming** for the side you want to tare
2. **Wait** for stable readings
3. **Click "Tare Left"** or "Tare Right" button
4. The display will now show 0.0 kg (or close to it)
5. Tare is automatically saved and remembered for the current ADC mode

### ADC Mode-Specific Tare

- Tare values are stored separately for Internal and ADS1115 modes
- When you switch ADC modes, the system uses the appropriate tare value
- Each side and mode combination has its own tare baseline

### When to Use Tare

- Daily zero-out before measurements
- When you want to measure weight changes
- After calibration to set a baseline
- When switching ADC modes (re-tare if needed)

---

## 📊 Data Logging

### Start Logging

1. Click **"Start Logging"** button (manual control only - no auto-start)
2. Data will be saved to CSV files in the `Data/` folder
3. Files are named: `suspension_log_YYYYMMDD_HHMMSS.csv` (timestamped)
4. A new file is created each time you start logging

### Stop Logging

1. Click **"Stop Logging"** button
2. The current log file is saved automatically

### View Log Files

- Use the **Log Files Manager** (Tools → Log Files Manager)
- Filter by file type (Data Logs, Production Logs, CAN Monitor exports)
- View file details: name, type, size, creation date
- Delete selected files or clear all (with confirmation)
- Open log directory in Explorer

### Log File Contents

CSV files contain:
- Timestamp
- Side (Left/Right)
- RawADC
- CalibratedKg
- TaredKg
- TareBaseline
- CalSlope
- CalIntercept
- ADCMode (0=Internal, 1=ADS1115)
- SystemStatus
- ErrorFlags
- StatusTimestamp

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

**Version**: 2.0.0  
**Last Updated**: November 2025

