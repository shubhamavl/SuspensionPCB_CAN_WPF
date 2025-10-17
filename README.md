# Suspension System Monitor

## Overview

This is a production-ready PC3 application for the LMV Suspension System that communicates with an STM32 microcontroller via CAN bus. The application handles calibration, tare functionality, real-time data display, and data logging with comprehensive logging capabilities.

## Features

### Core Functionality
- **Real-time Data Display**: Shows raw ADC values, calibrated weights, and tare-adjusted weights for both left and right sides
- **2-Point Calibration**: Linear calibration system for converting raw ADC values to weight measurements
- **Tare Management**: Zero-offset functionality for both sides independently
- **Data Logging**: CSV export with timestamp, raw, calibrated, and tared values
- **CAN Communication**: Ultra-minimal v0.7 protocol with semantic CAN IDs
- **Production Logging**: Configurable logging system with severity levels and UI visibility control

### Protocol (v0.7)
- **Raw Data**: 2-byte messages (75% bandwidth reduction)
- **Stream Control**: 1-byte start commands, 0-byte stop commands
- **Semantic IDs**: Message type encoded in CAN ID for maximum efficiency
- **On-demand Status**: No periodic overhead

## Quick Start Guide

### 1. Hardware Setup
1. Connect STM32 suspension board to PC via USB-CAN adapter
2. Ensure load cells are properly connected to ADC channels
3. Power on the STM32 board

### 2. Software Setup
1. Run `SuspensionPCB_CAN_WPF.exe`
2. Select COM port and connect to CAN bus
3. Verify connection status

### 3. Calibration Process
1. **Calibrate Left Side**:
   - Click "Calibrate Left" button
   - Place known weight on left side (covers Ch0+Ch1)
   - Capture Point 1: Enter known weight
   - Capture Point 2: Add more weight, enter total weight
   - Click "Calculate" and "Save"

2. **Calibrate Right Side**:
   - Click "Calibrate Right" button
   - Place known weight on right side (covers Ch2+Ch3)
   - Follow same process as left side

### 4. Tare Process
1. **Tare Left**: Click "Tare Left" to zero the left side
2. **Tare Right**: Click "Tare Right" to zero the right side
3. **Reset Tares**: Click "Reset All Tares" to clear all tare offsets

### 5. Data Streaming
1. Select transmission rate (100Hz, 500Hz, 1kHz, 1Hz)
2. Click "Request Suspension" to start left side streaming
3. Click "Request Axle" to start right side streaming
4. Click "Stop All" to stop all streams

### 6. Data Logging
1. Click "Start Logging" to begin data recording
2. Click "Stop Logging" to end recording
3. Click "Export CSV" to save logged data

### 7. Production Logging System
1. Switch to "Logs" tab to view system logs
2. Use checkboxes to filter by severity (Info, Warning, Error, Critical)
3. Adjust minimum log level to control verbosity
4. Click "Clear Logs" to reset log display
5. Click "Export Logs" to save logs to file

## File Structure

### Calibration Files
- `Left_calibration.json`: Left side calibration coefficients
- `Right_calibration.json`: Right side calibration coefficients

### Tare Files
- `tare_settings.json`: Tare baseline values for both sides

### Log Files
- `suspension_log_YYYYMMDD_HHMMSS.csv`: Data logging output
- `suspension_log_YYYYMMDD_HHMMSS.txt`: Production logging output

## CSV Log Format

```csv
Timestamp,Side,RawADC,CalibratedKg,TaredKg,TareBaselineKg,CalSlope,CalIntercept,ADCMode
2025-01-15 10:30:15.123,Left,2048,25.5,0.0,25.5,0.0125,0.0,0
2025-01-15 10:30:15.124,Right,1984,24.8,0.0,24.8,0.0125,0.0,0
```

## CAN Protocol Details (v0.7)

### Message IDs
- `0x200`: Left side raw ADC data (2 bytes)
- `0x201`: Right side raw ADC data (2 bytes)
- `0x040`: Start left side streaming (1 byte rate)
- `0x041`: Start right side streaming (1 byte rate)
- `0x044`: Stop all streams (0 bytes)
- `0x300`: System status (3 bytes, on-demand)
- `0x030`: Switch to Internal ADC mode (0 bytes)
- `0x031`: Switch to ADS1115 mode (0 bytes)

### Rate Codes
- `0x01`: 100Hz
- `0x02`: 500Hz
- `0x03`: 1kHz
- `0x05`: 1Hz

## Troubleshooting

### Common Issues

1. **"Calibration Required" Message**
   - Solution: Perform 2-point calibration for the side you want to stream

2. **CAN Connection Failed**
   - Check COM port selection
   - Verify USB-CAN adapter is working
   - Ensure STM32 is powered and responding

3. **No Data Received**
   - Verify STM32 is sending data
   - Check CAN bus termination (120Ω)
   - Confirm baud rate (250 kbps)

4. **Inaccurate Weight Readings**
   - Recalibrate with known weights
   - Check load cell connections
   - Verify tare is properly set

### Error Messages
- **"Left/Right side calibration is required"**: Calibrate before streaming
- **"CAN service not connected"**: Check connection and COM port
- **"Send Error"**: CAN transmission failed, check hardware

## Technical Specifications

- **CAN Baud Rate**: 250 kbps
- **ADC Resolution**: 12-bit (Internal), 16-bit (ADS1115)
- **Channel Mapping**: Ch0=Front-Left, Ch1=Rear-Left, Ch2=Front-Right, Ch3=Rear-Right
- **Side Grouping**: Left=Ch0+Ch1, Right=Ch2+Ch3
- **Least Count**: 1kg (handled by calibration)
- **Bandwidth**: ~3KB/sec (81% reduction from original)

## Support

For technical support or questions:
- Check this README first
- Review CAN protocol specification
- Verify hardware connections
- Test with known weights

---

**Version**: v0.7 Production  
**Last Updated**: January 2025  
**Status**: Production Ready - Clean Codebase