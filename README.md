# Weight Measurement System Monitor - PC3 Interface

A professional Windows WPF application for real-time weight monitoring and calibration of STM32-based suspension systems via CAN bus communication.

## 🚀 Features

### Core Functionality
- **Real-time Weight Display**: Live monitoring of left and right side weights
- **CAN Bus Communication**: Protocol v0.7 compliant communication at 250 kbps
- **High-Performance Processing**: Optimized for 1kHz data rates with multi-threaded architecture
- **Dual-Side Calibration**: Independent calibration for left and right sides
- **Tare Management**: Zero-point adjustment with persistent storage
- **Data Logging**: Comprehensive logging with production-grade file output

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
- **Error Handling**: Comprehensive error reporting and recovery
- **Production Logging**: Detailed logs for troubleshooting and analysis

## 📋 System Requirements

- **OS**: Windows 10/11 (64-bit)
- **Framework**: .NET 6.0 or later
- **Hardware**: USB-CAN adapter (250 kbps)
- **Memory**: 4GB RAM minimum, 8GB recommended
- **Storage**: 100MB for application and logs

## 🔧 Installation

1. **Download** the latest release from the repository
2. **Extract** to your preferred directory
3. **Run** `SuspensionPCB_CAN_WPF.exe`
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
| `F1` | Show help |

## 🔬 Calibration Process

### Two-Point Linear Calibration

1. **Prepare**: Ensure STM32 is connected and streaming
2. **Zero Point**: Place no weight on the side to be calibrated
3. **Start Calibration**: Click "Calibrate Left" or "Calibrate Right"
4. **Record Zero**: System captures raw ADC value
5. **Known Weight**: Place known weight (e.g., 10kg) on the side
6. **Record Weight**: System captures second raw ADC value
7. **Calculate**: Linear equation: `Weight = (ADC × Slope) + Intercept`
8. **Save**: Calibration data saved to JSON file

### Calibration Files
- `calibration_left.json`: Left side calibration data
- `calibration_right.json`: Right side calibration data
- `tare_config.json`: Tare baseline values
- `app_settings.json`: Application preferences

## 📊 Data Logging

### Production Logging
- **Format**: Structured text with timestamps
- **Location**: `logs/` directory
- **Rotation**: Automatic file rotation
- **Content**: All CAN messages, calibration events, errors

### Data Logging
- **Format**: CSV with weight measurements
- **Location**: User-specified directory
- **Content**: Raw ADC, calibrated weight, tared weight, timestamps
- **Control**: Start/stop via UI button

## 🔍 Monitor Window

### CAN Message Monitor
- **Real-time Display**: All CAN messages with timestamps
- **Color Coding**: TX (blue) and RX (green) messages
- **Filtering**: By message type, direction, or ID
- **Statistics**: Message counts and rates
- **Decoding**: Human-readable message descriptions

### Message Types
- **Raw Data**: 0x200 (Left), 0x201 (Right)
- **Stream Control**: 0x040 (Start Left), 0x041 (Start Right), 0x044 (Stop All)
- **System**: 0x300 (Status), 0x030 (Internal ADC), 0x031 (ADS1115)

## ⚡ Performance Optimization

### Multi-threaded Architecture
- **CAN Thread**: Handles incoming messages
- **WeightProcessor Thread**: Dedicated calibration processing
- **UI Thread**: Updates display at 20Hz
- **Logger Thread**: Asynchronous file I/O

### Performance Metrics
- **1kHz Data Rate**: Fully supported
- **CPU Usage**: <3.2% at maximum load
- **Memory**: <100MB typical usage
- **Latency**: <50ms end-to-end processing

## 🛠️ Troubleshooting

### Common Issues

#### Connection Problems
- **Check COM Port**: Ensure correct port selection
- **Driver Issues**: Install USB-CAN adapter drivers
- **Baud Rate**: Fixed at 250 kbps (v0.7 protocol)
- **Cable**: Verify USB cable connection

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

### Data Format
- **Raw Data**: 2 bytes (uint16_t) ADC value
- **Stream Control**: Empty message (0 bytes)
- **System Status**: 3 bytes (status, errors, ADC mode)

## 🚀 Getting Started

1. **Launch** the application
2. **Select** your COM port
3. **Connect** to CAN bus
4. **Calibrate** both sides
5. **Start** data streaming
6. **Monitor** real-time weights
7. **Log** data as needed

## 📞 Support

For technical support or feature requests:
- **Issues**: Create GitHub issue
- **Documentation**: Check project wiki
- **Updates**: Monitor repository releases

## 📄 License

This project is licensed under the Apache License 2.0 - see the LICENSE file for details.

---

**Version**: 1.0.0  
**Last Updated**: January 2025  
**Compatibility**: STM32 Suspension System v3.1