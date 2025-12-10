# Weight Measurement System Monitor - PC3 Interface

A professional Windows WPF application for real-time weight monitoring and calibration of STM32-based suspension systems via CAN bus communication.

## 🚀 Features

### Core Functionality
- **Real-time Weight Display**: Live monitoring of left and right side weights with configurable filtering
- **CAN Bus Communication**: Protocol v0.7 compliant communication at 250 kbps
- **High-Performance Processing**: Optimized for 1kHz data rates with multi-threaded architecture
- **Multi-Point Calibration**: Advanced calibration system with unlimited points and least-squares regression
- **Dual ADC Mode Support**: Independent calibration for Internal (12-bit) and ADS1115 (16-bit) modes
- **Weight Filtering**: EMA and SMA filters for smooth, stable weight readings
- **Tare Management**: Mode-specific zero-point adjustment with persistent storage
- **Data Logging**: Comprehensive logging with production-grade file output and timestamped files
- **Firmware Updates**: Bootloader support for STM32 firmware updates via CAN
- **Version Management**: View and install any previous version from GitHub releases

### User Interface
- **Modern Design**: Professional color palette with intuitive layout
- **Streaming Indicators**: Visual feedback for active data streams
- **Real-time Statistics**: Message counts, rates, and system status
- **Keyboard Shortcuts**: Efficient operation with hotkeys
- **Responsive Layout**: Adapts to different window sizes
- **Settings Persistence**: Remembers COM port and transmission rate preferences

### Advanced Features
- **Multi-threaded Architecture**: Dedicated WeightProcessor for 1kHz calibration
- **Performance Optimization**: Lock-free reads, batched UI updates
- **Protocol Compliance**: Full CAN v0.7 semantic ID implementation
- **Bootloader Protocol**: Complete firmware update support (0x510-0x513)
- **Simulator Adapter**: Software-based CAN adapter for testing without hardware
- **CAN Monitor Export**: Export captured CAN messages to CSV/text files
- **Log Files Manager**: View, filter, and delete log files with file management
- **Data Timeout Detection**: Automatic stream stopping when data stops
- **System Status Monitoring**: Real-time status with data rate calculation
- **Error Handling**: Comprehensive error reporting and recovery
- **Production Logging**: Detailed logs for troubleshooting and analysis

## 📋 System Requirements

- **OS**: Windows 10/11 (64-bit)
- **Framework**: .NET 8.0 runtime (included in portable version, no installation needed)
- **Hardware**: USB-CAN adapter (250 kbps)
- **Memory**: 4GB RAM minimum, 8GB recommended
- **Storage**: 150MB for portable application (includes .NET runtime), 100MB for logs/data

## 🔧 Installation

### Portable Version (Recommended)
1. **Download** the portable release package (ZIP file)
2. **Extract** to any directory (desktop, USB drive, etc.)
3. **Run** `SuspensionPCB_CAN_WPF.exe` - **No installation required!**
4. **Connect** your USB-CAN adapter
5. **Configure** COM port and transmission rate

The portable version includes the .NET runtime and stores all data files next to the executable. See [docs/DISTRIBUTION_GUIDE.md](docs/DISTRIBUTION_GUIDE.md) for details.

### Development/Source Version
1. **Clone** the repository
2. **Build** using `dotnet build` or Visual Studio
3. **Run** from Visual Studio or `dotnet run`
4. **Connect** your USB-CAN adapter
5. **Configure** COM port and transmission rate

## 🎮 User Interface Guide

### Main Window Layout

#### Top Toolbar
- **COM Port Selector**: Choose your USB-CAN adapter port
- **Connect Button**: Establish CAN bus connection
- **Settings Toggle**: Access advanced settings panel
- **Stop All Streams**: Emergency stop for all data streams

#### Live Weight Data Display
- **Left Side Panel**:
  - Raw ADC value (uncalibrated)
  - Calibrated weight (kg)
  - Final display weight (with tare applied)
  - Tare status indicator
  - Stream status (active/inactive)

- **Right Side Panel**:
  - Same layout as left side
  - Independent calibration and tare

#### Control Buttons
- **Request Left Stream**: Start left side data streaming
- **Request Right Stream**: Start right side data streaming
- **Tare Left**: Set left side zero point
- **Tare Right**: Set right side zero point
- **Calibrate Left**: Open left side calibration dialog
- **Calibrate Right**: Open right side calibration dialog
- **Reset Tares**: Clear all tare values
- **Start/Stop Logging**: Control data logging

#### Status Bar
- **Connection Status**: CAN bus connection state
- **Stream Status**: Active streaming information
- **TX Counter**: Messages sent counter
- **RX Counter**: Messages received counter
- **Timestamp**: Current system time

### Advanced Settings Panel

