using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SuspensionPCB_CAN_WPF.Models;
using SuspensionPCB_CAN_WPF.Core;

namespace SuspensionPCB_CAN_WPF.Services
{
    /// <summary>
    /// Filter type enumeration
    /// </summary>
    public enum FilterType
    {
        None,
        EMA,
        SMA
    }

    /// <summary>
    /// High-performance weight data processor
    /// Runs on dedicated thread to handle 1kHz data rate
    /// </summary>
    public class WeightProcessor : IDisposable
    {
        // Input queue: Raw ADC data from CAN thread
        private readonly ConcurrentQueue<RawWeightData> _rawDataQueue = new();
        
        // Output: Latest processed data (lock-free)
        private volatile ProcessedWeightData _latestLeft = new();
        private volatile ProcessedWeightData _latestRight = new();
        
        // Calibration references (immutable after set)
        private LinearCalibration? _leftCalibration;
        private LinearCalibration? _rightCalibration;
        private TareManager? _tareManager;
        
        // ADC mode tracking (0=Internal, 1=ADS1115)
        private byte _leftADCMode = 0;
        private byte _rightADCMode = 0;
        
        // Thread control
        private Task? _processingTask;
        private CancellationTokenSource? _cancellationSource;
        private volatile bool _isRunning = false;
        
        // Performance tracking
        private long _processedCount = 0;
        private long _droppedCount = 0;
        
        // ===== WEIGHT FILTERING (Configurable) =====
        // Filter configuration
        private FilterType _filterType = FilterType.EMA;
        private double _filterAlpha = 0.15;  // EMA alpha (0.0-1.0)
        private int _filterWindowSize = 10;  // SMA window size
        private bool _filterEnabled = true;  // Enable/disable filtering
        
        // EMA filtered weight values (per side)
        private double _leftFilteredCalibrated = 0;
        private double _leftFilteredTared = 0;
        private double _rightFilteredCalibrated = 0;
        private double _rightFilteredTared = 0;
        
        // Track if EMA filters are initialized (first sample)
        private bool _leftFilterInitialized = false;
        private bool _rightFilterInitialized = false;
        
        // SMA buffers (per side, per type)
        private readonly Queue<double> _leftSmaCalibrated = new Queue<double>();
        private readonly Queue<double> _leftSmaTared = new Queue<double>();
        private readonly Queue<double> _rightSmaCalibrated = new Queue<double>();
        private readonly Queue<double> _rightSmaTared = new Queue<double>();
        
        public ProcessedWeightData LatestLeft => _latestLeft;
        public ProcessedWeightData LatestRight => _latestRight;
        public long ProcessedCount => _processedCount;
        public long DroppedCount => _droppedCount;
        public bool IsRunning => _isRunning;
        
        /// <summary>
        /// Start the processing thread
        /// </summary>
        public void Start()
        {
            if (_isRunning) return;
            
            _isRunning = true;
            _cancellationSource = new CancellationTokenSource();
            _processingTask = Task.Run(() => ProcessingLoop(_cancellationSource.Token));
            
            ProductionLogger.Instance.LogInfo("WeightProcessor started", "WeightProcessor");
        }
        
        /// <summary>
        /// Stop the processing thread
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;
            
            _isRunning = false;
            _cancellationSource?.Cancel();
            _processingTask?.Wait(1000);
            
            ProductionLogger.Instance.LogInfo($"WeightProcessor stopped. Processed: {_processedCount}, Dropped: {_droppedCount}", "WeightProcessor");
        }
        
        /// <summary>
        /// Set calibration references
        /// </summary>
        public void SetCalibration(LinearCalibration? left, LinearCalibration? right)
        {
            _leftCalibration = left;
            _rightCalibration = right;
            
            ProductionLogger.Instance.LogInfo($"Calibration set - Left: {left?.IsValid}, Right: {right?.IsValid}", "WeightProcessor");
        }
        
        /// <summary>
        /// Set ADC mode for a side (0=Internal, 1=ADS1115)
        /// </summary>
        public void SetADCMode(bool isLeft, byte adcMode)
        {
            if (isLeft)
                _leftADCMode = adcMode;
            else
                _rightADCMode = adcMode;
        }
        
