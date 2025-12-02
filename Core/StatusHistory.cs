using System;
using System.Collections.Generic;
using System.Linq;
using SuspensionPCB_CAN_WPF.Models;

namespace SuspensionPCB_CAN_WPF.Core
{
    /// <summary>
    /// System status history manager
    /// </summary>
    public class StatusHistoryManager
    {
        private readonly List<StatusHistoryEntry> _statusHistory;
        private readonly int _maxEntries;
        private readonly object _lock = new object();

        public StatusHistoryManager(int maxEntries = 100)
        {
            _maxEntries = maxEntries;
            _statusHistory = new List<StatusHistoryEntry>();
        }

        /// <summary>
        /// Add a new status entry
        /// </summary>
        /// <param name="systemStatus">System status (0=OK, 1=Warning, 2=Error)</param>
        /// <param name="errorFlags">Error flags</param>
        /// <param name="adcMode">ADC mode (0=Internal, 1=ADS1115)</param>
        public void AddStatusEntry(byte systemStatus, byte errorFlags, byte adcMode)
        {
            lock (_lock)
            {
                var entry = new StatusHistoryEntry
                {
                    Timestamp = DateTime.Now,
                    SystemStatus = systemStatus,
                    ErrorFlags = errorFlags,
                    ADCMode = adcMode
                };

                _statusHistory.Insert(0, entry); // Add to beginning (most recent first)

                // Keep only the most recent entries
                if (_statusHistory.Count > _maxEntries)
                {
                    _statusHistory.RemoveRange(_maxEntries, _statusHistory.Count - _maxEntries);
                }
            }
        }

        /// <summary>
        /// Get all status history entries
        /// </summary>
        /// <returns>List of status entries (most recent first)</returns>
        public List<StatusHistoryEntry> GetAllEntries()
        {
            lock (_lock)
            {
                return new List<StatusHistoryEntry>(_statusHistory);
            }
        }

        /// <summary>
        /// Get recent status entries
        /// </summary>
        /// <param name="count">Number of recent entries to return</param>
        /// <returns>List of recent status entries</returns>
        public List<StatusHistoryEntry> GetRecentEntries(int count = 10)
        {
            lock (_lock)
            {
                return _statusHistory.Take(count).ToList();
            }
        }

        /// <summary>
        /// Get status entries within a time range
        /// </summary>
        /// <param name="startTime">Start time</param>
        /// <param name="endTime">End time</param>
        /// <returns>List of status entries within the time range</returns>
        public List<StatusHistoryEntry> GetEntriesInRange(DateTime startTime, DateTime endTime)
        {
            lock (_lock)
            {
                return _statusHistory.Where(entry => entry.Timestamp >= startTime && entry.Timestamp <= endTime).ToList();
            }
        }

        /// <summary>
        /// Get the most recent status entry
        /// </summary>
        /// <returns>Most recent status entry, or null if none</returns>
        public StatusHistoryEntry? GetLatestEntry()
        {
            lock (_lock)
            {
                return _statusHistory.FirstOrDefault();
            }
        }

        /// <summary>
        /// Clear all status history
        /// </summary>
        public void ClearHistory()
        {
            lock (_lock)
            {
                _statusHistory.Clear();
            }
        }

        /// <summary>
        /// Get status statistics
        /// </summary>
        /// <returns>Status statistics</returns>
        public (int totalEntries, int okCount, int warningCount, int errorCount, DateTime? firstEntry, DateTime? lastEntry) GetStatistics()
        {
            lock (_lock)
            {
                if (_statusHistory.Count == 0)
                    return (0, 0, 0, 0, null, null);

                int okCount = _statusHistory.Count(e => e.SystemStatus == 0);
                int warningCount = _statusHistory.Count(e => e.SystemStatus == 1);
                int errorCount = _statusHistory.Count(e => e.SystemStatus >= 2);

                return (_statusHistory.Count, okCount, warningCount, errorCount, 
                       _statusHistory.Last().Timestamp, _statusHistory.First().Timestamp);
            }
        }
    }
}
