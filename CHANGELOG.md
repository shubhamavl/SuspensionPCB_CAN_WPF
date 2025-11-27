# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Placeholder for upcoming features

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

