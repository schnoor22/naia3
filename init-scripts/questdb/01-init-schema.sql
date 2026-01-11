-- NAIA v3 QuestDB Time-Series Schema
-- 
-- This creates the tables for storing high-volume time-series data.
-- QuestDB is optimized for time-series workloads with columnar storage,
-- partitioning, and ultra-fast ingestion via ILP (InfluxDB Line Protocol).

-- ===========================================================================
-- POINT_DATA - Raw time-series data
-- ===========================================================================
CREATE TABLE IF NOT EXISTS point_data (
    timestamp TIMESTAMP,
    point_id LONG,
    value DOUBLE,
    quality INT
) TIMESTAMP(timestamp) PARTITION BY DAY WAL;

-- Performance tuning for high-volume writes
ALTER TABLE point_data SET PARAM maxUncommittedRows = 250000;
ALTER TABLE point_data SET PARAM o3MaxLag = 3600s;  -- 1 hour out-of-order tolerance (for lagging data)

-- ===========================================================================
-- POINT_AGGREGATES - Hourly rollups
-- ===========================================================================
CREATE TABLE IF NOT EXISTS point_aggregates (
    timestamp TIMESTAMP,
    point_id LONG,
    min_value DOUBLE,
    max_value DOUBLE,
    avg_value DOUBLE,
    sample_count LONG
) TIMESTAMP(timestamp) PARTITION BY MONTH WAL;

-- ===========================================================================
-- POINT_DAILY_STATS - Daily statistics for pattern analysis
-- ===========================================================================
CREATE TABLE IF NOT EXISTS point_daily_stats (
    timestamp TIMESTAMP,
    point_id LONG,
    min_value DOUBLE,
    max_value DOUBLE,
    avg_value DOUBLE,
    std_dev DOUBLE,
    sample_count LONG,
    update_rate DOUBLE
) TIMESTAMP(timestamp) PARTITION BY MONTH WAL;