        /// <summary>
        /// Set tare manager reference
        /// </summary>
        public void SetTareManager(TareManager tareManager)
        {
            _tareManager = tareManager;
            ProductionLogger.Instance.LogInfo("TareManager set", "WeightProcessor");
        }
        
        /// <summary>
        /// Configure filter settings
        /// </summary>
        public void ConfigureFilter(FilterType type, double alpha, int windowSize, bool enabled)
        {
            _filterType = type;
            _filterAlpha = alpha;
            _filterWindowSize = windowSize;
            _filterEnabled = enabled;
            
            // Clear SMA buffers when settings change
            _leftSmaCalibrated.Clear();
            _leftSmaTared.Clear();
            _rightSmaCalibrated.Clear();
            _rightSmaTared.Clear();
            
            // Reset EMA filters when changing filter type
            if (type != FilterType.EMA)
            {
                _leftFilterInitialized = false;
                _rightFilterInitialized = false;
            }
            
            ProductionLogger.Instance.LogInfo($"Filter configured: Type={type}, Alpha={alpha}, Window={windowSize}, Enabled={enabled}", "WeightProcessor");
        }
        
        /// <summary>
        /// Enqueue raw data from CAN thread - must be FAST!
        /// </summary>
        /// <summary>
        /// Enqueue raw ADC data for processing
        /// Supports both Internal ADC (unsigned 0-8190) and ADS1115 (signed -65536 to +65534)
        /// </summary>
        public void EnqueueRawData(byte side, int rawADC)
        {
            const int MAX_QUEUE_SIZE = 100; // Prevent memory leak
            
            if (_rawDataQueue.Count > MAX_QUEUE_SIZE)
            {
                Interlocked.Increment(ref _droppedCount);
                return; // Drop oldest data
            }
            
            _rawDataQueue.Enqueue(new RawWeightData 
            { 
                Side = side, 
                RawADC = rawADC,  // Can be signed (ADS1115) or unsigned (Internal)
                Timestamp = DateTime.Now 
            });
        }
        
