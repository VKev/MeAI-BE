CREATE DATABASE aidb;
CREATE DATABASE userdb;

-- Ensure the default postgres user has access
GRANT ALL PRIVILEGES ON DATABASE aidb TO postgres;
GRANT ALL PRIVILEGES ON DATABASE userdb TO postgres;
