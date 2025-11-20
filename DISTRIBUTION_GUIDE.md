# Distribution Guide for End Users

This guide explains how to create a distributable package and how end users can install/use the application.

## 📦 For Developers: Creating the Distribution Package

### Step 1: Build the Portable Version

**Option A: Using the Build Script (Easiest)**
1. Open Command Prompt or PowerShell in the project folder
2. Run: `build-portable.bat`
3. Wait for the build to complete

**Option B: Using Command Line**
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true
```

### Step 2: Locate the Built Files

After building, the files will be in:
```
bin\Release\net8.0-windows\win-x64\publish\
```

### Step 3: Create Distribution Package

1. **Open the publish folder**: `bin\Release\net8.0-windows\win-x64\publish\`

2. **Select all files** in the publish folder (Ctrl+A)

3. **Create a ZIP file**:
   - Right-click → Send to → Compressed (zipped) folder
   - Or use 7-Zip/WinRAR to create a ZIP
   - Name it: `SuspensionPCB_CAN_WPF_Portable_v1.0.zip` (or your version)

4. **Recommended: Include Documentation**
   - Add `FOR_END_USERS.txt` to the ZIP (MUST READ - explains no .NET needed)
   - Add `README_FOR_USERS.txt` to the ZIP (simple instructions)
   - Add `USER_GUIDE.md` to the ZIP (detailed user manual)
   - Add `QUICK_START.txt` to the ZIP (quick reference)
   - Add `INSTALL_INSTRUCTIONS.txt` to the ZIP (simple steps)

### Step 4: Test the Package

Before distributing:
1. Extract the ZIP to a test folder
2. Run `SuspensionPCB_CAN_WPF.exe`
3. Verify it works correctly
4. Check that it creates `Data/` and `Logs/` folders

### Step 5: Distribute

- Upload to file sharing service (Google Drive, Dropbox, OneDrive, etc.)
- Email the ZIP file (if small enough)
- Share via USB drive
- Host on your website/server

---

## 👥 For End Users: Installation Instructions

### What You'll Receive

You'll receive a ZIP file named something like:
- `SuspensionPCB_CAN_WPF_Portable_v1.0.zip`

**IMPORTANT**: The .NET 8.0 runtime is **ALREADY INCLUDED** in this package!
- ❌ You do NOT need to download .NET 8.0 separately
- ❌ You do NOT need to install .NET runtime
- ✅ Everything is included - just extract and run!

### Installation Steps (No Installation Required!)

#### Step 1: Download the ZIP File

Download the ZIP file to your computer (Desktop, Downloads folder, or any location you prefer).

#### Step 2: Extract the ZIP File

1. **Right-click** on the ZIP file
2. Select **"Extract All..."** or **"Extract Here"**
3. Choose where to extract (Desktop is fine, or create a folder like `C:\SuspensionSystem\`)
4. Click **Extract**

You'll see a folder with the application files.

#### Step 3: Run the Application

1. **Open the extracted folder**
2. **Double-click** `SuspensionPCB_CAN_WPF.exe`
3. The application will start immediately - **no installation needed!**

#### Step 4: First Run

On the first run, the application will automatically create:
- `Data/` folder - for calibration and data files
- `Logs/` folder - for log files
- `settings.json` - for your settings

This is normal and happens automatically.

### Where to Place the Application

You can place the application **anywhere**:
- ✅ Desktop
- ✅ Documents folder
- ✅ USB drive (fully portable!)
- ✅ Network drive
- ✅ Any folder you prefer

**Important**: The application stores all its data in the same folder, so:
- Keep the entire folder together
- Don't delete the `Data/` or `Logs/` folders
- If you move the application, move the entire folder

### Using from USB Drive

1. Extract the ZIP to your USB drive
2. Run `SuspensionPCB_CAN_WPF.exe` directly from the USB
3. All data will be stored on the USB drive
4. Works on any Windows computer (no installation needed)

### System Requirements

- **Windows 10 or Windows 11** (64-bit)
- **No other software needed** - everything is included!
- **.NET 8.0 is INCLUDED** - you don't need to install it separately!

**Note**: The ZIP file is large (~70-100 MB) because it includes the .NET runtime. This is normal and means you don't need to download anything else!

### Troubleshooting

#### "Windows protected your PC" Message

If Windows shows a security warning:
1. Click **"More info"**
2. Click **"Run anyway"**
3. This happens because the file isn't digitally signed (normal for custom software)

#### "Application won't start"

- Make sure you're using **Windows 10 or 11** (64-bit)
- Try running as Administrator (right-click → Run as administrator)
- Check that all files were extracted from the ZIP

#### "Can't save settings"

- Make sure you have write permissions to the folder
- If on USB, make sure it's not write-protected
- Try running as Administrator

### Updating the Application

To update to a new version:
1. **Backup your data** (copy the `Data/` folder)
2. **Download the new version** ZIP file
3. **Extract to a new folder** (or replace old files)
4. **Copy your `Data/` folder** to the new location
5. **Run the new version**

Your calibration and settings will be preserved!

### Uninstalling

To remove the application:
1. **Delete the entire folder** where you extracted it
2. That's it! No uninstaller needed.

**Note**: This will delete your calibration data and logs. Back them up first if you want to keep them.

---

## 📋 Quick Reference

### For Developers
```
1. Build: build-portable.bat
2. Find files: bin\Release\net8.0-windows\win-x64\publish\
3. Create ZIP with all files
4. Distribute ZIP
```

### For End Users
```
1. Download ZIP
2. Extract ZIP
3. Run .exe file
4. Done! (No installation needed)
```

---

## 📧 Distribution Checklist

Before sending to end users, make sure:

- [ ] Application builds successfully
- [ ] Tested on a clean Windows machine
- [ ] ZIP file contains all necessary files
- [ ] README or user guide included (optional but helpful)
- [ ] Version number in ZIP filename
- [ ] Instructions provided to end users

---

**Version**: 1.0.0  
**Last Updated**: January 2025

