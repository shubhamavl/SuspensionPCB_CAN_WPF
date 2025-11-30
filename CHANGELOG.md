# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0] - 30-11-2025

### Added
- **Multi-Point Calibration System**
  - Support for unlimited calibration points (minimum 1 point required)
  - Least-squares linear regression for optimal calibration fit
  - Automatic zero point detection (weight = 0)
  - Dual ADC mode calibration (captures both Internal and ADS1115 simultaneously)
  - R² (coefficient of determination) calculation for calibration quality
  - Maximum error calculation across all calibration points
  - Calibration quality assessment (Excellent/Good/Acceptable/Poor)
  - Add/remove calibration points dynamically
  - Configurable capture delay before recording calibration point
  - Visual calibration point management in dialog
  - Separate calibration for Internal 12-bit and ADS1115 16-bit ADC modes
- **Weight Filtering System**
  - EMA (Exponential Moving Average) filter with configurable alpha (0.0-1.0)
  - SMA (Simple Moving Average) filter with configurable window size
  - Filter enable/disable toggle
  - Separate filtering for calibrated and tared weights
  - Independent filters for left and right sides
  - Real-time filter configuration updates
  - Filter settings persistence across sessions
- **Version History and Rollback Feature**
  - View all GitHub releases with release dates and notes
  - Install any previous version (with downgrade warning)
  - Version selection dialog with release information
  - Access via "Check for Updates" → "View All Versions"
  - Downgrade warning dialog with compatibility notice
- **Log Files Manager Window**
  - View all log files (Data logs, Production logs, CAN monitor exports)
  - Filter by file type (CSV, TXT, CAN exports)
  - File details: name, type, size, creation date, path
  - Delete selected files or clear all (with confirmation)
  - Open data directory in Explorer
  - File statistics (count and total size)
  - Timestamped file creation (always creates new files with date/time)
- **CAN Monitor Export Feature**
  - Export captured CAN messages to CSV or text file
  - Includes timestamp, ID, direction, data, and description
  - Accessible from CAN Monitor window
- **Data Timeout Detection**
  - Automatic detection when CAN messages stop
  - Configurable timeout (1-30 seconds, default 5 seconds)
  - Auto-stops streams when timeout detected
  - Visual notification when data timeout occurs
- **System Status UI Enhancements**
  - Real-time data rate calculation and display
  - Dynamic status updates (OK/Warning/Error/Critical)
  - Error flags display with hex format
  - Last update timestamp
  - Rate timeout (resets to "--" after 5 seconds of no updates)
- **Tools Menu Consolidation**
  - Consolidated window access buttons into dropdown menu
  - Cleaner header with "Tools" dropdown
  - Quick access to Monitor, Logs, and Log Files windows
- **Bootloader Feature Control**
  - Setting to enable/disable all bootloader-related functionality
  - Hides firmware update UI when disabled
  - Prevents automatic bootloader queries when disabled
- **Firmware Update System**
  - Complete bootloader protocol implementation (0x510, 0x511, 0x512, 0x513)
  - Firmware update service with progress tracking
  - BIN file upload to STM32 via CAN bootloader
  - Bootloader entry and exit commands
  - Boot info query and display
  - CRC-32 verification for firmware integrity
  - Progress bar and status updates during firmware update
- **Simulator CAN Adapter**
  - Software-based CAN adapter for testing without hardware
  - Configurable simulated weight data
  - Simulator control window for real-time parameter adjustment
  - Simulated left/right weight generation
  - Useful for development and testing
- **Manual ADC Entry Dialog**
  - Manual entry of ADC values when stream is unavailable
  - Support for both Internal and ADS1115 ADC modes
  - Useful for offline calibration or testing
- **Side Selection Dialog**
  - Dialog for selecting left or right side for operations
  - Used in calibration and tare workflows
- **ADC Mode-Specific Calibration and Tare**
  - Independent calibration storage for Internal (12-bit) and ADS1115 (16-bit) ADC modes
  - Separate tare baselines for each ADC mode per side
  - Automatic mode detection and calibration/tare application
  - Mode-specific calibration file storage (calibration_left_internal.json, calibration_left_ads1115.json, etc.)
  - Prevents calibration/tare conflicts when switching between ADC modes
- **Settings Info Dialog**
  - Contextual help dialogs for various settings
  - Detailed explanations for weight filtering, display, and advanced settings
  - Accessible via info buttons throughout settings panel

### Changed
- **Calibration System Enhancements**
  - Upgraded from two-point to multi-point calibration support
  - Improved calibration accuracy with least-squares regression
  - Better calibration quality metrics (R², max error)
  - Dual ADC mode support (captures both modes in single operation)
  - Enhanced calibration dialog with point management
  - Configurable calibration capture delay
  - Duplicate weight detection with user warnings
  - Automatic zero point detection
  - Manual ADC entry option when stream unavailable
  - Side selection dialog for calibration operations
  - Mode-specific calibration storage (Internal vs ADS1115)
  - Independent tare management per ADC mode
