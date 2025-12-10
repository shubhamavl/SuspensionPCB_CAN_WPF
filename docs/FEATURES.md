# SuspensionPCB_CAN_WPF - Complete Feature List

## 📋 Table of Contents
1. [CAN Communication Features](#can-communication-features)
2. [User Interface Features](#user-interface-features)
3. [Calibration Features](#calibration-features)
4. [Tare Management Features](#tare-management-features)
5. [Data Logging Features](#data-logging-features)
6. [Settings & Configuration Features](#settings--configuration-features)
7. [System Status & Monitoring Features](#system-status--monitoring-features)
8. [Performance & Architecture Features](#performance--architecture-features)
9. [Keyboard Shortcuts](#keyboard-shortcuts)
10. [File Management Features](#file-management-features)

---

## CAN Communication Features

### Adapter Support
- ✅ **USB-CAN-A Serial Adapter** - Full support via COM port (CH341 driver)
- ✅ **PCAN Adapter** - Full support via PCANBasic.dll (PEAK System)
- ✅ **Adapter Auto-Detection** - Automatically detects available COM ports
- ✅ **Adapter Selection UI** - Dropdown to switch between adapter types
- ✅ **PCAN Channel Selection** - Support for USB1-USB8 channels
- ✅ **PCAN Availability Check** - Real-time detection of PCAN driver installation

### Protocol Implementation (CAN v0.7)
- ✅ **Semantic Message IDs** - Protocol v0.7 compliant
  - `0x200` - Left side raw ADC data (2 bytes)
  - `0x201` - Right side raw ADC data (2 bytes)
  - `0x040` - Start left side streaming (1 byte rate code)
  - `0x041` - Start right side streaming (1 byte rate code)
  - `0x044` - Stop all streams (empty message)
  - `0x300` - System status response (3 bytes)
  - `0x032` - Request system status (empty message)
  - `0x030` - Switch to Internal ADC mode (empty message)
  - `0x031` - Switch to ADS1115 mode (empty message)

### Transmission Rates
- ✅ **100Hz** - Low-frequency monitoring (10ms interval)
- ✅ **500Hz** - Standard operation (2ms interval)
- ✅ **1kHz** - High-speed data acquisition (1ms interval)
- ✅ **1Hz** - Slow monitoring/debugging (1000ms interval)
- ✅ **Rate Selection UI** - Dropdown in header for easy rate switching
- ✅ **Rate Persistence** - Remembers last selected rate

### CAN Bitrate Support
- ✅ **125 kbps** - Standard CAN speed
- ✅ **250 kbps** - Default protocol speed (v0.7)
- ✅ **500 kbps** - High-speed CAN
- ✅ **1 Mbps** - Maximum CAN speed

### Connection Management
- ✅ **Connect/Disconnect Toggle** - Single button connection control
- ✅ **Connection Status Indicator** - Visual status (green/red indicator)
- ✅ **Auto-Reconnect** - Handles connection failures gracefully
- ✅ **Connection Timeout Detection** - 5-second timeout with notification
- ✅ **Error Messages** - Detailed error reporting for connection issues

### Message Handling
- ✅ **Frame Decoding** - USB-CAN-A variable-length protocol parsing (Waveshare specification)
  - Variable-length frame format: `[0xAA] [Type] [ID_LOW] [ID_HIGH] [DATA...] [0x55]`
  - Frame length: 5-13 bytes (5 + DLC, where DLC = 0-8 bytes)
  - Type byte encoding: bit5=frame type, bit4=frame format, bits 0-3=DLC
  - Supports empty messages (DLC=0) for command messages
  - Dynamic frame parsing based on DLC from Type byte
- ✅ **Message Validation** - CAN ID and data length validation
- ✅ **TX/RX Tracking** - Separate counters for transmitted/received messages
- ✅ **Message Queue** - Thread-safe message buffering
- ✅ **Batch Processing** - Efficient 50-message batch processing for UI

---

## User Interface Features

### Main Window
- ✅ **Modern WPF Design** - Professional color palette and layout
- ✅ **Responsive Layout** - Adapts to different window sizes (1000x600 to 2400x1400)
- ✅ **AVL Branding** - Logo and icon integration
- ✅ **Status Banner** - Animated slide-down notifications
- ✅ **Status Bar** - Real-time connection, stream, and message statistics
- ✅ **Live Clock** - System time display in status bar

### Weight Display Panels
- ✅ **Left Side Panel** - Dedicated left side weight display
- ✅ **Right Side Panel** - Dedicated right side weight display
- ✅ **Large Weight Display** - 48pt bold weight values
- ✅ **Raw ADC Display** - Real-time raw ADC value display
- ✅ **Stream Status Indicators** - Visual indicators (green/gray) for active streams
- ✅ **Calibration Status Icons** - ✓/⚠ indicators for calibration state
- ✅ **Tare Status Display** - Shows tare baseline and status

### Control Buttons
- ✅ **Start Left Stream** - Button to initiate left side data streaming
- ✅ **Start Right Stream** - Button to initiate right side data streaming
- ✅ **Stop All Streams** - Emergency stop for all data streams
- ✅ **Tare Left** - Zero-out left side weight
- ✅ **Tare Right** - Zero-out right side weight
- ✅ **Calibrate Left** - Open left side calibration dialog
- ✅ **Calibrate Right** - Open right side calibration dialog
- ✅ **Reset All Tares** - Clear all tare values
- ✅ **Settings Toggle** - Show/hide advanced settings panel

### Settings Panel
- ✅ **Collapsible Panel** - Expandable/collapsible settings section
- ✅ **CAN Adapter Configuration** - Adapter type, channel, bitrate selection
- ✅ **Save Directory Selection** - Browse button for data directory
- ✅ **Keyboard Shortcuts Button** - Opens shortcuts reference
- ✅ **Configuration Viewer Button** - Opens configuration file viewer

### Additional Windows
- ✅ **Monitor Window** - Real-time CAN message monitoring
- ✅ **Logs Window** - Production log viewer with filtering
- ✅ **Calibration Dialog** - Step-by-step calibration wizard
- ✅ **Configuration Viewer** - View all configuration files

### Visual Feedback
- ✅ **TX Indicator Flash** - Visual feedback when messages are sent
- ✅ **Stream Indicators** - Color-coded stream status (green=active, gray=inactive)
- ✅ **Status Colors** - Green (success), Red (error), Orange (warning)
- ✅ **Animated Status Banner** - Slide-down/slide-up animations

---

## Calibration Features

### Multi-Point Calibration System
- ✅ **Unlimited Calibration Points** - Add as many points as needed (minimum 1 point)
- ✅ **Least-Squares Linear Regression** - Optimal calibration fit using all points
- ✅ **Automatic Zero Point Detection** - Detects weight=0 points automatically
- ✅ **Dual ADC Mode Capture** - Captures both Internal and ADS1115 simultaneously
- ✅ **R² Quality Metric** - Coefficient of determination for calibration quality
- ✅ **Maximum Error Calculation** - Largest deviation from fitted line
- ✅ **Calibration Quality Assessment** - Excellent/Good/Acceptable/Poor ratings
- ✅ **Dynamic Point Management** - Add/remove calibration points on the fly
- ✅ **Configurable Capture Delay** - Delay before recording calibration point
- ✅ **Duplicate Weight Detection** - Warns user about duplicate weight values
- ✅ **Manual ADC Entry** - Enter ADC values manually when stream unavailable

### Calibration Dialog
- ✅ **Step-by-Step Wizard** - Visual stepper interface
- ✅ **Step Visual Indicators** - Color-coded progress (blue=active, green=completed)
- ✅ **Point Status Messages** - Real-time status for each calibration point
- ✅ **Results Popup** - Shows equation, R², max error, and quality metrics
- ✅ **Instructions Popup** - Help text for multi-point calibration process
- ✅ **Side-Specific Calibration** - Separate calibration for Left and Right sides
- ✅ **Point List Management** - Visual list of all calibration points
- ✅ **Point Numbering** - Automatic point numbering and renumbering

### Calibration Persistence
- ✅ **Mode-Specific JSON Storage** - Separate files for Internal and ADS1115 modes
  - `calibration_left_internal.json` - Left side Internal ADC (12-bit)
  - `calibration_left_ads1115.json` - Left side ADS1115 (16-bit)
  - `calibration_right_internal.json` - Right side Internal ADC
  - `calibration_right_ads1115.json` - Right side ADS1115
- ✅ **Auto-Load on Startup** - Calibrations loaded automatically based on ADC mode
- ✅ **Calibration Date Tracking** - Timestamp of when calibration was performed
- ✅ **Calibration Point Storage** - Saves all calibration points for verification
- ✅ **Calibration Mode Tracking** - Tracks which ADC mode each calibration is for

### Calibration Validation
- ✅ **Least-Squares Regression** - Optimal slope and intercept from multiple points
- ✅ **R² Calculation** - Measures how well calibration fits the data (0.0-1.0)
- ✅ **Maximum Error** - Largest deviation between points and fitted line
- ✅ **Point Verification** - Verifies calibration accuracy at all points
- ✅ **Error Percentage** - Calculates error percentage for quality assessment

---

## Tare Management Features

### Tare Operations
- ✅ **Tare Left** - Set left side zero point
- ✅ **Tare Right** - Set right side zero point
- ✅ **Reset Both Tares** - Clear all tare values
- ✅ **Individual Tare Reset** - Reset left or right independently

### Tare Application
- ✅ **Automatic Tare Application** - Applied to calibrated weight in real-time
- ✅ **Tare Baseline Storage** - Remembers baseline weight for each side
- ✅ **Tare Time Tracking** - Timestamp of when tare was performed
- ✅ **Non-Negative Results** - Ensures tared weight never goes negative

### Tare Persistence
- ✅ **JSON File Storage** - `tare_config.json` in Data directory
- ✅ **Auto-Load on Startup** - Tare values loaded automatically
- ✅ **Tare Status Display** - Shows tare status and baseline in UI

### Tare Validation
- ✅ **Calibration Required Check** - Prevents tare without calibration
- ✅ **Tare Status Text** - Human-readable tare status messages
- ✅ **Tare Time Display** - Shows when tare was last performed

---

## Data Logging Features

### CSV Data Logging
- ✅ **Start/Stop Logging** - Control data logging with buttons
- ✅ **Timestamped Log Files** - Files named with date/time: `suspension_log_YYYYMMDD_HHMMSS.csv`
- ✅ **Comprehensive CSV Columns**:
  - Timestamp
  - Side (Left/Right)
  - RawADC
  - CalibratedKg
  - TaredKg
  - TareBaseline
  - CalSlope
  - CalIntercept
  - ADCMode
  - SystemStatus
  - ErrorFlags
  - StatusTimestamp

### Logging Control
- ✅ **Logging Status Indicator** - Visual indicator (green=active, red=stopped)
- ✅ **Sample Counter** - Real-time count of logged samples
- ✅ **Export CSV** - Export current session to new CSV file
- ✅ **Log File Path Display** - Shows current log file location

### Production Logging
- ✅ **Structured Text Logs** - Detailed production logs in `logs/` directory
- ✅ **Log Levels** - Info, Warning, Error, Critical
- ✅ **Timestamped Entries** - All log entries include timestamps
- ✅ **Source Tagging** - Each log entry tagged with source component
- ✅ **Log Rotation** - Automatic file rotation with timestamps
- ✅ **Log Filtering** - Filter by log level in Logs Window
- ✅ **Log Export** - Export logs to text file

### Log File Management
- ✅ **Log Files Manager Window** - Dedicated window for log file management
- ✅ **View All Log Files** - Data logs, production logs, CAN monitor exports
- ✅ **Filter by Type** - Filter by CSV, TXT, CAN exports
- ✅ **File Details** - Name, type, size, creation date, path
- ✅ **Delete Files** - Delete selected files or clear all (with confirmation)
- ✅ **Open Folder** - Quick access to log directory in Explorer
- ✅ **File Statistics** - Count and total size of log files
- ✅ **Automatic Directory Creation** - Creates logs directory if needed
- ✅ **Portable Log Storage** - Logs stored next to executable
- ✅ **Timestamped Files** - All log files created with date/time stamps
- ✅ **Log File Size Tracking** - Tracks log file size
- ✅ **Log Line Count** - Counts lines in log file

---

## Settings & Configuration Features

### Application Settings
- ✅ **COM Port Persistence** - Remembers last used COM port
- ✅ **Transmission Rate Persistence** - Remembers last selected rate
- ✅ **Save Directory Persistence** - Remembers data directory location
- ✅ **ADC Mode Persistence** - Remembers last ADC mode (Internal/ADS1115)
- ✅ **System Status Persistence** - Remembers last known system status

### Settings File
- ✅ **JSON Format** - `settings.json` in application directory
- ✅ **Auto-Save** - Settings saved automatically on changes
- ✅ **Auto-Load** - Settings loaded on application startup
- ✅ **Last Saved Timestamp** - Tracks when settings were last modified

### Configuration Management
- ✅ **Configuration Viewer** - Dedicated window to view all config files
- ✅ **File Location Display** - Shows paths to all configuration files
- ✅ **Open in Notepad** - Quick access to edit config files
- ✅ **Refresh Configuration** - Reload configuration data
- ✅ **Configuration Statistics** - Shows file sizes, counts, etc.

### Adapter Configuration
- ✅ **Adapter Type Selection** - USB-CAN-A Serial, PCAN, or Simulator
- ✅ **PCAN Channel Selection** - USB1-USB8 channel selection
- ✅ **Bitrate Selection** - 125/250/500/1000 kbps
- ✅ **Configuration Persistence** - Adapter settings saved to `Suspension_Config.json`
- ✅ **Simulator Adapter** - Software-based CAN adapter for testing
- ✅ **Simulator Control Window** - Real-time parameter adjustment for simulator
- ✅ **Simulated Weight Generation** - Configurable left/right weight simulation

---

## System Status & Monitoring Features

### System Status
- ✅ **Status Request** - On-demand system status request (0x032)
- ✅ **Status Response Handling** - Processes 0x300 status messages
- ✅ **Status Display** - Shows system status (OK/Warning/Error/Critical)
- ✅ **Error Flags Display** - Shows error flags in hex format
- ✅ **ADC Mode Display** - Shows current ADC mode (Internal/ADS1115)
- ✅ **Real-Time Data Rate** - Calculates and displays data rate
- ✅ **Rate Timeout** - Resets to "--" after 5 seconds of no updates
- ✅ **Dynamic UI Updates** - Status UI updates in real-time

### Status History
- ✅ **Status History Manager** - Tracks last 100 status entries
- ✅ **Status History Window** - DataGrid view of status history
- ✅ **Status Statistics** - Total entries, OK/Warning/Error counts
- ✅ **Time Range Filtering** - Filter status by time range
- ✅ **Status Entry Details** - Timestamp, status, mode, error flags
- ✅ **Clear History** - Button to clear status history

### Monitor Window
- ✅ **Real-Time Message Display** - Shows all CAN messages
- ✅ **Message Filtering** - Filter by direction, ID, type
- ✅ **Color Coding** - TX (blue) and RX (green) messages
- ✅ **Message Decoding** - Human-readable message descriptions
- ✅ **Message Count** - Real-time message counter
- ✅ **Auto-Scroll** - Automatically scrolls to latest messages
- ✅ **Message Limit** - Keeps last 1000 messages in memory
- ✅ **Export Feature** - Export captured messages to CSV or text file
- ✅ **Live CAN Service Connection** - Connected to real-time CAN service

### ADC Mode Control
- ✅ **Internal ADC Mode** - Switch to 12-bit internal ADC (0x030)
- ✅ **ADS1115 Mode** - Switch to 16-bit ADS1115 (0x031)
- ✅ **Mode Indicators** - Visual indicators for current mode
- ✅ **Mode Persistence** - Remembers last ADC mode
- ✅ **Mode Toggle Button** - Unified button to switch between modes

---

## Performance & Architecture Features

### Multi-Threaded Architecture
- ✅ **CAN Thread** - Dedicated thread for CAN message reception
- ✅ **WeightProcessor Thread** - Dedicated thread for calibration processing (1kHz capable)
- ✅ **UI Thread** - Main thread for UI updates (20Hz refresh rate)
- ✅ **Logger Thread** - Asynchronous file I/O for logging

### Performance Optimizations
- ✅ **Lock-Free Reads** - Volatile variables for latest weight data
- ✅ **Batched UI Updates** - Processes 50 messages per UI update cycle
- ✅ **Queue Size Limits** - Prevents memory leaks (max 100 items in queue)
- ✅ **Message Buffer Management** - Efficient frame buffer processing
- ✅ **Reduced Verbose Logging** - Optimized for 1kHz data rates

### Weight Processing
- ✅ **WeightProcessor Class** - Dedicated high-performance processor
- ✅ **Concurrent Queue** - Thread-safe data queue for raw ADC values
- ✅ **Processed Data Snapshots** - Lock-free access to latest processed data
- ✅ **Drop Counter** - Tracks dropped messages when queue is full
- ✅ **Processed Counter** - Tracks successfully processed messages

### Weight Filtering System
- ✅ **EMA Filter** - Exponential Moving Average with configurable alpha (0.0-1.0)
- ✅ **SMA Filter** - Simple Moving Average with configurable window size
- ✅ **Filter Enable/Disable** - Master switch for all filtering
- ✅ **Separate Filters** - Independent filtering for calibrated and tared weights
- ✅ **Per-Side Filters** - Independent filters for left and right sides
- ✅ **Real-Time Configuration** - Filter settings apply immediately
- ✅ **Filter Persistence** - Filter settings saved across sessions
- ✅ **Filter Reset** - Automatic reset on calibration/tare changes

### Error Handling
- ✅ **Comprehensive Try-Catch** - Error handling throughout application
- ✅ **Error Logging** - All errors logged to production logs
- ✅ **User-Friendly Error Messages** - Clear error messages in UI
- ✅ **Graceful Degradation** - Application continues running after errors

---

## Keyboard Shortcuts

### Connection
- ✅ **Ctrl+C** - Connect to CAN bus
- ✅ **Ctrl+D** - Disconnect from CAN bus
- ✅ **F5** - Connect if not connected

### Streaming Control
- ✅ **Ctrl+L** - Start left side streaming
- ✅ **Ctrl+R** - Start right side streaming
- ✅ **Ctrl+S** - Stop all streams

### ADC Mode
- ✅ **Ctrl+I** - Switch to Internal ADC mode (12-bit)
- ✅ **Ctrl+A** - Switch to ADS1115 mode (16-bit)

### Windows
- ✅ **Ctrl+T** - Toggle settings panel
- ✅ **Ctrl+M** - Open monitor window
- ✅ **Ctrl+P** - Open production logs

### Help
- ✅ **F1** - Show help (keyboard shortcuts dialog)

### Tare
- ✅ **Ctrl+T** - Reset all tares (conflicts with settings toggle, but implemented)

---

## File Management Features

### Portable Deployment
- ✅ **Portable File Structure** - All files next to executable
- ✅ **Data Directory** - `Data/` folder for calibration and tare files
- ✅ **Logs Directory** - `Logs/` folder for production logs
- ✅ **Settings File** - `settings.json` next to executable
- ✅ **Configuration File** - `Suspension_Config.json` for adapter settings

### File Paths
- ✅ **PathHelper Class** - Centralized path management
- ✅ **Portable Path Resolution** - Works with single-file deployments
- ✅ **Auto-Directory Creation** - Creates directories if they don't exist
- ✅ **Relative Path Support** - All paths relative to executable

### Configuration Files
- ✅ **Calibration Files** - `calibration_left.json`, `calibration_right.json`
- ✅ **Tare Config File** - `tare_config.json`
- ✅ **Settings File** - `settings.json`
- ✅ **Adapter Config File** - `Suspension_Config.json`

### File Operations
- ✅ **JSON Serialization** - All config files in JSON format
- ✅ **File Existence Checks** - Validates files before loading
- ✅ **Error Handling** - Graceful handling of missing/corrupt files
- ✅ **File Opening** - Open config files in Notepad from UI
- ✅ **Directory Opening** - Open data directory in Explorer from UI

---

## Additional Features

### Status History Manager
- ✅ **Circular Buffer** - Maintains last 100 status entries
- ✅ **Statistics** - Total, OK, Warning, Error counts
- ✅ **Time Range Queries** - Get entries within time range
- ✅ **Latest Entry** - Get most recent status entry

### Production Logger
- ✅ **Singleton Pattern** - Single instance throughout application
- ✅ **Observable Collection** - UI-bound log entries
- ✅ **Log Level Filtering** - Filter by Info/Warning/Error/Critical
- ✅ **File Writing** - Automatic log file writing
- ✅ **Log Export** - Export logs to text file

### Configuration Viewer
- ✅ **Settings Display** - Shows all application settings
- ✅ **Calibration Display** - Shows left/right calibration data
- ✅ **Tare Display** - Shows tare configuration
- ✅ **File Statistics** - Shows CSV file count and total size
- ✅ **Quick File Access** - Open files/directories from viewer

### UI Enhancements
- ✅ **Status Banner Animations** - Slide-down/slide-up animations
- ✅ **TX Indicator Flash** - Visual feedback for sent messages
- ✅ **Stream Status Colors** - Color-coded stream indicators
- ✅ **Calibration Status Icons** - Visual calibration state indicators
- ✅ **Responsive Design** - Adapts to window resizing

---

## Update & Version Management Features

### Version History & Rollback
- ✅ **View All Releases** - Browse all GitHub releases with dates and notes
- ✅ **Version Selection Dialog** - Select any previous version to install
- ✅ **Downgrade Warning** - Warns user before installing older version
- ✅ **Release Notes Display** - Shows release notes for each version
- ✅ **Version Comparison** - Compares current vs selected version

### Auto-Update System
- ✅ **GitHub Releases Integration** - Checks for updates from GitHub
- ✅ **Automatic Update Check** - Checks on application startup
- ✅ **Manual Update Check** - "Check for Updates" button
- ✅ **Secure Download** - SHA-256 hash verification
- ✅ **Update Installation** - Seamless update via external updater

## Firmware Update Features

### Bootloader Support
- ✅ **Bootloader Protocol** - Complete implementation (0x510, 0x511, 0x512, 0x513)
- ✅ **Firmware Update Service** - Handles BIN file upload to STM32
- ✅ **Progress Tracking** - Progress bar and status updates
- ✅ **CRC-32 Verification** - Firmware integrity verification
- ✅ **Bootloader Entry/Exit** - Commands to enter/exit bootloader
- ✅ **Boot Info Query** - Query bootloader information
- ✅ **Feature Control** - Enable/disable bootloader features via settings

## Data Timeout & Connection Management

### Data Timeout Detection
- ✅ **Configurable Timeout** - 1-30 seconds (default 5 seconds)
- ✅ **Automatic Stream Stopping** - Stops streams when timeout detected
- ✅ **Visual Notification** - Status banner when data timeout occurs
- ✅ **Connection Status Tracking** - Monitors connection health

## UI & Usability Features

### Tools Menu
- ✅ **Consolidated Menu** - Single "Tools" dropdown in header
- ✅ **Quick Access** - Monitor, Logs, Log Files Manager
- ✅ **Cleaner Header** - Reduced button clutter

### Settings Panel
- ✅ **Scrollable Panel** - Scrollable settings with max height
- ✅ **Settings Info Dialogs** - Contextual help for settings
- ✅ **Info Buttons** - Help buttons throughout settings panel

## Summary Statistics

- **Total Features**: 250+
- **CAN Protocol Messages**: 13 message types (including bootloader)
- **Transmission Rates**: 4 rates (1Hz, 100Hz, 500Hz, 1kHz)
- **Adapter Types**: 3 (USB-CAN-A Serial, PCAN, Simulator)
- **Windows**: 5 (Main, Monitor, Logs, Configuration Viewer, Log Files Manager)
- **Dialogs**: 5 (Calibration, Keyboard Shortcuts, Version Selection, Manual ADC Entry, Side Selection)
- **Keyboard Shortcuts**: 12 shortcuts
- **Configuration Files**: 8+ file types (mode-specific calibrations)
- **Log File Types**: 3 (CSV data logs, Production text logs, CAN monitor exports)
- **Threads**: 4 dedicated threads
- **UI Update Rate**: 20Hz (50ms intervals)
- **Max Data Rate Supported**: 1kHz (1000 messages/second)
- **Filter Types**: 2 (EMA, SMA)

---

**Last Updated**: January 2025  
**Version**: 2.0.1 (Unreleased)  
**Protocol Compatibility**: CAN v0.7  
**USB-CAN-A Protocol**: Variable-length (Waveshare specification)  
**Framework**: .NET 8.0 WPF



