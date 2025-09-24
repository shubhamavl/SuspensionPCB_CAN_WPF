# SuspensionPCB CAN WPF

A WPF (.NET 8) desktop tool for monitoring and calibrating the LMV Suspension PCB over a USB–CAN adapter. Implements CAN Protocol v0.5 with variable multi‑point calibration workflow.

## Features
- Live CAN message log with filters (TX/RX, ID filter)
- Suspension and axle weight visualization (FL/FR/RL/RR, totals)
- Variable multi‑point calibration (2–20 points), auto/ manual spacing
- Calibration quality analysis display (accuracy, max error, grade, recommendation)
- USB–CAN (CH341/USB‑CAN‑A) serial framing with stable decode/encode

## Project Structure
- `MainWindow.xaml(.cs)`: Primary UI and high‑level flow
- `CANService.cs`: USB‑CAN serial framing, decode/encode, helpers for messages
- `CANMessage.cs`: Message and decoding helpers for UI
- `Calibration/CalibrationWindow.xaml(.cs)`: Calibration setup UI (point count, poly order, spacing, channels, max weight)
- `Calibration/WeightCalibrationPoint.xaml(.cs)`: Point capture UI and protocol integration

## Build
Prereqs: .NET SDK 8.0+, Windows.

Open `SuspensionPCB_CAN_WPF.sln` in Visual Studio 2022 (17.10+) or run:
```
dotnet restore
dotnet build -c Release
```

## Run
```
dotnet run --project SuspensionPCB_CAN_WPF.csproj
```

## USB–CAN Notes
- App auto‑selects the last available COM port
- Serial default: 2,000,000 baud, 8‑N‑1
- CAN IDs used (subset):
  - TX: `0x020, 0x022, 0x024, 0x025, 0x026, 0x027, 0x030, 0x031`
  - RX: `0x200, 0x201, 0x400, 0x401, 0x402`

## Protocol v0.5 Quick Reference
- Data requests: `0x030` (susp), `0x031` (axle)
- Calibration: `0x020` start, `0x022` set weight point, `0x024` complete
- Responses: `0x200/0x201` weights, `0x400` cal point, `0x401` quality, `0x402` errors

## Configuration
Optional `Suspension_Config.json` (placed next to exe):
```
{
  "AutoRequestInterval": 1000,
  "TransmissionRate": 2
}
```

## Development
- TargetFramework: `net8.0-windows`, `UseWPF=true`
- NuGet: `System.IO.Ports`, `System.Management`

## License
Proprietary – internal use. Update this section before public release.