#### Transmission Rate Selection
- **100Hz**: Low-frequency monitoring
- **500Hz**: Standard operation
- **1kHz**: High-speed data acquisition
- **1Hz**: Slow monitoring/debugging

#### Settings Persistence
- **COM Port**: Automatically saved and restored
- **Transmission Rate**: Preference remembered between sessions
- **Calibration Data**: Stored in JSON files
- **Tare Values**: Persistent across restarts

## ⌨️ Keyboard Shortcuts

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
| `Ctrl+I` | Switch to Internal ADC mode |
| `Ctrl+A` | Switch to ADS1115 mode |
| `F1` | Show help |

## 🔬 ADC Modes

### Internal ADC (12-bit)
- **Resolution**: 4096 levels (0-4095)
- **Reference**: 3.3V
- **Precision**: ~1.075 kg/count (with 0.75V signal)
- **Best for**: Standard measurements, faster sampling
- **Sampling**: Timer-triggered DMA (dynamic rate)

### ADS1115 (16-bit)
- **Resolution**: 32768 levels (0-32767)
- **Reference**: 4.096V
- **Precision**: ~0.167 kg/count (with 0.75V signal)
- **Best for**: High-precision measurements, 6.4x better than Internal ADC
- **Sampling**: Fixed 860 SPS

**Note**: All keyboard shortcuts are also accessible via the "⌨ Keyboard Shortcuts" button in the Settings panel.

## 🔬 Calibration Process

### Multi-Point Calibration System

The application supports **unlimited calibration points** using least-squares linear regression for optimal accuracy.

1. **Prepare**: Ensure STM32 is connected and streaming
2. **Start Calibration**: Click "Calibrate Left" or "Calibrate Right"
3. **Add Points**: Click "Add Point" to create calibration points
4. **Enter Weight**: Enter known weight for each point (can be in any order)
5. **Capture**: System automatically captures both Internal and ADS1115 ADC values
6. **Repeat**: Add multiple points (3-5+ recommended for best accuracy)
7. **Calculate**: System performs least-squares regression and shows R² quality metric
8. **Save**: Calibration data saved to mode-specific JSON files

### Calibration Quality Metrics
- **R² (Coefficient of Determination)**: Measures fit quality (1.0 = perfect)
- **Maximum Error**: Largest deviation from fitted line
- **Quality Assessment**: Excellent/Good/Acceptable/Poor ratings

### Calibration Files
- `calibration_left_internal.json`: Left side Internal ADC (12-bit) calibration
- `calibration_left_ads1115.json`: Left side ADS1115 (16-bit) calibration
- `calibration_right_internal.json`: Right side Internal ADC calibration
- `calibration_right_ads1115.json`: Right side ADS1115 calibration
- `tare_config.json`: Mode-specific tare baseline values
- `app_settings.json`: Application preferences

## 📊 Data Logging

### Production Logging
- **Format**: Structured text with timestamps and severity levels
- **Location**: `logs/` directory (timestamped files)
- **Real-time Updates**: Live log viewing with auto-scroll
- **Content**: All CAN messages, calibration events, errors, system status
- **Filtering**: Filter by level (Info/Warning/Error/Critical) and source

### Data Logging
- **Format**: CSV with weight measurements and system status
- **Location**: User-specified directory (timestamped files)
- **Content**: Raw ADC, calibrated weight, tared weight, ADC mode, system status, timestamps
- **Control**: Manual start/stop via UI button (no auto-start)
- **File Management**: View, filter, and delete log files via Log Files Manager

### Log Files Manager
- **View All Logs**: Data logs, production logs, CAN monitor exports
- **Filter by Type**: CSV, TXT, CAN exports
- **File Details**: Name, type, size, creation date, path
- **Delete Files**: Selected files or clear all (with confirmation)
- **Open Folder**: Quick access to log directory

## 🔍 Monitor Window

### CAN Message Monitor
- **Real-time Display**: All CAN messages with timestamps
- **Color Coding**: TX (blue) and RX (green) messages
- **Filtering**: By message type, direction, or ID
- **Statistics**: Message counts and rates
- **Decoding**: Human-readable message descriptions
- **Export**: Export captured messages to CSV or text file

### Message Types
- **Raw Data**: 0x200 (Left), 0x201 (Right)
- **Stream Control**: 0x040 (Start Left), 0x041 (Start Right), 0x044 (Stop All)
- **System**: 0x300 (Status), 0x030 (Internal ADC), 0x031 (ADS1115)
- **Bootloader**: 0x510 (App Command), 0x511 (Boot Command), 0x512 (Boot Data), 0x513 (Boot Status)

## ⚡ Performance Optimization

### Multi-threaded Architecture
- **CAN Thread**: Handles incoming messages
- **WeightProcessor Thread**: Dedicated calibration processing with filtering
- **UI Thread**: Updates display at 20Hz
- **Logger Thread**: Asynchronous file I/O

