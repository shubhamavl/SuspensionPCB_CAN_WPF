using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace SuspensionPCB_CAN_WPF
{
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
        /// Enqueue raw data from CAN thread - must be FAST!
        /// </summary>
        public void EnqueueRawData(byte side, ushort rawADC)
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
                RawADC = rawADC,
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
        /// Core processing - optimized for speed
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
                    processed.CalibratedWeight = _leftCalibration.RawToKg(raw.RawADC);
                    
                    // Apply tare (mode-specific) - uses _leftADCMode which should match the calibration mode
                    // _leftADCMode is set via SetADCMode() when stream starts or ADC mode changes
                    processed.TaredWeight = _tareManager?.ApplyTare(processed.CalibratedWeight, true, _leftADCMode) ?? processed.CalibratedWeight;
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
                    processed.CalibratedWeight = _rightCalibration.RawToKg(raw.RawADC);
                    
                    // Apply tare (mode-specific) - uses _rightADCMode which should match the calibration mode
                    // _rightADCMode is set via SetADCMode() when stream starts or ADC mode changes
                    processed.TaredWeight = _tareManager?.ApplyTare(processed.CalibratedWeight, false, _rightADCMode) ?? processed.CalibratedWeight;
                }
                
                _latestRight = processed;
            }
        }
        
        public void Dispose()
        {
            Stop();
            _cancellationSource?.Dispose();
        }
    }
    
    /// <summary>
    /// Raw weight data from CAN
    /// </summary>
    public class RawWeightData
    {
        public byte Side { get; set; } // 0=Left, 1=Right
        public ushort RawADC { get; set; }
        public DateTime Timestamp { get; set; }
    }
    
    /// <summary>
    /// Processed weight data with calibration and tare applied
    /// </summary>
    public class ProcessedWeightData
    {
        public ushort RawADC { get; set; }
        public double CalibratedWeight { get; set; }
        public double TaredWeight { get; set; }
        public DateTime Timestamp { get; set; }
    }
}