        /// <summary>
        /// Processing thread - runs continuously
        /// </summary>
        private void ProcessingLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (_rawDataQueue.TryDequeue(out var rawData))
                {
                    ProcessRawData(rawData);
                    Interlocked.Increment(ref _processedCount);
                }
                else
                {
                    // No data available - sleep briefly
                    Thread.Sleep(1);
                }
            }
        }
        
        /// <summary>
        /// Core processing - optimized for speed with configurable filtering
        /// </summary>
        private void ProcessRawData(RawWeightData raw)
        {
            if (raw.Side == 0) // Left side
            {
                var processed = new ProcessedWeightData
                {
                    RawADC = raw.RawADC,
                    Timestamp = raw.Timestamp
                };
                
                // Apply calibration (fast floating-point math)
                if (_leftCalibration?.IsValid == true)
                {
                    double calibratedWeight = _leftCalibration.RawToKg(raw.RawADC);
                    
                    // Apply filtering if enabled
                    if (_filterEnabled)
                    {
                        processed.CalibratedWeight = ApplyFilter(calibratedWeight, true, true);
                    }
                    else
                    {
                        processed.CalibratedWeight = calibratedWeight;
                    }
                    
                    // Apply tare (mode-specific) - uses _leftADCMode which should match the calibration mode
                    double taredWeight = _tareManager?.ApplyTare(processed.CalibratedWeight, true, _leftADCMode) ?? processed.CalibratedWeight;
                    
                    // Apply filtering to tared weight if enabled
                    if (_filterEnabled)
                    {
                        processed.TaredWeight = ApplyFilter(taredWeight, true, false);
                    }
                    else
                    {
                        processed.TaredWeight = taredWeight;
                    }
                }
                
                _latestLeft = processed; // Atomic write
            }
            else // Right side
            {
                var processed = new ProcessedWeightData
                {
                    RawADC = raw.RawADC,
                    Timestamp = raw.Timestamp
                };
                
                if (_rightCalibration?.IsValid == true)
                {
                    double calibratedWeight = _rightCalibration.RawToKg(raw.RawADC);
                    
                    // Apply filtering if enabled
                    if (_filterEnabled)
                    {
                        processed.CalibratedWeight = ApplyFilter(calibratedWeight, false, true);
                    }
                    else
                    {
                        processed.CalibratedWeight = calibratedWeight;
                    }
                    
                    // Apply tare (mode-specific)
                    double taredWeight = _tareManager?.ApplyTare(processed.CalibratedWeight, false, _rightADCMode) ?? processed.CalibratedWeight;
                    
                    // Apply filtering to tared weight if enabled
                    if (_filterEnabled)
                    {
                        processed.TaredWeight = ApplyFilter(taredWeight, false, false);
                    }
                    else
                    {
                        processed.TaredWeight = taredWeight;
                    }
                }
                
                _latestRight = processed;
            }
        }
        
        /// <summary>
        /// Apply filter based on configured filter type
        /// </summary>
        private double ApplyFilter(double value, bool isLeft, bool isCalibrated)
        {
            switch (_filterType)
            {
                case FilterType.EMA:
                    return ApplyEMA(value, isLeft, isCalibrated);
                case FilterType.SMA:
                    return ApplySMA(value, isLeft, isCalibrated);
                case FilterType.None:
                default:
                    return value;
            }
        }
        
        /// <summary>
        /// Apply Exponential Moving Average filter
        /// </summary>
        private double ApplyEMA(double value, bool isLeft, bool isCalibrated)
        {
            if (isLeft)
            {
                if (isCalibrated)
                {
                    if (!_leftFilterInitialized)
                    {
                        _leftFilteredCalibrated = value;
                        _leftFilterInitialized = true;
                        return value;
                    }
                    _leftFilteredCalibrated = _filterAlpha * value + (1 - _filterAlpha) * _leftFilteredCalibrated;
                    return _leftFilteredCalibrated;
                }
                else
                {
                    if (!_leftFilterInitialized)
                    {
                        _leftFilteredTared = value;
                        return value;
                    }
                    _leftFilteredTared = _filterAlpha * value + (1 - _filterAlpha) * _leftFilteredTared;
                    return _leftFilteredTared;
                }
            }
            else // Right side
            {
                if (isCalibrated)
                {
                    if (!_rightFilterInitialized)
                    {
                        _rightFilteredCalibrated = value;
                        _rightFilterInitialized = true;
                        return value;
                    }
                    _rightFilteredCalibrated = _filterAlpha * value + (1 - _filterAlpha) * _rightFilteredCalibrated;
                    return _rightFilteredCalibrated;
                }
                else
                {
                    if (!_rightFilterInitialized)
                    {
                        _rightFilteredTared = value;
                        return value;
                    }
                    _rightFilteredTared = _filterAlpha * value + (1 - _filterAlpha) * _rightFilteredTared;
                    return _rightFilteredTared;
                }
            }
        }
        
        /// <summary>
        /// Apply Simple Moving Average filter
        /// </summary>
        private double ApplySMA(double value, bool isLeft, bool isCalibrated)
        {
            Queue<double> buffer;
            
            if (isLeft)
            {
                buffer = isCalibrated ? _leftSmaCalibrated : _leftSmaTared;
            }
            else
            {
                buffer = isCalibrated ? _rightSmaCalibrated : _rightSmaTared;
            }
            
            buffer.Enqueue(value);
            if (buffer.Count > _filterWindowSize)
            {
                buffer.Dequeue();
            }
            
            // Return average of buffer
            return buffer.Count > 0 ? buffer.Average() : value;
        }
        
        /// <summary>
        /// Reset filters (call when tare changes or calibration changes)
        /// </summary>
        public void ResetFilters()
        {
            _leftFilterInitialized = false;
            _rightFilterInitialized = false;
            _leftFilteredCalibrated = 0;
            _leftFilteredTared = 0;
            _rightFilteredCalibrated = 0;
            _rightFilteredTared = 0;
            
            // Clear SMA buffers
            _leftSmaCalibrated.Clear();
            _leftSmaTared.Clear();
            _rightSmaCalibrated.Clear();
            _rightSmaTared.Clear();
            
            ProductionLogger.Instance.LogInfo("Weight filters reset", "WeightProcessor");
        }
        
        public void Dispose()
        {
            Stop();
            _cancellationSource?.Dispose();
        }
    }
}

