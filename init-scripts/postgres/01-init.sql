-- PostgreSQL initialization script for NAIA
-- This runs automatically on first container start

-- Enable useful extensions
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "pg_trgm";  -- For fast LIKE searches

-- Create schema (tables created by EF Core migrations, but we can set up roles)

-- Grant permissions
GRANT ALL PRIVILEGES ON DATABASE naia TO naia;

-- Create read-only user for reporting/dashboards
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'naia_readonly') THEN
        CREATE ROLE naia_readonly WITH LOGIN PASSWORD 'naia_readonly_password';
    END IF;
END
$$;

GRANT CONNECT ON DATABASE naia TO naia_readonly;
GRANT USAGE ON SCHEMA public TO naia_readonly;

-- These will be granted after tables are created by migrations
-- ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO naia_readonly;

-- Performance settings (could also be in postgresql.conf)
-- ALTER SYSTEM SET shared_buffers = '256MB';
-- ALTER SYSTEM SET effective_cache_size = '512MB';
-- ALTER SYSTEM SET work_mem = '16MB';

SELECT 'NAIA PostgreSQL initialization complete!' as status;