### Weight Filtering
- **EMA (Exponential Moving Average)**: Fast response with configurable alpha (0.0-1.0)
- **SMA (Simple Moving Average)**: Consistent smoothing with configurable window size
- **Filter Enable/Disable**: Master switch for all filtering
- **Separate Filters**: Independent filtering for calibrated and tared weights per side

### Performance Metrics
- **1kHz Data Rate**: Fully supported
- **CPU Usage**: <3.2% at maximum load
- **Memory**: <100MB typical usage
- **Latency**: <50ms end-to-end processing
- **Filter Performance**: <1% CPU overhead for EMA/SMA filtering

## 🛠️ Troubleshooting

### Common Issues

#### Connection Problems
- **Check COM Port**: Ensure correct port selection
- **Driver Issues**: Install USB-CAN adapter drivers (CH341 driver for USB-CAN-A)
- **Baud Rate**: Fixed at 250 kbps (v0.7 protocol)
- **Cable**: Verify USB cable connection
- **Protocol**: USB-CAN-A uses variable-length protocol (5-13 bytes per frame)
  - Frame format: `[0xAA] [Type] [ID_LOW] [ID_HIGH] [DATA...] [0x55]`
  - Supports messages with 0-8 data bytes (DLC)

#### Calibration Issues
- **Stream Required**: Ensure data streaming is active
- **Weight Range**: Use appropriate test weights
- **Stability**: Wait for stable readings before recording
- **File Permissions**: Check write access to calibration files

#### Performance Issues
- **High CPU**: Check for excessive logging
- **Memory Leaks**: Restart application periodically
- **Slow Updates**: Verify UI update timer settings

### Error Codes
- **CAN001**: Connection failed
- **CAL002**: Calibration data invalid
- **TARE003**: Tare operation failed
- **LOG004**: Logging system error

## 📁 File Structure

```
SuspensionPCB_CAN_WPF/
├── MainWindow.xaml              # Main UI layout
├── MainWindow.xaml.cs           # Main UI logic
├── MonitorWindow.xaml           # CAN monitor layout
├── MonitorWindow.xaml.cs        # CAN monitor logic
├── LogsWindow.xaml              # Production logs layout
├── LogsWindow.xaml.cs           # Production logs logic
├── CalibrationDialog.xaml       # Calibration dialog
├── CalibrationDialog.xaml.cs    # Calibration logic
├── CANService.cs                # CAN communication
├── CANMessage.cs                # Message data structures
├── LinearCalibration.cs         # Calibration mathematics
├── TareManager.cs               # Tare management
├── DataLogger.cs                # Data logging
├── ProductionLogger.cs          # Production logging
├── SettingsManager.cs           # Settings persistence
├── WeightProcessor.cs           # Multi-threaded processing
└── README.md                    # This file
```

## 🔄 Protocol v0.7 Compliance

### CAN Message IDs
- **0x200**: Left side raw ADC data
- **0x201**: Right side raw ADC data
- **0x040**: Start left side streaming
- **0x041**: Start right side streaming
- **0x044**: Stop all streams
- **0x300**: System status (on-demand)
- **0x030**: Switch to Internal ADC mode
- **0x031**: Switch to ADS1115 mode
- **0x032**: Request system status
- **0x510**: Bootloader app command
- **0x511**: Bootloader boot command
- **0x512**: Bootloader boot data
- **0x513**: Bootloader boot status

### Data Format
- **Raw Data**: 2 bytes (uint16_t) ADC value
- **Stream Control**: 1 byte rate code (0x01=1Hz, 0x02=100Hz, 0x03=500Hz, 0x04=1kHz)
- **System Status**: 3 bytes (status, errors, ADC mode)
- **Bootloader**: Variable length (commands, data chunks, status)

## 🚀 Getting Started

1. **Launch** the application
2. **Select** your CAN adapter (USB-CAN-A, PCAN, or Simulator)
3. **Connect** to CAN bus
4. **Calibrate** both sides using multi-point calibration
5. **Configure** weight filtering (EMA/SMA) if needed
6. **Start** data streaming
7. **Monitor** real-time weights with filtering
8. **Log** data as needed (manual start/stop)
9. **Manage** log files via Log Files Manager
10. **Update** firmware via bootloader if needed

## 📞 Support

For technical support or feature requests:
- **Issues**: Create GitHub issue
- **Documentation**: Check project wiki
- **Updates**: Monitor repository releases

## 📄 License

This project is licensed under the Apache License 2.0 - see the LICENSE file for details.

---

**Version**: 2.2.0   
**Last Updated**: 12th December 2025
**Compatibility**: STM32 Suspension System v3.1  
**USB-CAN-A Protocol**: Variable-length (Waveshare specification) - Fixed in this version