- **Weight Processing Improvements**
  - Added configurable filtering (EMA/SMA/None)
  - Improved weight stability with filtering options
  - Separate filter state for calibrated vs tared weights
  - Filter configuration in settings panel
- **Production Logs Window Improvements**
  - Real-time log updates via collection change notifications
  - Fixed log level extraction (uses actual Level property)
  - Auto-scroll to latest entries
  - Improved timing and performance
  - Better source display (uses actual source from logger)
- **Logging System Improvements**
  - Removed auto-start logging feature (manual control only)
  - Logging checkbox in LogsWindow reflects actual state (read-only)
  - New timestamped log file created each time logging starts
  - Improved logging UI state initialization
  - Thread-safe logging state checks with double-lock pattern
- **Settings Panel**
  - Made scrollable for better navigation with many settings
  - Max height constraint with auto-scrollbar
  - Added filter configuration settings (type, alpha, window size, enable/disable)
  - Added calibration capture delay setting
- **CAN Bus Monitor**
  - Fixed TX messages not displaying (now shows both TX and RX)
  - Connected to live CANService for real-time monitoring
  - Proper message direction tracking
- **UI Layout Optimization**
  - Reduced header clutter by consolidating tools into menu
  - Better organization of window access buttons
  - Improved visual hierarchy

### Fixed
- **CAN Monitor Issues**
  - Fixed TX messages not appearing (was showing "no TX only RX")
  - Fixed MonitorWindow not connected to CANService
  - Fixed message direction not being set correctly
- **System Status Issues**
  - Fixed system status UI not updating dynamically
  - Fixed status messages not being processed correctly
  - Fixed data rate not displaying (showed "--")
- **Logging Issues**
  - Fixed logging UI state not initializing correctly on startup
  - Fixed logging always appearing "on" regardless of actual state
  - Fixed race condition in logging state checks
  - Fixed thread-safety issues in DataLogger
- **Production Logs Window**
  - Fixed timing issues with log updates
  - Fixed log level extraction (was parsing strings, now uses enum)
  - Fixed real-time updates not working
  - Fixed collection change event handling
- **Threading Issues**
  - Fixed status bar update errors from background threads
  - Added Dispatcher.Invoke for all UI updates from background threads
- **Build Errors**
  - Fixed NotifyCollectionChangedAction enum usage
  - Fixed OnClosed method placement in LogsWindow

## [1.2.0] - 27-11-2025

### Added
- **Auto-update system with GitHub Releases integration**
  - Automatic update checking on application startup
  - Manual update check via "Check for Updates" button in header
  - Secure download with SHA-256 hash verification
  - Seamless update installation using external updater helper
  - Update progress indicator and status notifications
- **Enhanced CAN adapter support**
  - PCAN adapter support (USB1-USB8 channels) via Peak.PCANBasic.NET
  - USB-CAN-A Serial adapter support via COM port
  - Adapter auto-detection and availability checking
  - Adapter selection dropdown in settings panel
  - Real-time PCAN driver detection
- **Advanced calibration system**
  - Two-point linear calibration with step-by-step wizard
  - Visual calibration progress indicators
  - Calibration quality assessment (Excellent/Good/Acceptable/Poor)
  - Accuracy verification with error percentage calculation
  - Side-specific calibration (independent left/right calibration)
  - Calibration data persistence in JSON format
- **Comprehensive data logging**
  - CSV data logging with timestamped files
  - Production logging system with severity levels (Info/Warning/Error/Critical)
  - Log filtering and export functionality
  - System status tracking in log files
  - Automatic log file rotation
- **System status monitoring**
  - On-demand system status requests
  - Status history tracking (last 100 entries)
  - Status statistics (OK/Warning/Error counts)
  - Time range filtering for status history
  - ADC mode display and switching (Internal/ADS1115)
- **Monitor window for CAN debugging**
  - Real-time CAN message display with color coding (TX=blue, RX=green)
  - Message filtering by direction, ID, and type
  - Human-readable message descriptions
  - Auto-scroll to latest messages
  - Message count tracking (last 1000 messages)
- **Configuration management**
  - Configuration viewer window for all settings
  - Quick file access (open in Notepad)
  - File statistics display
  - Settings persistence across sessions
  - Portable configuration storage (next to executable)
