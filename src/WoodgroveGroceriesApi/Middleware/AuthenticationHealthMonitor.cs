// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using System.Collections.Concurrent;

namespace WoodgroveGroceriesApi.Middleware
{
    /// <summary>
    /// Monitors and tracks authentication health metrics, including success rates and latency.
    /// </summary>
    public class AuthenticationHealthMonitor
    {
        private readonly TelemetryClient _telemetryClient;
        private readonly ConcurrentDictionary<string, HealthMetric> _metrics;
        private readonly ILogger<AuthenticationHealthMonitor> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="AuthenticationHealthMonitor"/> class.
        /// </summary>
        /// <param name="telemetryClient">The Application Insights telemetry client.</param>
        /// <param name="logger">The logger instance.</param>
        public AuthenticationHealthMonitor(
            TelemetryClient telemetryClient, 
            ILogger<AuthenticationHealthMonitor> logger)
        {
            _telemetryClient = telemetryClient;
            _logger = logger;
            _metrics = new ConcurrentDictionary<string, HealthMetric>();
        }

        /// <summary>
        /// Tracks authentication result metrics including success/failure rate and latency.
        /// </summary>
        /// <param name="provider">The authentication provider or mechanism name.</param>
        /// <param name="success">Whether the authentication was successful.</param>
        /// <param name="elapsedMilliseconds">The time taken for the authentication process in milliseconds.</param>
        public void TrackAuthenticationResult(string provider, bool success, long elapsedMilliseconds)
        {
            var metric = _metrics.GetOrAdd(provider, _ => new HealthMetric(provider));
            
            if (success)
            {
                metric.RecordSuccess(elapsedMilliseconds);
                _telemetryClient.TrackMetric($"Authentication.{provider}.Success", 1);
                _telemetryClient.TrackMetric($"Authentication.{provider}.Latency", elapsedMilliseconds);
            }
            else
            {
                metric.RecordFailure(elapsedMilliseconds);
                _telemetryClient.TrackMetric($"Authentication.{provider}.Failure", 1);
            }
            
            // Report health status periodically
            if (metric.TotalRequests % 100 == 0)
            {
                ReportHealthStatus(metric);
            }
        }

        /// <summary>
        /// Tracks JWT token validation metrics including success/failure rate and latency.
        /// </summary>
        /// <param name="success">Whether the token validation was successful.</param>
        /// <param name="elapsedMilliseconds">The time taken for the token validation in milliseconds.</param>
        public void TrackTokenValidation(bool success, long elapsedMilliseconds)
        {
            TrackAuthenticationResult("TokenValidation", success, elapsedMilliseconds);
        }

        /// <summary>
        /// Reports the health status of the authentication process to logs and metrics.
        /// </summary>
        /// <param name="metric">The health metric to report.</param>
        public void ReportHealthStatus(HealthMetric metric)
        {
            var successRate = metric.SuccessRate;
            var avgLatency = metric.AverageLatency;
            
            _telemetryClient.TrackMetric($"Authentication.{metric.Provider}.SuccessRate", successRate);
            _telemetryClient.TrackMetric($"Authentication.{metric.Provider}.AvgLatency", avgLatency);
            
            // Log health status based on success rate thresholds
            if (successRate < 0.8)
            {
                _logger.LogCritical(
                    "Critical authentication health for {Provider}: {SuccessRate:P2} success rate, {Latency}ms avg latency",
                    metric.Provider, successRate, avgLatency);
            }
            else if (successRate < 0.95)
            {
                _logger.LogWarning(
                    "Degraded authentication health for {Provider}: {SuccessRate:P2} success rate, {Latency}ms avg latency",
                    metric.Provider, successRate, avgLatency);
            }
            else
            {
                _logger.LogInformation(
                    "Healthy authentication for {Provider}: {SuccessRate:P2} success rate, {Latency}ms avg latency",
                    metric.Provider, successRate, avgLatency);
            }
        }

        /// <summary>
        /// Class to track health metrics for a specific provider.
        /// </summary>
        public class HealthMetric
        {
            public string Provider { get; }
            private long _totalRequests;
            private long _successCount;
            private long _totalLatency;
            
            public long TotalRequests => _totalRequests;
            public long SuccessCount => _successCount;
            public long TotalLatency => _totalLatency;
            
            public double SuccessRate => TotalRequests > 0 ? (double)SuccessCount / TotalRequests : 0;
            public double AverageLatency => TotalRequests > 0 ? (double)TotalLatency / TotalRequests : 0;

            /// <summary>
            /// Initializes a new instance of the <see cref="HealthMetric"/> class.
            /// </summary>
            /// <param name="provider">The authentication provider or mechanism name.</param>
            public HealthMetric(string provider)
            {
                Provider = provider;
                _totalRequests = 0;
                _successCount = 0;
                _totalLatency = 0;
            }

            /// <summary>
            /// Records a successful authentication attempt and its latency.
            /// </summary>
            /// <param name="latencyMs">The time taken for the authentication process in milliseconds.</param>
            public void RecordSuccess(long latencyMs)
            {
                Interlocked.Increment(ref _totalRequests);
                Interlocked.Increment(ref _successCount);
                Interlocked.Add(ref _totalLatency, latencyMs);
            }

            /// <summary>
            /// Records a failed authentication attempt and its latency.
            /// </summary>
            /// <param name="latencyMs">The time taken for the authentication process in milliseconds.</param>
            public void RecordFailure(long latencyMs)
            {
                Interlocked.Increment(ref _totalRequests);
                Interlocked.Add(ref _totalLatency, latencyMs);
            }
        }
    }
}