# Release Notes Guide

This guide explains how to add feature notes and changelog entries that will automatically appear in GitHub Releases.

## How It Works

1. **Edit CHANGELOG.md** - Add your changes to the `[Unreleased]` section
2. **Create Git Tag** - When ready to release, tag the version (e.g., `v1.1.0`)
3. **GitHub Actions** - Automatically extracts release notes from CHANGELOG.md and creates the release

## Step-by-Step Process

### Step 1: Add Features to CHANGELOG.md

Edit `CHANGELOG.md` and add your changes under the `[Unreleased]` section:

```markdown
## [Unreleased]

### Added
- New feature: Auto-calibration wizard
- Improved error messages for connection failures
- Export calibration data to CSV

### Changed
- Updated UI colors for better visibility
- Optimized data processing (30% faster)

### Fixed
- Fixed issue where calibration data wasn't saving
- Resolved memory leak in high-frequency mode
- Fixed connection timeout handling

### Security
- Updated dependencies to patch security vulnerabilities
```

### Step 2: When Ready to Release

1. **Move Unreleased to Version Section** - Update CHANGELOG.md:

```markdown
## [Unreleased]

### Added
- Placeholder for next version

## [1.1.0] - 2025-02-15

### Added
- New feature: Auto-calibration wizard
- Improved error messages for connection failures
- Export calibration data to CSV

### Changed
- Updated UI colors for better visibility
- Optimized data processing (30% faster)

### Fixed
- Fixed issue where calibration data wasn't saving
- Resolved memory leak in high-frequency mode
- Fixed connection timeout handling
```

2. **Commit and Push**:

```bash
git add CHANGELOG.md
git commit -m "Update CHANGELOG for v1.1.0"
git push origin master
```

3. **Create and Push Tag**:

```bash
git tag v1.1.0
git push origin v1.1.0
```

4. **GitHub Actions Automatically**:
   - Builds the application
   - Extracts release notes from CHANGELOG.md for version 1.1.0
   - Creates GitHub Release with your feature notes
   - Uploads ZIP file and SHA-256 hash

## CHANGELOG.md Format

Follow this format for best results:

```markdown
## [Version] - YYYY-MM-DD

### Added
- New features

### Changed
- Changes in existing functionality

### Deprecated
- Soon-to-be removed features

### Removed
- Removed features

### Fixed
- Bug fixes

### Security
- Security fixes
```

## Examples

### Example 1: Minor Update (v1.1.0)

```markdown
## [1.1.0] - 2025-02-15

### Added
- Auto-calibration wizard for easier setup
- Export calibration data to CSV format
- Keyboard shortcut for quick tare (Ctrl+T)

### Fixed
- Fixed calibration data not persisting after restart
- Resolved connection timeout issues with USB-CAN adapters
```

### Example 2: Bug Fix Release (v1.0.1)

```markdown
## [1.0.1] - 2025-01-20

### Fixed
- Critical: Fixed memory leak in 1kHz data mode
- Fixed application crash when disconnecting during data stream
- Resolved issue where tare values were lost on restart

### Changed
- Improved error messages for better user feedback
```

### Example 3: Major Update (v2.0.0)

```markdown
## [2.0.0] - 2025-03-01

### Added
- Multi-point polynomial calibration (up to 10 points)
- Real-time graphing with left/right balance visualization
- Cloud sync for calibration data
- Advanced diagnostics window

### Changed
- Complete UI redesign for better usability
- Improved performance (50% faster processing)
- Updated to .NET 8.0

### Removed
- Legacy calibration format (auto-migrated)
```

## What Appears in GitHub Release

When you create a release, GitHub will show:

1. **Your Release Notes** (from CHANGELOG.md)
   - All Added/Changed/Fixed entries
   - Formatted nicely with markdown

2. **Security Verification Section**
   - SHA-256 hash for download verification
   - Verification commands for Windows/Linux/Mac

## Tips

1. **Keep It Simple** - Users want to know what's new, not technical details
2. **Be Specific** - "Fixed calibration bug" is better than "Fixed bugs"
3. **Group Changes** - Use Added/Changed/Fixed categories
4. **Update Regularly** - Add to `[Unreleased]` as you develop features
5. **Date Format** - Use YYYY-MM-DD format (e.g., 2025-02-15)

## Quick Reference

```bash
# 1. Edit CHANGELOG.md - add features to [Unreleased]

# 2. When ready, move to version section and commit
git add CHANGELOG.md
git commit -m "Update CHANGELOG for v1.1.0"
git push origin master

# 3. Create and push tag
git tag v1.1.0
git push origin v1.1.0

# 4. GitHub Actions automatically creates release with your notes!
```

## Troubleshooting

**Q: Release notes not appearing?**
- Make sure CHANGELOG.md has a section matching your version (e.g., `## [1.1.0]`)
- Check that the version in CHANGELOG.md matches your tag (without the 'v' prefix)

**Q: Can I edit release notes after creation?**
- Yes! Go to GitHub Releases page and click "Edit" on the release
- You can manually edit the release notes there

**Q: What if I forget to update CHANGELOG.md?**
- The release will still be created, but without feature notes
- You can manually add notes by editing the release on GitHub

---

**Remember**: Keep CHANGELOG.md updated as you develop, and move entries to version sections when you're ready to release!