- **Keyboard shortcuts for efficient operation**
  - Ctrl+C: Connect to CAN bus
  - Ctrl+D: Disconnect from CAN bus
  - Ctrl+L: Start left side streaming
  - Ctrl+R: Start right side streaming
  - Ctrl+S: Stop all streams
  - Ctrl+T: Toggle settings panel
  - Ctrl+M: Open monitor window
  - Ctrl+P: Open production logs
  - Ctrl+I: Switch to Internal ADC mode
  - Ctrl+A: Switch to ADS1115 mode
  - F1: Show help (keyboard shortcuts dialog)
- **Portable deployment system**
  - Self-contained executable (includes .NET 8.0 runtime)
  - Single-file deployment option
  - All data stored next to executable (fully portable)
  - No installation required
  - USB drive compatible
- **Performance optimizations**
  - Multi-threaded architecture (CAN thread, WeightProcessor thread, UI thread, Logger thread)
  - Lock-free reads for latest weight data
  - Batched UI updates (50 messages per cycle)
  - Queue size limits to prevent memory leaks
  - Optimized for 1kHz data rates
  - CPU usage <3.2% at maximum load
  - Memory usage <100MB typical

### Changed
- **Improved user interface**
  - Modern WPF design with professional color palette
  - Responsive layout (adapts to 1000x600 to 2400x1400 window sizes)
  - Animated status banner with slide-down/slide-up animations
  - Enhanced visual feedback (TX indicator flash, stream status colors)
  - Large weight display (48pt bold) for better visibility
  - Improved calibration dialog with visual stepper interface
- **Enhanced error handling**
  - Comprehensive error reporting throughout application
  - User-friendly error messages in UI
  - Graceful degradation (application continues after errors)
  - Detailed error logging to production logs
- **Improved connection management**
  - Connection timeout detection (5-second timeout with notification)
  - Auto-reconnect handling for connection failures
  - Detailed error messages for connection issues
  - Connection status indicators (green/red visual feedback)
- **Better data processing**
  - Support for multiple transmission rates (1Hz, 100Hz, 500Hz, 1kHz)
  - Rate persistence (remembers last selected rate)
  - Enhanced message validation (CAN ID and data length checks)
  - Improved frame decoding for USB-CAN-A 20-byte format
- **Enhanced tare management**
  - Tare status display with baseline and timestamp
  - Individual tare reset (left/right independently)
  - Tare persistence across restarts
  - Non-negative tare results enforcement

### Fixed
- Resolved calibration data persistence issues
- Fixed memory leaks in high-frequency data processing
- Improved connection timeout handling
- Fixed UI update issues during high data rates
- Resolved file path issues in portable deployments

### Technical Details
- Protocol: CAN v0.7 compliant
- Transmission rates: 1Hz, 100Hz, 500Hz, 1kHz
- ADC modes: Internal 12-bit (4096 levels), ADS1115 16-bit (32768 levels)
- CAN bitrates: 125 kbps, 250 kbps, 500 kbps, 1 Mbps
- Framework: .NET 8.0 WPF
- Platform: Windows 10/11 (64-bit)
- Architecture: Multi-threaded with lock-free data access
- Performance: <3.2% CPU at 1kHz, <100MB memory typical

## [1.0.0] - 26-11-2025

### Added
- Initial release of SuspensionPCB_CAN_WPF
- Real-time weight monitoring for left and right sides
- CAN bus communication support (USB-CAN-A Serial and PCAN adapters)
- Two-point linear calibration system
- Tare management with persistent storage
- Data logging to CSV files
- Production logging system
- Auto-update functionality via GitHub Releases
- Portable deployment (no installation required)
- Multi-threaded architecture for high-performance processing (1kHz capable)
- Monitor window for CAN message debugging
- Configuration viewer for all settings
- Keyboard shortcuts for efficient operation

### Technical Details
- Protocol: CAN v0.7 compliant
- Transmission rates: 1Hz, 100Hz, 500Hz, 1kHz
- ADC modes: Internal 12-bit, ADS1115 16-bit
- Framework: .NET 8.0 WPF
- Platform: Windows 10/11 (64-bit)

---

## How to Add Release Notes

When creating a new release, add your changes to the `[Unreleased]` section above, then move them to a new version section when you tag the release.

### Example:

```markdown
## [Unreleased]

### Added
- New feature: Auto-calibration wizard
- Improved error handling for connection failures

### Changed
- Updated UI colors for better visibility
- Optimized data processing performance

### Fixed
- Fixed issue where calibration data wasn't saving
- Resolved memory leak in high-frequency mode

## [1.1.0] - 2025-02-XX

### Added
- Auto-calibration wizard
- Connection retry mechanism

### Changed
- UI color scheme updated
- Performance improvements (30% faster processing)

### Fixed
- Calibration data persistence issue
- Memory leak in 1kHz mode
```

