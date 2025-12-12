# Status and Firmware Version Management

**Last Updated:** 10 December 2025

## Overview

The WPF application manages both **System Status** and **Firmware Version** information from the STM32 firmware. Both follow the same pattern for consistency and maintainability.

## System Status Management

### CAN Messages
- **Request**: `0x032` (empty message from PC3)
- **Response**: `0x300` (3 bytes: system_status, error_flags, adc_mode)

### Implementation Flow

1. **Event Subscription** (`MainWindow.xaml.cs` line ~1554):
   ```csharp
   _canService.SystemStatusReceived += HandleSystemStatus;
   ```

2. **Request Method** (`RequestStatusBtn_Click`):
   ```csharp
   _canService.RequestSystemStatus();
   ```

3. **Event Handler** (`HandleSystemStatus`):
   - Updates ADC mode indicators
   - Reloads calibrations if ADC mode changed
   - Updates data logger
   - Updates settings persistence
   - Adds to status history
   - Updates UI via `UpdateSystemStatusUI()`

4. **UI Update** (`UpdateSystemStatusUI`):
   - Updates `SystemStatusIndicator` (color-coded ellipse)
   - Updates `SystemStatusText` ("OK", "Warning", "Error", "Critical")
   - Updates `ErrorCountTxt` (error flags display)
   - Updates `LastUpdateTxt` (timestamp)
   - Updates `DataRateTxt` (calculated update rate)

### UI Elements (MainWindow.xaml)
- **SystemStatusIndicator**: Color-coded ellipse (Green/Yellow/Red)
- **SystemStatusText**: Status text display
- **ErrorCountTxt**: Error flags display
- **LastUpdateTxt**: Last update timestamp
- **DataRateTxt**: Calculated update rate
- **RequestStatusBtn**: Button to request status

## Firmware Version Management

### CAN Messages
- **Request**: `0x033` (empty message from PC3)
- **Response**: `0x301` (4 bytes: major, minor, patch, build)

### Implementation Flow

1. **Event Subscription** (`MainWindow.xaml.cs` line ~1555):
   ```csharp
   _canService.FirmwareVersionReceived += HandleFirmwareVersion;
   ```

2. **Request Method** (`RequestStatusBtn_Click`):
   ```csharp
   _canService.RequestFirmwareVersion();  // Called alongside RequestSystemStatus()
   ```

3. **Event Handler** (`HandleFirmwareVersion`):
   - Logs version information
   - Updates UI via `UpdateFirmwareVersionUI()`

4. **UI Update** (`UpdateFirmwareVersionUI`):
   - Updates `FirmwareVersionText` (e.g., "FW: 3.2.0")
   - Sets tooltip with full version and timestamp

### UI Elements (MainWindow.xaml)
- **FirmwareVersionText**: TextBlock displaying firmware version
  - Text: `FW: {Major}.{Minor}.{Patch}` (e.g., "FW: 3.2.0")
  - Tooltip: Full version with timestamp

## Combined Request Pattern

The **Status** button (`RequestStatusBtn`) requests both status and version simultaneously:

```csharp
bool statusSuccess = _canService.RequestSystemStatus();
bool versionSuccess = _canService.RequestFirmwareVersion();
```

This ensures both pieces of information are updated together when the user clicks the Status button.

## UI Layout

The System Status panel displays:
```
┌─────────────────────────┐
│   SYSTEM STATUS          │
├─────────────────────────┤
│ Status: [●] OK          │
│ Rate: 1.00 Hz          │
│ FW: 3.2.0              │  ← Firmware Version
│ Updated: 14:30:25      │
│ Errors: None           │
│ [Status] [History]     │
└─────────────────────────┘
```

## Event Args Classes

### SystemStatusEventArgs
```csharp
public class SystemStatusEventArgs : EventArgs
{
    public byte SystemStatus { get; set; }  // 0=OK, 1=Warning, 2=Error
    public byte ErrorFlags { get; set; }
    public byte ADCMode { get; set; }       // 0=Internal, 1=ADS1115
    public DateTime Timestamp { get; set; }
}
```

### FirmwareVersionEventArgs
```csharp
public class FirmwareVersionEventArgs : EventArgs
{
    public byte Major { get; set; }
    public byte Minor { get; set; }
    public byte Patch { get; set; }
    public byte Build { get; set; }
    public DateTime Timestamp { get; set; }
    
    public string VersionString => $"{Major}.{Minor}.{Patch}";
    public string VersionStringFull => $"{Major}.{Minor}.{Patch}.{Build}";
}
```

## Status History

System status updates are also added to the status history manager:
```csharp
_statusHistoryManager.AddStatusEntry(e.SystemStatus, e.ErrorFlags, e.ADCMode);
```

This allows users to view historical status information via the **History** button.

## Error Handling

Both handlers include try-catch blocks and error logging:
- Handler errors are logged but don't crash the application
- UI update errors are caught and logged separately
- Failed requests are indicated in the inline status message

## Consistency Check

✅ **Both status and version follow the same pattern:**
- Event subscription in initialization
- Request method in button click handler
- Event handler that processes data
- UI update method that updates display elements
- Error handling and logging

✅ **Both are requested together** when the Status button is clicked

✅ **Both display in the same System Status panel** for easy visibility

## Testing

To verify both systems work correctly:

1. **Connect to STM32** via CAN
2. **Click "Status" button**
3. **Verify**:
   - System status indicator updates (color changes)
   - System status text updates ("OK", "Warning", etc.)
   - Firmware version displays (e.g., "FW: 3.2.0")
   - Last update timestamp updates
   - Error count displays correctly

4. **Check logs** for:
   - "Requested system status and firmware version from STM32"
   - "Firmware version received: 3.2.0.0"

## Files Modified

- `Views/MainWindow.xaml.cs`:
  - Added `HandleFirmwareVersion()` handler
  - Added `UpdateFirmwareVersionUI()` method
  - Updated `RequestStatusBtn_Click()` to request both
  - Added event subscription in initialization

- `Views/MainWindow.xaml`:
  - Added `FirmwareVersionText` TextBlock in System Status panel

- `Services/CANService.cs`:
  - Added `RequestFirmwareVersion()` method
  - Added `FirmwareVersionReceived` event
  - Added `FirmwareVersionEventArgs` class
  - Added version response handler

- `Models/CANMessage.cs`:
  - Added version message decoding
  - Added version message names

- `Adapters/UsbSerialCanAdapter.cs`:
  - Added version message filtering

